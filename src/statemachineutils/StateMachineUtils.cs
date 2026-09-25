using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace StateMachineAutomation
{
    /// <summary>
    /// A finite state machine for Pega Robot Studio: declare states, triggers and guarded transitions once
    /// (JSON or method calls), then <c>Fire</c> triggers and react to events on the automation surface.
    /// One component instance is one machine.
    /// </summary>
    [Description("Models a process as states, triggers and guarded transitions. Declare the machine as JSON or with method calls, Fire triggers, and react to StateEntered/TransitionFired events. Optional file persistence lets a long-running flow resume after a crash. Never throws.")]
    public sealed class StateMachineUtils : Component
    {
        private const int DefaultMaximumHistoryEntries = 100;
        private const int AbsoluteMaximumHistoryEntries = 10000;
        private const int MaxReentrancyDepth = 16;
        private const int PersistedSchemaVersion = 1;
        // Restore and the increment share this ceiling so the component can never write a counter it would then refuse to read.
        private const long MaxSequence = long.MaxValue - 1;

        /// <summary>The run state of the machine. Treated as immutable: every change builds a new instance and swaps it in only after any persistence write succeeded.</summary>
        private sealed class Snapshot
        {
            public bool Started;
            public string Current = string.Empty;
            public DateTime EnteredUtc;
            public long Sequence;
            public Dictionary<string, string> Context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<HistoryEntry> History = new List<HistoryEntry>();

            public Snapshot Clone() => (Snapshot)MemberwiseClone();
        }

        private readonly object syncRoot = new object();
        // Held across a state change AND the delivery of its events, so concurrent callers cannot commit a
        // second transition between the first one's commit and its handlers (which would deliver events
        // out of order and let a handler see a later state than the event it received). Always taken before
        // syncRoot, never the other way round; readers only ever take syncRoot, so they are never blocked by handlers.
        private readonly object dispatchLock = new object();
        private readonly ThreadLocal<int> fireDepth = new ThreadLocal<int>();
        private MachineDefinition definition = new MachineDefinition();
        private Snapshot state = new Snapshot();
        private int maximumHistoryEntries = DefaultMaximumHistoryEntries;
        private bool disposed;

        private string persistFolder;
        private string persistStatePath;
        private string persistMachineName;
        private string persistDefinitionHash;
        private FileStream persistLock;

        /// <summary>Largest saved-state file accepted on restore. Instance-level so a test can lower it without affecting other components.</summary>
        internal int MaxStateFileBytes = 64 * 1024 * 1024;

        /// <summary>Time source; tests substitute a controllable clock.</summary>
        internal Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <summary>Empty constructor required so Pega Robot Studio can create the component.</summary>
        public StateMachineUtils() { }

        /// <summary>Standard designer constructor; attaches the component to a container.</summary>
        public StateMachineUtils(IContainer container) { container?.Add(this); }

        // ================================================================= properties

        /// <summary>Gets the machine's current state, or an empty string if it has not been started.</summary>
        [Category("StateMachine - Status")]
        [Description("The machine's current state, or an empty string if it has not been started. Read-only; wire it to a data port.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string CurrentState
        {
            get { lock (syncRoot) return disposed ? string.Empty : state.Current; }
        }

        /// <summary>Gets whether the machine has been started and is now in a final state.</summary>
        [Category("StateMachine - Status")]
        [Description("True once the machine has been started and has reached a final state. Read-only.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsFinished
        {
            get { lock (syncRoot) return !disposed && IsFinalLocked(state); }
        }

        /// <summary>Gets the machine name declared in the definition's "name" property, or an empty string.</summary>
        [Category("StateMachine - Status")]
        [Description("The name declared in the loaded definition, or an empty string. Read-only.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string MachineName
        {
            get { lock (syncRoot) return disposed ? string.Empty : definition.Name ?? string.Empty; }
        }

        /// <summary>Gets or sets how many history entries the machine keeps (oldest are dropped first).</summary>
        [Category("StateMachine - Configuration")]
        [Description("Maximum number of history entries kept (transitions, rejections, starts). Valid range: 1 through 10,000. Default: 100. Lowering it hides older entries immediately and discards them at the next change.")]
        [DefaultValue(DefaultMaximumHistoryEntries)]
        public int MaximumHistoryEntries
        {
            get { lock (syncRoot) return maximumHistoryEntries; }
            set
            {
                if (value < 1 || value > AbsoluteMaximumHistoryEntries)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaximumHistoryEntries must be between 1 and 10,000.");
                // Only the limit changes here. The stored history is never rewritten by a configuration change:
                // rewriting it would have to be persisted too (and a property setter cannot report a failed
                // write), so instead reads honor the limit immediately and the next real change trims the store.
                lock (syncRoot) maximumHistoryEntries = value;
            }
        }

        /// <summary>Sets <see cref="MaximumHistoryEntries"/> without the possibility of an exception.</summary>
        [Category("StateMachine - Configuration")]
        [Description("Sets the number of history entries kept, from 1 through 10,000. Never throws.")]
        public bool SetMaximumHistoryEntries(int maximumEntries, out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (maximumEntries < 1 || maximumEntries > AbsoluteMaximumHistoryEntries) { message = "maximumEntries must be between 1 and 10,000."; return false; }
                lock (syncRoot)
                {
                    // Same guarantee as every other public method: re-check under the lock Dispose takes, rather than
                    // trusting the fast-path check above, so a disposed component is never changed and reported as success.
                    if (!RequireLiveLocked(out message)) return false;
                    maximumHistoryEntries = maximumEntries;
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetMaximumHistoryEntries), ex); return false; }
        }

        // ================================================================= events

        /// <summary>Raised when the machine leaves a state.</summary>
        [Category("StateMachine - Events")]
        [Description("Raised when the machine leaves a state, before TransitionFired and StateEntered. Carries PreviousState, NewState, Trigger and MillisecondsInState (how long the machine was in the state it is leaving). Fires synchronously on the thread that called Fire.")]
        public event EventHandler<StateMachineExitEventArgs> StateExited;

        /// <summary>Raised once per successful transition.</summary>
        [Category("StateMachine - Events")]
        [Description("Raised once per successful transition. Fires synchronously on the thread that called Fire.")]
        public event EventHandler<StateMachineTransitionEventArgs> TransitionFired;

        /// <summary>Raised when the machine enters a state, including the initial state on Start/Reset.</summary>
        [Category("StateMachine - Events")]
        [Description("Raised when the machine enters a state (also for the initial state on Start or Reset). Fires synchronously on the thread that called Fire, Start or Reset.")]
        public event EventHandler<StateMachineTransitionEventArgs> StateEntered;

        /// <summary>Raised when a trigger is declined (no transition, guard failed, machine finished or not started).</summary>
        [Category("StateMachine - Events")]
        [Description("Raised when a trigger is declined: NoTransition, GuardFailed, Finished or NotStarted. Fires synchronously on the thread that called Fire. A ReentrancyLimit refusal is returned by Fire and recorded in history but deliberately raises no event.")]
        public event EventHandler<StateMachineRejectedEventArgs> TransitionRejected;

        /// <summary>Raised after entering a final state.</summary>
        [Category("StateMachine - Events")]
        [Description("Raised after the machine enters a final state, following StateEntered. Fires synchronously on the thread that called Fire.")]
        public event EventHandler<StateMachineTransitionEventArgs> MachineFinished;

        private void RaiseSafely<TArgs>(EventHandler<TArgs> handler, TArgs args) where TArgs : EventArgs
        {
            if (handler == null) return;
            foreach (EventHandler<TArgs> subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, args); }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    // A faulty subscriber must not stop the others or corrupt the machine, which has
                    // already committed the change before any event is raised.
                    Debug.WriteLine("StateMachineUtils: an event subscriber threw: " + ex.Message);
                }
            }
        }

        // ================================================================= definition

        /// <summary>Validates a JSON definition without loading it and returns a report.</summary>
        [Category("StateMachine - Definition")]
        [Description("Validates a JSON definition without loading it. Returns True with a JSON report {valid, errors, warnings, stateCount, transitionCount}; an invalid definition is reported inside the JSON, not as a failure. Never throws.")]
        public bool ValidateDefinitionJson(string definitionJson, out string reportJson, out string message)
        {
            reportJson = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (string.IsNullOrWhiteSpace(definitionJson)) { message = "definitionJson is required."; return false; }
                DefinitionReport report = StateMachineCore.ParseDefinition(definitionJson);
                reportJson = JsonSerializer.Serialize(new
                {
                    valid = report.Valid,
                    errors = report.Errors,
                    warnings = report.Warnings,
                    stateCount = report.Definition?.States.Count ?? 0,
                    transitionCount = report.Definition?.Transitions.Count ?? 0
                }, StateMachineCore.CompactOptions);
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ValidateDefinitionJson), ex); return false; }
        }

        /// <summary>Replaces the machine's definition with a validated JSON definition.</summary>
        [Category("StateMachine - Definition")]
        [Description("Parses and validates a JSON definition, then replaces the current one and stops the machine (context is kept). An invalid definition is rejected whole and the previous definition stays in force. Waits for another thread's event handlers to finish; refused when called from inside an event handler. Never throws.")]
        public bool LoadDefinitionJson(string definitionJson, out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!RequireNotInsideEvent(nameof(LoadDefinitionJson), out message)) return false;
                // Replacing the definition stops the machine, so it must not happen in the middle of another
                // thread's event delivery: take the dispatch lock (waiting for those handlers) before syncRoot.
                lock (dispatchLock)
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (!RequireDefinitionEditable(true, out message)) return false;
                    DefinitionReport report = StateMachineCore.ParseDefinition(definitionJson);
                    if (!report.Valid) { message = "The definition is not valid: " + JoinProblems(report.Errors); return false; }
                    definition = report.Definition;
                    StopMachineLocked();
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(LoadDefinitionJson), ex); return false; }
        }

        /// <summary>Returns the current definition as JSON.</summary>
        [Category("StateMachine - Definition")]
        [Description("Returns the current definition (loaded from JSON or built with AddState/AddTransition) as JSON. Never throws.")]
        public bool GetDefinitionJson(out string definitionJson, out string message)
        {
            definitionJson = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    definitionJson = definition.ToJson(true);
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetDefinitionJson), ex); return false; }
        }

        /// <summary>Adds a state to the definition being built.</summary>
        [Category("StateMachine - Definition")]
        [Description("Adds a state. State names are case-insensitive and must be unique. A final state ends the machine. Never throws.")]
        public bool AddState(string name, out string message, bool isFinal = false)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (!RequireDefinitionEditable(false, out message)) return false;
                    if (!MachineDefinition.IsValidName(name, out string problem)) { message = "name " + problem + "."; return false; }
                    string trimmed = name.Trim();
                    if (trimmed == MachineDefinition.Wildcard) { message = "'*' is reserved for transitions and cannot be a state name."; return false; }
                    if (definition.FindState(trimmed) != null) { message = "A state named '" + definition.FindState(trimmed).Name + "' already exists (state names are case-insensitive)."; return false; }
                    if (definition.States.Count >= MachineDefinition.MaxStates) { message = "A machine may have at most " + MachineDefinition.MaxStates + " states."; return false; }
                    definition.States.Add(new StateDef { Name = trimmed, Final = isFinal });
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddState), ex); return false; }
        }

        /// <summary>Sets the state the machine enters on Start.</summary>
        [Category("StateMachine - Definition")]
        [Description("Sets the initial state, which must already have been added and must not be final. Never throws.")]
        public bool SetInitialState(string name, out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (!RequireDefinitionEditable(false, out message)) return false;
                    StateDef found = definition.FindState(name?.Trim());
                    if (found == null) { message = "'" + name + "' is not a declared state; call AddState first."; return false; }
                    if (found.Final) { message = "The initial state cannot be a final state."; return false; }
                    definition.Initial = found.Name;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetInitialState), ex); return false; }
        }

        /// <summary>Adds a transition, optionally with one guard.</summary>
        [Category("StateMachine - Definition")]
        [Description("Adds a transition from a state (or '*' for any non-final state) on a trigger to another state, optionally with one guard (guardKey + guardOp [+ guardValue]; leave guardOp at None for no guard). Transitions for the same state and trigger are tried in the order added; the first whose guard passes wins. Use LoadDefinitionJson for several guards on one transition. Never throws.")]
        public bool AddTransition(string fromState, string trigger, string toState, out string message, string guardKey = null, GuardOperator guardOp = GuardOperator.None, string guardValue = null)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (!RequireDefinitionEditable(false, out message)) return false;
                    if (definition.Transitions.Count >= MachineDefinition.MaxTransitions) { message = "A machine may have at most " + MachineDefinition.MaxTransitions + " transitions."; return false; }

                    var transition = new TransitionDef { From = fromState?.Trim(), Trigger = trigger?.Trim(), To = toState?.Trim() };
                    StateDef from = transition.From == MachineDefinition.Wildcard ? null : definition.FindState(transition.From);
                    if (from != null) transition.From = from.Name;
                    StateDef to = definition.FindState(transition.To);
                    if (to != null) transition.To = to.Name;

                    bool anyGuardArg = !string.IsNullOrWhiteSpace(guardKey) || guardOp != GuardOperator.None || guardValue != null;
                    if (anyGuardArg)
                    {
                        if (string.IsNullOrWhiteSpace(guardKey) || guardOp == GuardOperator.None) { message = "A guard needs both guardKey and guardOp (and guardValue for every operator except Exists/NotExists)."; return false; }
                        string op = MachineDefinition.OperatorName(guardOp);
                        if (op == null) { message = "guardOp " + (int)guardOp + " is not a known GuardOperator."; return false; }
                        transition.Guards.Add(new GuardDef { Key = guardKey.Trim(), Op = op, Value = guardValue });
                    }

                    var report = new DefinitionReport();
                    definition.ValidateTransition(transition, "The transition", report);
                    if (!report.Valid) { message = JoinProblems(report.Errors); return false; }
                    definition.Transitions.Add(transition);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddTransition), ex); return false; }
        }

        /// <summary>Adds an unconditional transition: from, trigger and to, nothing else.</summary>
        [Category("StateMachine - Definition")]
        [Description("Adds an unconditional transition from a state (or '*' for any non-final state) on a trigger to another state. The minimal AddTransition: use AddTransition when the transition needs a guard. Transitions for the same state and trigger are tried in the order added. Never throws.")]
        public bool AddTransitionSimple(string fromState, string trigger, string toState, out string message)
        {
            message = null;
            try { return AddTransition(fromState, trigger, toState, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddTransitionSimple), ex); return false; }
        }

        /// <summary>Removes every state and transition and stops the machine.</summary>
        [Category("StateMachine - Definition")]
        [Description("Removes the whole definition and stops the machine (context is kept). Not allowed while persistence is enabled. Waits for another thread's event handlers to finish; refused when called from inside an event handler. Never throws.")]
        public bool ClearDefinition(out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!RequireNotInsideEvent(nameof(ClearDefinition), out message)) return false;
                lock (dispatchLock)
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (!RequireDefinitionEditable(true, out message)) return false;
                    definition = new MachineDefinition();
                    StopMachineLocked();
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ClearDefinition), ex); return false; }
        }

        // ================================================================= run

        /// <summary>Starts the machine in its initial state.</summary>
        [Category("StateMachine - Run")]
        [Description("Validates the definition and enters the initial state, raising StateEntered. Fails if the machine is already started (use Reset to restart). Context set beforehand is kept. Never throws.")]
        public bool Start(out string currentState, out string message) => StartCore(false, false, nameof(Start), out currentState, out message);

        /// <summary>Returns the machine to its initial state.</summary>
        [Category("StateMachine - Run")]
        [Description("Returns the machine to its initial state (also starts a machine that has not been started), clears history, and raises StateEntered. Set clearContext True to also clear the context. Never throws.")]
        public bool Reset(bool clearContext, out string currentState, out string message) => StartCore(true, clearContext, nameof(Reset), out currentState, out message);

        private bool StartCore(bool isReset, bool clearContext, string operation, out string currentState, out string message)
        {
            currentState = null;
            message = null;
            bool depthTaken = false;
            try
            {
                if (!RequireLive(out message)) return false;
                if (fireDepth.Value >= MaxReentrancyDepth) { message = "The re-entrancy limit (" + MaxReentrancyDepth + ") was reached: a handler keeps calling Start/Reset/Fire from inside an event."; return false; }
                fireDepth.Value++;
                depthTaken = true;

                var pending = new List<Action>();
                lock (dispatchLock)
                {
                    lock (syncRoot)
                    {
                        if (!RequireLiveLocked(out message)) return false;
                        if (!isReset && state.Started) { message = "The machine is already started; call Reset to restart it."; return false; }
                        var report = new DefinitionReport();
                        definition.Validate(report);
                        if (!report.Valid) { message = "The definition is not valid: " + JoinProblems(report.Errors); return false; }

                        string initial = definition.FindState(definition.Initial).Name;
                        Snapshot next = state.Clone();
                        next.Started = true;
                        next.Current = initial;
                        next.EnteredUtc = Clock();
                        next.History = new List<HistoryEntry>();
                        next.Sequence = 0; // the history it numbered is gone, so the numbering restarts (and an exhausted counter recovers)
                        if (clearContext) next.Context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        AddHistory(next, isReset ? "reset" : "start", null, null, initial, null, null);
                        if (!PersistLocked(next, out message)) return false;

                        state = next;
                        currentState = initial;
                        pending.Add(() => RaiseSafely(StateEntered, new StateMachineTransitionEventArgs(string.Empty, initial, string.Empty)));
                    }
                    foreach (Action raise in pending) raise();
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { currentState = null; message = NeverThrowsGuard.Failure(operation, ex); return false; }
            finally { if (depthTaken) ReleaseDepth(); }
        }

        /// <summary>Fires a trigger.</summary>
        [Category("StateMachine - Run")]
        [Description("Fires a trigger. True means the call worked: if message is empty the trigger fired (newState is the state entered); if message has text it is the reason the trigger was declined (NoTransition, GuardFailed, Finished, NotStarted or ReentrancyLimit) and newState is the state the machine is still in. False means the call failed (bad input, no definition, or a persistence write failure): message says what was wrong and the machine is unchanged. Events are raised synchronously on this thread after the change is committed; concurrent Fire/Start/Reset calls from other threads wait until those handlers finish, so events always arrive in transition order. Never throws.")]
        public bool Fire(string trigger, out string newState, out string message)
        {
            bool fired = false;
            string rejectionReason = null;
            newState = null;
            rejectionReason = null;
            message = null;
            bool depthTaken = false;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!MachineDefinition.IsValidName(trigger, out string problem)) { message = "trigger " + problem + "."; return false; }
                string trimmedTrigger = trigger.Trim();

                if (fireDepth.Value >= MaxReentrancyDepth)
                {
                    // Recorded but deliberately not raised as an event: a TransitionRejected handler that
                    // fires again would otherwise recurse without bound.
                    lock (syncRoot)
                    {
                        if (!RequireLiveLocked(out message)) return false;
                        newState = state.Current;
                        Snapshot next = state.Clone();
                        AddHistory(next, "rejected", trimmedTrigger, state.Current, null, "ReentrancyLimit", "Trigger '" + trimmedTrigger + "' was declined: the re-entrancy limit (" + MaxReentrancyDepth + ") was reached.");
                        state = next;
                    }
                    message = "ReentrancyLimit";
                    return true;
                }
                fireDepth.Value++;
                depthTaken = true;

                var pending = new List<Action>();
                lock (dispatchLock)
                {
                    lock (syncRoot)
                    {
                        if (!RequireLiveLocked(out message)) return false;
                        if (definition.States.Count == 0) { message = "No definition has been loaded; call LoadDefinitionJson or AddState first."; return false; }

                        Snapshot current = state;
                        string reason = null;
                        string detail = null;
                        TransitionDef transition = null;

                        if (!current.Started) { reason = "NotStarted"; detail = "The machine has not been started; call Start."; }
                        else if (IsFinalLocked(current)) { reason = "Finished"; detail = "The machine is in final state '" + current.Current + "' and accepts no more triggers."; }
                        else transition = StateMachineCore.FindTransition(definition, current.Current, trimmedTrigger, current.Context, out reason, out detail);

                        if (transition == null)
                        {
                            Snapshot rejected = current.Clone();
                            AddHistory(rejected, "rejected", trimmedTrigger, current.Started ? current.Current : null, null, reason, detail);
                            state = rejected;
                            newState = current.Current;
                            rejectionReason = reason;
                            string rejectedFrom = current.Current;
                            pending.Add(() => RaiseSafely(TransitionRejected, new StateMachineRejectedEventArgs(rejectedFrom, trimmedTrigger, reason, detail)));
                        }
                        else
                        {
                            string from = current.Current;
                            StateDef target = definition.FindState(transition.To);
                            string to = target.Name;
                            string firedTrigger = transition.Trigger;

                            Snapshot next = current.Clone();
                            next.Current = to;
                            next.EnteredUtc = Clock();
                            double millisecondsInState = Math.Max(0, Math.Round((next.EnteredUtc - current.EnteredUtc).TotalMilliseconds));
                            AddHistory(next, "transition", firedTrigger, from, to, null, null);
                            if (!PersistLocked(next, out message)) return false;

                            state = next;
                            fired = true;
                            newState = to;
                            pending.Add(() => RaiseSafely(StateExited, new StateMachineExitEventArgs(from, to, firedTrigger, millisecondsInState)));
                            pending.Add(() => RaiseSafely(TransitionFired, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                            pending.Add(() => RaiseSafely(StateEntered, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                            if (target.Final)
                                pending.Add(() => RaiseSafely(MachineFinished, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                        }
                    }
                    foreach (Action raise in pending) raise();
                }
                message = fired ? null : rejectionReason;   // empty = fired; text = why it was declined
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                newState = null;
                message = NeverThrowsGuard.Failure(nameof(Fire), ex);
                return false;
            }
            finally { if (depthTaken) ReleaseDepth(); }
        }

        /// <summary>The minimal Fire: a trigger in, a result and a message out.</summary>
        [Category("StateMachine - Run")]
        [Description("Fires a trigger with just a result and a message. True means the call worked: if message is empty the trigger fired, otherwise message is the reason it was declined (NoTransition, GuardFailed, Finished, NotStarted or ReentrancyLimit) and nothing changed. False means the call failed (bad input, no definition, or a persistence write failure) and message says what was wrong. Use Fire when you also need the state. Never throws.")]
        public bool FireSimple(string trigger, out string message)
        {
            message = null;
            try { return Fire(trigger, out _, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(FireSimple), ex); return false; }
        }

        /// <summary>Reports whether a trigger would currently fire, without firing it.</summary>
        [Category("StateMachine - Run")]
        [Description("Reports whether a trigger would fire right now (guards evaluated) without firing it or raising events. When canFire is False, rejectionReason says why. Never throws.")]
        public bool CanFire(string trigger, out bool canFire, out string rejectionReason, out string message)
        {
            canFire = false;
            rejectionReason = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!MachineDefinition.IsValidName(trigger, out string problem)) { message = "trigger " + problem + "."; return false; }
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (definition.States.Count == 0) { message = "No definition has been loaded; call LoadDefinitionJson or AddState first."; return false; }
                    if (!state.Started) { rejectionReason = "NotStarted"; return true; }
                    if (IsFinalLocked(state)) { rejectionReason = "Finished"; return true; }
                    TransitionDef t = StateMachineCore.FindTransition(definition, state.Current, trigger.Trim(), state.Context, out string reason, out _);
                    canFire = t != null;
                    rejectionReason = reason;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { canFire = false; rejectionReason = null; message = NeverThrowsGuard.Failure(nameof(CanFire), ex); return false; }
        }

        // ================================================================= query

        /// <summary>Gets the current state.</summary>
        [Category("StateMachine - Query")]
        [Description("Gets the current state; an empty string if the machine has not been started. Never throws.")]
        public bool GetCurrentState(out string currentState, out string message)
        {
            currentState = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    currentState = state.Current;
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { currentState = null; message = NeverThrowsGuard.Failure(nameof(GetCurrentState), ex); return false; }
        }

        /// <summary>Reports whether the machine has been started.</summary>
        [Category("StateMachine - Query")]
        [Description("Returns True in isStarted once Start (or Reset) has been called. Never throws.")]
        public bool IsStarted(out bool isStarted, out string message)
        {
            isStarted = false;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    isStarted = state.Started;
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { isStarted = false; message = NeverThrowsGuard.Failure(nameof(IsStarted), ex); return false; }
        }

        /// <summary>Reports whether the machine is in a final state.</summary>
        [Category("StateMachine - Query")]
        [Description("Returns True in isFinal once the machine has reached a final state (False if it has not been started). Never throws.")]
        public bool IsInFinalState(out bool isFinal, out string message)
        {
            isFinal = false;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    isFinal = IsFinalLocked(state);
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { isFinal = false; message = NeverThrowsGuard.Failure(nameof(IsInFinalState), ex); return false; }
        }

        /// <summary>Lists the triggers that would fire right now, joined by a delimiter.</summary>
        [Category("StateMachine - Query")]
        [Description("Lists the triggers that would fire from the current state right now (guards evaluated), joined by the delimiter (default comma). Empty when none. Requires a started machine. Never throws.")]
        public bool GetAvailableTriggersDelimited(out string triggers, out string message, string delimiter = ",")
        {
            triggers = null;
            message = null;
            try
            {
                if (!TryGetAvailable(out List<string> available, out message)) return false;
                triggers = string.Join(string.IsNullOrEmpty(delimiter) ? "," : delimiter, available);
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { triggers = null; message = NeverThrowsGuard.Failure(nameof(GetAvailableTriggersDelimited), ex); return false; }
        }

        /// <summary>Lists the triggers that would fire right now, as a JSON array.</summary>
        [Category("StateMachine - Query")]
        [Description("Lists the triggers that would fire from the current state right now (guards evaluated) as a JSON array of strings. Requires a started machine. Never throws.")]
        public bool GetAvailableTriggersJson(out string json, out string message)
        {
            json = null;
            message = null;
            try
            {
                if (!TryGetAvailable(out List<string> available, out message)) return false;
                json = JsonSerializer.Serialize(available, StateMachineCore.CompactOptions);
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { json = null; message = NeverThrowsGuard.Failure(nameof(GetAvailableTriggersJson), ex); return false; }
        }

        private bool TryGetAvailable(out List<string> available, out string message)
        {
            available = null;
            message = null;
            if (!RequireLive(out message)) return false;
            lock (syncRoot)
            {
                if (!RequireLiveLocked(out message)) return false;
                if (!state.Started) { message = "The machine has not been started; call Start."; return false; }
                available = IsFinalLocked(state) ? new List<string>() : StateMachineCore.AvailableTriggers(definition, state.Current, state.Context);
                return true;
            }
        }

        /// <summary>Gets how long the machine has been in its current state.</summary>
        [Category("StateMachine - Query")]
        [Description("Gets the seconds spent in the current state - poll it to detect a stuck step, since the component runs no timers. Requires a started machine. Never throws.")]
        public bool GetSecondsInState(out double seconds, out string message)
        {
            seconds = 0;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (!state.Started) { message = "The machine has not been started; call Start."; return false; }
                    seconds = Math.Max(0, (Clock() - state.EnteredUtc).TotalSeconds);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { seconds = 0; message = NeverThrowsGuard.Failure(nameof(GetSecondsInState), ex); return false; }
        }

        /// <summary>Gets the most recent history entries as JSON, oldest first.</summary>
        [Category("StateMachine - Query")]
        [Description("Gets the most recent history entries as a JSON array, oldest first: [{seq, utc, kind (start|reset|transition|rejected), trigger, from, to, reason, detail}]. History is bounded by MaximumHistoryEntries. Never throws.")]
        public bool GetHistoryJson(out string json, out string message, int maxEntries = 100)
        {
            json = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (maxEntries < 1 || maxEntries > AbsoluteMaximumHistoryEntries) { message = "maxEntries must be between 1 and 10,000."; return false; }
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    int take = Math.Min(maxEntries, maximumHistoryEntries);
                    IEnumerable<HistoryEntry> slice = state.History.Skip(Math.Max(0, state.History.Count - take));
                    json = JsonSerializer.Serialize(slice.ToList(), StateMachineCore.CompactOptions);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { json = null; message = NeverThrowsGuard.Failure(nameof(GetHistoryJson), ex); return false; }
        }

        // ================================================================= context

        /// <summary>Sets a context value that guards can test.</summary>
        [Category("StateMachine - Context")]
        [Description("Sets a context key to a text value that transition guards can test. Keys are case-insensitive. Do not store secrets here: context is written to disk in plain text when persistence is enabled. Never throws.")]
        public bool SetContext(string key, string value, out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!MachineDefinition.IsValidName(key, out string problem)) { message = "key " + problem + "."; return false; }
                if (value == null) { message = "value is required; use RemoveContext to delete a key."; return false; }
                if (value.Length > StateMachineCore.MaxContextValueLength) { message = "value is longer than " + StateMachineCore.MaxContextValueLength + " characters."; return false; }
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    string trimmedKey = key.Trim();
                    if (!state.Context.ContainsKey(trimmedKey) && state.Context.Count >= StateMachineCore.MaxContextEntries) { message = "The context is full (" + StateMachineCore.MaxContextEntries + " keys)."; return false; }
                    Snapshot next = state.Clone();
                    next.Context = new Dictionary<string, string>(state.Context, StringComparer.OrdinalIgnoreCase) { [trimmedKey] = value };
                    EnforceHistoryLimit(next);
                    if (!PersistLocked(next, out message)) return false;
                    state = next;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetContext), ex); return false; }
        }

        /// <summary>Reads a context value.</summary>
        [Category("StateMachine - Context")]
        [Description("Reads a context value. A missing key is a normal outcome: True with exists False. Never throws.")]
        public bool GetContext(string key, out bool exists, out string value, out string message)
        {
            exists = false;
            value = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!MachineDefinition.IsValidName(key, out string problem)) { message = "key " + problem + "."; return false; }
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    exists = state.Context.TryGetValue(key.Trim(), out value);
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { exists = false; value = null; message = NeverThrowsGuard.Failure(nameof(GetContext), ex); return false; }
        }

        /// <summary>Removes a context key.</summary>
        [Category("StateMachine - Context")]
        [Description("Removes a context key. A missing key is a normal outcome: True with removed False. Never throws.")]
        public bool RemoveContext(string key, out bool removed, out string message)
        {
            removed = false;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!MachineDefinition.IsValidName(key, out string problem)) { message = "key " + problem + "."; return false; }
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    string trimmedKey = key.Trim();
                    if (!state.Context.ContainsKey(trimmedKey)) return true;
                    Snapshot next = state.Clone();
                    var copy = new Dictionary<string, string>(state.Context, StringComparer.OrdinalIgnoreCase);
                    copy.Remove(trimmedKey);
                    next.Context = copy;
                    EnforceHistoryLimit(next);
                    if (!PersistLocked(next, out message)) return false;
                    state = next;
                    removed = true;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { removed = false; message = NeverThrowsGuard.Failure(nameof(RemoveContext), ex); return false; }
        }

        /// <summary>Removes every context key.</summary>
        [Category("StateMachine - Context")]
        [Description("Removes every context key. Never throws.")]
        public bool ClearContext(out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (state.Context.Count == 0) return true;
                    Snapshot next = state.Clone();
                    next.Context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    EnforceHistoryLimit(next);
                    if (!PersistLocked(next, out message)) return false;
                    state = next;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ClearContext), ex); return false; }
        }

        /// <summary>Gets the whole context as a JSON object.</summary>
        [Category("StateMachine - Context")]
        [Description("Gets the whole context as a JSON object of key/value text, keys sorted. Never throws.")]
        public bool GetContextJson(out string json, out string message)
        {
            json = null;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    var sorted = new SortedDictionary<string, string>(state.Context, StringComparer.OrdinalIgnoreCase);
                    json = JsonSerializer.Serialize(sorted, StateMachineCore.CompactOptions);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { json = null; message = NeverThrowsGuard.Failure(nameof(GetContextJson), ex); return false; }
        }

        // ================================================================= persistence

        /// <summary>Makes the machine's run state durable so a crashed flow can resume.</summary>
        [Category("StateMachine - Persistence")]
        [Description("Persists the machine's state, context and history under LocalApplicationData so a flow can resume after a crash or restart. Call after the definition is loaded and before Start. Waits for another thread's event handlers to finish; refused when called from inside an event handler. If saved state for this machineName exists and matches the definition it is restored (restored True) without raising events; a saved state from a different definition is refused. From then on every change is written first and only committed if the write succeeds. Never throws.")]
        public bool EnablePersistence(string machineName, out string statePath, out bool restored, out string message)
        {
            statePath = null;
            restored = false;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!RequireNotInsideEvent(nameof(EnablePersistence), out message)) return false;
                if (!StateMachineCore.TryResolveMachineFolder(machineName, out string folder, out message)) return false;

                // A restore replaces the run state wholesale, so like LoadDefinitionJson it must wait for another
                // thread's event delivery (dispatchLock before syncRoot) and cannot run from inside a handler.
                lock (dispatchLock)
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (persistStatePath != null) { message = "Persistence is already enabled for this component; call DisablePersistence first."; return false; }
                    if (state.Started) { message = "Enable persistence before Start, so a saved state can be restored instead of overwritten."; return false; }
                    var report = new DefinitionReport();
                    definition.Validate(report);
                    if (!report.Valid) { message = "The definition is not valid: " + JoinProblems(report.Errors); return false; }

                    Directory.CreateDirectory(folder);
                    FileStream lockStream;
                    try { lockStream = new FileStream(Path.Combine(folder, ".machine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException) { message = "Persistence for '" + machineName.Trim() + "' is already open by another StateMachineUtils component or process."; return false; }

                    bool keep = false;
                    try
                    {
                        string path = Path.Combine(folder, "state.json");
                        string hash = definition.ComputeHash();
                        Snapshot next;
                        bool didRestore = false;

                        // We hold the ownership lock, so no other writer exists and any temp file left by a crash
                        // mid-write (state.json.<guid>.tmp) is dead weight: sweep it.
                        foreach (string stale in Directory.EnumerateFiles(folder, "state.json.*.tmp"))
                        {
                            try { File.Delete(stale); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* best effort; a leftover is harmless */ }
                        }

                        if (File.Exists(path))
                        {
                            // A restore replaces the whole run state with the saved one. Context set beforehand would be
                            // dropped without a trace, so refuse and say how to do it safely.
                            if (state.Context.Count > 0)
                            {
                                message = "A saved run exists for '" + machineName.Trim() + "', and restoring it would discard the " + state.Context.Count + " context value(s) already set on this component. Call SetContext after EnablePersistence (or ClearContext first) so nothing is lost silently.";
                                return false;
                            }
                            if (!TryLoadSaved(path, machineName.Trim(), hash, out next, out message)) return false;
                            didRestore = true;
                        }
                        else { next = state.Clone(); EnforceHistoryLimit(next); }

                        // Publish the persistence fields first so the initial write goes through the same
                        // durable-first path as every later change, then roll them back if it fails.
                        persistFolder = folder;
                        persistStatePath = path;
                        persistMachineName = machineName.Trim();
                        persistDefinitionHash = hash;
                        persistLock = lockStream;
                        if (!PersistLocked(next, out message))
                        {
                            ClearPersistenceFields(false);
                            return false;
                        }

                        state = next;
                        statePath = path;
                        restored = didRestore;
                        keep = true;
                        return true;
                    }
                    finally { if (!keep) lockStream.Dispose(); }
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                statePath = null;
                restored = false;
                message = NeverThrowsGuard.Failure(nameof(EnablePersistence), ex);
                return false;
            }
        }

        /// <summary>Stops persisting; the saved state stays on disk.</summary>
        [Category("StateMachine - Persistence")]
        [Description("Stops persisting and releases the saved state's ownership lock. The saved file is kept so a later EnablePersistence can resume it. Harmless if persistence was not enabled. Never throws.")]
        public bool DisablePersistence(out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    ClearPersistenceFields(true);
                }
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(DisablePersistence), ex); return false; }
        }

        /// <summary>Deletes a machine's saved state.</summary>
        [Category("StateMachine - Persistence")]
        [Description("Deletes the saved state for a machineName - use it to abandon a saved run, or when EnablePersistence refuses a state saved under a different definition. Refused while this component has that machine's persistence enabled or another owner holds it. A missing saved state is a normal outcome: True with discarded False. Never throws.")]
        public bool DiscardPersistedState(string machineName, out bool discarded, out string message)
        {
            discarded = false;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!StateMachineCore.TryResolveMachineFolder(machineName, out string folder, out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireLiveLocked(out message)) return false;
                    if (persistFolder != null && string.Equals(persistFolder, folder, StringComparison.OrdinalIgnoreCase)) { message = "Persistence is enabled for '" + machineName.Trim() + "' on this component; call DisablePersistence first."; return false; }
                    if (!Directory.Exists(folder)) return true;

                    FileStream lockStream;
                    try { lockStream = new FileStream(Path.Combine(folder, ".machine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException) { message = "The saved state for '" + machineName.Trim() + "' is in use by another StateMachineUtils component or process."; return false; }

                    // Only the saved state is removed, and only while the ownership lock is held. The lock marker
                    // and the folder are deliberately left behind: deleting them after releasing the lock would race
                    // with a new owner acquiring it in that gap - on Unix an unlinked-but-open lock file lets the
                    // next caller create a second lock file, giving two owners - and they cost nothing to keep.
                    using (lockStream)
                    {
                        string path = Path.Combine(folder, "state.json");
                        if (File.Exists(path)) { File.Delete(path); discarded = true; }
                    }
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { discarded = false; message = NeverThrowsGuard.Failure(nameof(DiscardPersistedState), ex); return false; }
        }

        private bool TryLoadSaved(string path, string requestedName, string definitionHash, out Snapshot restored, out string message)
        {
            restored = null;
            message = null;
            long length = new FileInfo(path).Length;
            if (length > MaxStateFileBytes) { message = "The saved state is " + length + " bytes, larger than the " + MaxStateFileBytes + "-byte limit this component will load; call DiscardPersistedState to start fresh."; return false; }
            string text = File.ReadAllText(path);
            string duplicate = StateMachineCore.FindDuplicateSavedField(text);
            if (duplicate != null) { message = "The saved state is corrupt (" + duplicate + ", so which value is right cannot be told); call DiscardPersistedState to start fresh."; return false; }
            PersistedState saved;
            try { saved = JsonSerializer.Deserialize<PersistedState>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException ex) { message = "The saved state is corrupt (" + ex.Message + "); call DiscardPersistedState to start fresh."; return false; }
            if (saved == null) { message = "The saved state is empty; call DiscardPersistedState to start fresh."; return false; }

            // Every field this component writes must be present and well-formed. A file that parses as JSON but
            // is missing pieces (truncated by hand or by another tool) is corrupt, not "a fresh machine": accepting
            // it would silently reset the context or history, invent a time-in-state, or - for a missing 'started' -
            // restore a running machine as unstarted and then rewrite the damaged file over the real one.
            const string discard = "; call DiscardPersistedState to start fresh.";
            var missing = new List<string>();
            if (saved.schemaVersion == null) missing.Add("schemaVersion");
            if (saved.machineName == null) missing.Add("machineName");
            if (saved.definitionHash == null) missing.Add("definitionHash");
            if (saved.started == null) missing.Add("started");
            if (saved.currentState == null) missing.Add("currentState");
            if (saved.enteredUtc == null) missing.Add("enteredUtc");
            if (saved.sequence == null) missing.Add("sequence");
            if (saved.context == null) missing.Add("context");
            if (saved.history == null) missing.Add("history");
            if (missing.Count > 0) { message = "The saved state is incomplete (it has no " + string.Join(" and no ", missing.Select(m => "'" + m + "'")) + ")" + discard; return false; }

            if (saved.schemaVersion.Value != PersistedSchemaVersion) { message = "The saved state uses schema version " + saved.schemaVersion.Value + ", which this component does not understand" + discard; return false; }
            // The folder name is the machine's identity. A state.json copied in from another machine's folder would
            // otherwise resume under the wrong name whenever the two share a definition.
            if (!string.Equals(saved.machineName, requestedName, StringComparison.OrdinalIgnoreCase))
            { message = "The saved state belongs to machine '" + saved.machineName + "', not '" + requestedName + "' (was the file copied or renamed?)" + discard; return false; }
            if (!string.Equals(saved.definitionHash, definitionHash, StringComparison.Ordinal)) { message = "The saved state was made with a different definition; call DiscardPersistedState to abandon it, or load the original definition."; return false; }

            bool savedStarted = saved.started.Value;
            long savedSequence = saved.sequence.Value;
            // The counter's ceiling is refused, not just negatives: the next history entry increments it, and a value
            // at long.MaxValue would wrap to a negative number that a later restore refuses.
            if (savedSequence < 0 || savedSequence > MaxSequence) { message = "The saved state has an invalid sequence number (" + savedSequence + ")" + discard; return false; }
            if (!savedStarted && saved.currentState.Length != 0) { message = "The saved state is inconsistent: it is not started but names a current state ('" + saved.currentState + "')" + discard; return false; }
            if (saved.history.Any(h => h == null)) { message = "The saved state has an empty history entry" + discard; return false; }
            if (saved.history.Count > AbsoluteMaximumHistoryEntries) { message = "The saved state has " + saved.history.Count + " history entries, more than the " + AbsoluteMaximumHistoryEntries + " this component ever writes" + discard; return false; }
            if (saved.context.Count > StateMachineCore.MaxContextEntries) { message = "The saved state has " + saved.context.Count + " context keys, more than the " + StateMachineCore.MaxContextEntries + " allowed" + discard; return false; }
            string historyProblem = ValidateSavedHistory(saved.history, savedSequence, savedStarted, saved.currentState);
            if (historyProblem != null) { message = "The saved state has an invalid history (" + historyProblem + ")" + discard; return false; }

            // The timestamp is a required field of every snapshot, so it must parse whether or not the machine is
            // running; it only means something (time in the current state) for a run that has started.
            if (string.IsNullOrWhiteSpace(saved.enteredUtc)
                || !DateTime.TryParse(saved.enteredUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime entered))
            {
                message = "The saved state has a missing or invalid 'enteredUtc' timestamp ('" + saved.enteredUtc + "')" + discard;
                return false;
            }

            var snapshot = new Snapshot { Started = savedStarted, Sequence = savedSequence };
            if (savedStarted)
            {
                if (string.IsNullOrWhiteSpace(saved.currentState)) { message = "The saved state is incomplete (a started machine has no 'currentState')" + discard; return false; }
                StateDef current = definition.FindState(saved.currentState);
                if (current == null) { message = "The saved state names '" + saved.currentState + "', which is not a state of this definition" + discard; return false; }
                snapshot.Current = current.Name;
                snapshot.EnteredUtc = entered.ToUniversalTime();
            }

            foreach (KeyValuePair<string, string> entry in saved.context)
            {
                // The same shape SetContext enforces: a saved context is untrusted input, since the file can be
                // stale or edited, and anything SetContext would refuse must not sneak in through a restore.
                if (!MachineDefinition.IsValidName(entry.Key, out string keyProblem) || entry.Key != entry.Key.Trim())
                { message = "The saved state has a context key that " + (keyProblem ?? "has leading or trailing spaces") + discard; return false; }
                if (entry.Value == null) { message = "The saved state has a null context value for '" + entry.Key + "'" + discard; return false; }
                if (entry.Value.Length > StateMachineCore.MaxContextValueLength) { message = "The saved state has a value for context key '" + entry.Key + "' longer than " + StateMachineCore.MaxContextValueLength + " characters" + discard; return false; }
                if (snapshot.Context.ContainsKey(entry.Key)) { message = "The saved state has two context keys that differ only by case ('" + entry.Key + "')" + discard; return false; }
                snapshot.Context[entry.Key] = entry.Value;
            }
            snapshot.History = saved.history.Skip(Math.Max(0, saved.history.Count - maximumHistoryEntries)).ToList();
            restored = snapshot;
            return true;
        }

        private static readonly string[] HistoryKinds = { "start", "reset", "transition", "rejected" };
        // The only rejection codes schema version 1 ever writes; anything else is not from this component.
        private static readonly string[] RejectionReasons = { "NoTransition", "GuardFailed", "Finished", "NotStarted", "ReentrancyLimit" };

        /// <summary>
        /// Checks a saved history against the invariants everything this component writes satisfies, so an edited or
        /// damaged file cannot smuggle in records that <see cref="GetHistoryJson"/> would then serve as fact and that
        /// the next change would build on: every entry has a known kind, a parseable timestamp and the fields its kind
        /// needs, names only states the definition declares, sequence numbers are positive and strictly increasing, and
        /// the last one equals the top-level sequence counter (every entry is stamped with the counter's new value), so
        /// the next entry can never reuse or overflow a number. Returns null when valid, otherwise what is wrong.
        /// </summary>
        private string ValidateSavedHistory(List<HistoryEntry> history, long sequence, bool started, string savedCurrentState)
        {
            const int MaxDetail = 16384;
            // An empty history is only ever legitimate for a machine that has never recorded anything: not started, counter
            // zero. A started machine always has at least its start entry, and a counter above zero means entries existed.
            // Accepting an empty list otherwise would let an edit erase the audit trail and still resume.
            if (history.Count == 0 && (started || sequence != 0))
                return started
                    ? "a started machine has no history entries, but it always has at least its start entry"
                    : "there are no history entries, but the saved sequence counter is " + sequence;
            long previous = 0;
            string lastMoveTo = null;
            for (int i = 0; i < history.Count; i++)
            {
                HistoryEntry h = history[i];
                string at = "entry " + (i + 1);
                if (h.seq < 1) return at + " has a sequence number below 1";
                // Every entry is stamped with the counter's next value and only the oldest are ever dropped, so the
                // retained entries run consecutively; a gap or repeat means records were removed, added or reordered.
                if (i > 0 && h.seq != previous + 1) return at + " has sequence number " + h.seq + " but the entry before it is " + previous + " (sequence numbers must run consecutively)";
                previous = h.seq;
                if (!HistoryKinds.Contains(h.kind)) return at + " has an unknown kind '" + h.kind + "'";
                if (string.IsNullOrWhiteSpace(h.utc) || !DateTime.TryParse(h.utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
                    return at + " has a missing or invalid timestamp";
                foreach (string name in new[] { h.trigger, h.reason })
                    if (name != null && name.Length > MachineDefinition.MaxNameLength) return at + " has a field longer than " + MachineDefinition.MaxNameLength + " characters";
                if (h.detail != null && h.detail.Length > MaxDetail) return at + " has a detail longer than " + MaxDetail + " characters";
                foreach (string stateName in new[] { h.from, h.to })
                    if (!string.IsNullOrEmpty(stateName) && definition.FindState(stateName) == null) return at + " names state '" + stateName + "', which is not a state of this definition";

                // What each kind may carry is fixed: a reason only ever rides on a rejection, a destination only on a
                // move. Free text an edited file could otherwise attach would be served by GetHistoryJson as if this
                // component had produced it.
                switch (h.kind)
                {
                    case "start":
                    case "reset":
                        if (string.IsNullOrEmpty(h.to)) return at + " (" + h.kind + ") has no 'to' state";
                        if (!string.IsNullOrEmpty(h.trigger) || !string.IsNullOrEmpty(h.from) || !string.IsNullOrEmpty(h.reason) || !string.IsNullOrEmpty(h.detail))
                            return at + " (" + h.kind + ") carries fields that only a transition or a rejection has";
                        if (!started) return at + " (" + h.kind + ") is in the history of a machine that is not started";
                        // Start and Reset always enter the definition's initial state.
                        if (!string.Equals(h.to, definition.Initial, StringComparison.OrdinalIgnoreCase))
                            return at + " (" + h.kind + ") enters '" + h.to + "', but " + h.kind + " always enters the initial state '" + definition.Initial + "'";
                        lastMoveTo = h.to;
                        break;
                    case "transition":
                        if (string.IsNullOrWhiteSpace(h.trigger) || string.IsNullOrEmpty(h.from) || string.IsNullOrEmpty(h.to)) return at + " (transition) is missing its trigger, 'from' or 'to'";
                        if (!string.IsNullOrEmpty(h.reason) || !string.IsNullOrEmpty(h.detail)) return at + " (transition) carries a rejection reason or detail";
                        if (!started) return at + " (transition) is in the history of a machine that is not started";
                        // The definition hash already matched, so a recorded move must be one the definition declares.
                        // A final state declines every trigger before any transition is matched, so a wildcard ('from *') can
                        // never have moved the machine out of one.
                        if (definition.FindState(h.from).Final) return at + " (transition) leaves final state '" + h.from + "', which accepts no triggers";
                        if (!definition.Transitions.Any(t => (t.From == MachineDefinition.Wildcard || string.Equals(t.From, h.from, StringComparison.OrdinalIgnoreCase))
                                                             && string.Equals(t.Trigger, h.trigger, StringComparison.OrdinalIgnoreCase)
                                                             && string.Equals(t.To, h.to, StringComparison.OrdinalIgnoreCase)))
                            return at + " (transition) records '" + h.from + "' -> '" + h.to + "' on '" + h.trigger + "', which the definition does not declare";
                        // Moves chain: each starts where the previous one ended (when the previous one is still retained).
                        if (lastMoveTo != null && !string.Equals(lastMoveTo, h.from, StringComparison.OrdinalIgnoreCase))
                            return at + " (transition) leaves '" + h.from + "' but the machine had just moved to '" + lastMoveTo + "'";
                        lastMoveTo = h.to;
                        break;
                    case "rejected":
                        if (string.IsNullOrWhiteSpace(h.trigger) || string.IsNullOrWhiteSpace(h.reason)) return at + " (rejected) is missing its trigger or reason";
                        if (!RejectionReasons.Contains(h.reason)) return at + " (rejected) has unknown reason '" + h.reason + "'";
                        if (!string.IsNullOrEmpty(h.to)) return at + " (rejected) names a destination state, but a rejection moves nothing";
                        if (h.reason == "NotStarted" && started) return at + " (rejected) has reason 'NotStarted' but the machine is started";
                        if (h.reason == "Finished" && !started) return at + " (rejected) has reason 'Finished' but the machine is not started";
                        // Fire records the state the machine was in, and which reason it gives follows from that state: a final
                        // state declines everything with 'Finished', a non-final one never does. Anything else is a record this
                        // component could not have written.
                        if (h.reason == "NotStarted")
                        {
                            if (!string.IsNullOrEmpty(h.from)) return at + " (rejected) has reason 'NotStarted' but names a current state";
                        }
                        else if (!string.IsNullOrEmpty(h.from))
                        {
                            if (lastMoveTo != null && !string.Equals(lastMoveTo, h.from, StringComparison.OrdinalIgnoreCase))
                                return at + " (rejected) was made in '" + h.from + "' but the machine had just moved to '" + lastMoveTo + "'";
                            bool wasFinal = definition.FindState(h.from).Final;
                            if (h.reason == "Finished" && !wasFinal) return at + " (rejected) has reason 'Finished' but '" + h.from + "' is not a final state";
                            if ((h.reason == "NoTransition" || h.reason == "GuardFailed") && wasFinal) return at + " (rejected) has reason '" + h.reason + "' but '" + h.from + "' is a final state, which declines everything with 'Finished'";
                        }
                        else if (h.reason != "ReentrancyLimit") return at + " (rejected) has reason '" + h.reason + "' but names no current state";
                        break;
                }
            }
            if (started && lastMoveTo != null && !string.Equals(lastMoveTo, savedCurrentState, StringComparison.OrdinalIgnoreCase))
                return "the last recorded move ends in '" + lastMoveTo + "' but the saved current state is '" + savedCurrentState + "'";
            if (history.Count > 0 && history[history.Count - 1].seq != sequence)
                return "its last entry is numbered " + history[history.Count - 1].seq + " but the saved sequence counter is " + sequence;
            return null;
        }

        /// <summary>Writes a snapshot durably. A no-op when persistence is off. The caller only commits the snapshot if this returns true.</summary>
        private bool PersistLocked(Snapshot snapshot, out string message)
        {
            message = null;
            if (persistStatePath == null) return true;
            try
            {
                var doc = new PersistedState
                {
                    schemaVersion = PersistedSchemaVersion,
                    machineName = persistMachineName,
                    definitionHash = persistDefinitionHash,
                    started = snapshot.Started,
                    currentState = snapshot.Current,
                    // A snapshot that was never started has no entry time; write a fixed value rather than converting
                    // DateTime.MinValue, whose UTC form depends on the machine's time zone.
                    enteredUtc = snapshot.Started
                        ? snapshot.EnteredUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                        : new DateTime(0, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
                    sequence = snapshot.Sequence,
                    context = new Dictionary<string, string>(snapshot.Context),
                    history = snapshot.History
                };
                StateMachineCore.WriteAtomic(persistStatePath, JsonSerializer.Serialize(doc, StateMachineCore.IndentedOptions));
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = "The change was not applied because the machine's state could not be saved: " + ex.Message;
                return false;
            }
        }

        private void ClearPersistenceFields(bool disposeLock)
        {
            FileStream held = persistLock;
            persistFolder = null;
            persistStatePath = null;
            persistMachineName = null;
            persistDefinitionHash = null;
            persistLock = null;
            if (disposeLock) held?.Dispose();
        }

        // ================================================================= helpers

        /// <summary>
        /// Test seam, invoked between a method's fast-path liveness check and its own critical section - the exact window in
        /// which another thread can dispose the component. Lets a test dispose there deterministically and prove that every
        /// public method re-checks under the lock. Null in production.
        /// </summary>
        internal Action AfterLivenessCheck;

        private bool RequireLive(out string message)
        {
            lock (syncRoot)
            {
                if (!RequireLiveLocked(out message)) return false;
            }
            AfterLivenessCheck?.Invoke();
            return true;
        }

        /// <summary>
        /// The liveness check for use INSIDE a critical section. A check made before taking the locks is only a fast
        /// path: a caller can pass it, wait for the dispatch lock while another thread disposes the component, and then
        /// mutate a disposed component and report success. Every critical section therefore re-checks under syncRoot,
        /// which Dispose also takes, so a call either finishes before disposal or observes it.
        /// </summary>
        private bool RequireLiveLocked(out string message)
        {
            message = null;
            if (!disposed) return true;
            message = "This StateMachineUtils component has been disposed.";
            return false;
        }

        /// <summary>
        /// Refuses a call made from inside one of this machine's own event handlers on the current thread.
        /// The locks are re-entrant, so without this check a handler could replace the definition while the batch
        /// of events describing the previous transition is still being delivered.
        /// </summary>
        private bool RequireNotInsideEvent(string operation, out string message)
        {
            message = null;
            if (fireDepth.Value <= 0) return true;
            message = operation + " cannot be called from inside an event handler: it replaces the definition and stops the machine while that handler's event batch is still being delivered. Call it from the automation's main flow instead.";
            return false;
        }

        private bool RequireDefinitionEditable(bool allowStoppingRunningMachine, out string message)
        {
            message = null;
            if (persistStatePath != null)
            {
                message = "Persistence is enabled; call DisablePersistence before changing the definition, because the saved state belongs to the current one.";
                return false;
            }
            if (state.Started && !allowStoppingRunningMachine)
            {
                message = "The machine is running; call ClearDefinition or LoadDefinitionJson (which stop it) before changing the definition.";
                return false;
            }
            return true;
        }

        /// <summary>After the definition is replaced: back to "not started" with no history, context kept. The sequence counter restarts with the history it numbered, so a stopped machine is indistinguishable from a fresh one and can still be persisted.</summary>
        private void StopMachineLocked()
        {
            Snapshot stopped = new Snapshot { Context = state.Context, Sequence = 0 };
            state = stopped;
        }

        private bool IsFinalLocked(Snapshot snapshot)
        {
            if (!snapshot.Started) return false;
            StateDef current = definition.FindState(snapshot.Current);
            return current != null && current.Final;
        }

        /// <summary>Appends a history entry to <paramref name="snapshot"/> (a fresh clone the caller owns), dropping the oldest beyond the cap.</summary>
        private void AddHistory(Snapshot snapshot, string kind, string trigger, string from, string to, string reason, string detail)
        {
            if (snapshot.Sequence >= MaxSequence)
                throw new InvalidOperationException("The history sequence counter is exhausted; call Reset, which clears the history and restarts the numbering.");
            snapshot.Sequence++;
            var list = new List<HistoryEntry>(snapshot.History.Count + 1);
            int keep = Math.Max(0, maximumHistoryEntries - 1);
            list.AddRange(snapshot.History.Skip(Math.Max(0, snapshot.History.Count - keep)));
            list.Add(new HistoryEntry
            {
                seq = snapshot.Sequence,
                utc = Clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                kind = kind,
                trigger = trigger,
                from = from,
                to = to,
                reason = reason,
                detail = detail
            });
            snapshot.History = list;
        }

        /// <summary>Undoes one re-entrancy increment.</summary>
        private void ReleaseDepth() => fireDepth.Value--;

        /// <summary>
        /// Drops history beyond the current limit from a snapshot that is about to be committed, so nothing this
        /// component persists or serves ever exceeds <see cref="MaximumHistoryEntries"/>. Appends already do this; the
        /// context changes and the first write after enabling persistence must too, or a lowered limit would leave
        /// the old, longer history sitting on disk until the next transition.
        /// </summary>
        private void EnforceHistoryLimit(Snapshot snapshot)
        {
            if (snapshot.History.Count > maximumHistoryEntries)
                snapshot.History = snapshot.History.Skip(snapshot.History.Count - maximumHistoryEntries).ToList();
        }

        private static string JoinProblems(List<string> problems) =>
            string.Join(" ", problems.Take(10)) + (problems.Count > 10 ? " (and " + (problems.Count - 10) + " more)" : string.Empty);

        /// <summary>Releases the persistence ownership lock and the per-thread re-entrancy counter.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Deliberately NOT under dispatchLock. Waiting there would make Dispose block for as long as any handler
                // runs, and a handler that marshals to the thread calling Dispose (Robot Studio tearing down on its UI
                // thread) would then deadlock. syncRoot is enough: it is held by every mutation and every critical
                // section re-checks `disposed` under it (RequireLiveLocked), so an operation that already committed
                // finishes first and one still waiting for its turn observes disposal and refuses.
                lock (syncRoot)
                {
                    disposed = true;
                    ClearPersistenceFields(true);
                }
                // fireDepth is intentionally not disposed: it holds no unmanaged resources (it does not track its
                // values), and disposing it would make a concurrent Fire's counter access throw mid-call.
            }
            base.Dispose(disposing);
        }
    }
}
