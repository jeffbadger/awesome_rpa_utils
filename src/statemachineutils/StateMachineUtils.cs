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
        [Description("Maximum number of history entries kept (transitions, rejections, starts). Valid range: 1 through 10,000. Default: 100.")]
        [DefaultValue(DefaultMaximumHistoryEntries)]
        public int MaximumHistoryEntries
        {
            get { lock (syncRoot) return maximumHistoryEntries; }
            set
            {
                if (value < 1 || value > AbsoluteMaximumHistoryEntries)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaximumHistoryEntries must be between 1 and 10,000.");
                lock (syncRoot)
                {
                    maximumHistoryEntries = value;
                    if (state.History.Count > value)
                    {
                        Snapshot trimmed = state.Clone();
                        trimmed.History = state.History.Skip(state.History.Count - value).ToList();
                        state = trimmed;
                    }
                }
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
                MaximumHistoryEntries = maximumEntries;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetMaximumHistoryEntries), ex); return false; }
        }

        // ================================================================= events

        /// <summary>Raised when the machine leaves a state.</summary>
        [Category("StateMachine - Events")]
        [Description("Raised when the machine leaves a state, before TransitionFired and StateEntered. Fires synchronously on the thread that called Fire.")]
        public event EventHandler<StateMachineTransitionEventArgs> StateExited;

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
        [Description("Parses and validates a JSON definition, then replaces the current one and stops the machine (context is kept). An invalid definition is rejected whole and the previous definition stays in force. Never throws.")]
        public bool LoadDefinitionJson(string definitionJson, out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
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
                lock (syncRoot) definitionJson = definition.ToJson(true);
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
        [Description("Adds a transition from a state (or '*' for any non-final state) on a trigger to another state, optionally with one guard (guardKey + guardOp [+ guardValue]). Transitions for the same state and trigger are tried in the order added; the first whose guard passes wins. Use LoadDefinitionJson for several guards on one transition. Never throws.")]
        public bool AddTransition(string fromState, string trigger, string toState, out string message, string guardKey = null, string guardOp = null, string guardValue = null)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
                    if (!RequireDefinitionEditable(false, out message)) return false;
                    if (definition.Transitions.Count >= MachineDefinition.MaxTransitions) { message = "A machine may have at most " + MachineDefinition.MaxTransitions + " transitions."; return false; }

                    var transition = new TransitionDef { From = fromState?.Trim(), Trigger = trigger?.Trim(), To = toState?.Trim() };
                    StateDef from = transition.From == MachineDefinition.Wildcard ? null : definition.FindState(transition.From);
                    if (from != null) transition.From = from.Name;
                    StateDef to = definition.FindState(transition.To);
                    if (to != null) transition.To = to.Name;

                    bool anyGuardArg = !string.IsNullOrWhiteSpace(guardKey) || !string.IsNullOrWhiteSpace(guardOp) || guardValue != null;
                    if (anyGuardArg)
                    {
                        if (string.IsNullOrWhiteSpace(guardKey) || string.IsNullOrWhiteSpace(guardOp)) { message = "A guard needs both guardKey and guardOp (and guardValue for every operator except exists/notExists)."; return false; }
                        transition.Guards.Add(new GuardDef { Key = guardKey.Trim(), Op = MachineDefinition.NormalizeOp(guardOp.Trim()), Value = guardValue });
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

        /// <summary>Removes every state and transition and stops the machine.</summary>
        [Category("StateMachine - Definition")]
        [Description("Removes the whole definition and stops the machine (context is kept). Not allowed while persistence is enabled. Never throws.")]
        public bool ClearDefinition(out string message)
        {
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                lock (syncRoot)
                {
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
                lock (syncRoot)
                {
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
                    if (clearContext) next.Context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    AddHistory(next, isReset ? "reset" : "start", null, null, initial, null, null);
                    if (!PersistLocked(next, out message)) return false;

                    state = next;
                    currentState = initial;
                    pending.Add(() => RaiseSafely(StateEntered, new StateMachineTransitionEventArgs(string.Empty, initial, string.Empty)));
                }
                foreach (Action raise in pending) raise();
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { currentState = null; message = NeverThrowsGuard.Failure(operation, ex); return false; }
            finally { if (depthTaken) ReleaseDepth(); }
        }

        /// <summary>Fires a trigger.</summary>
        [Category("StateMachine - Run")]
        [Description("Fires a trigger. A declined trigger (no transition, guard failed, machine finished or not started) is a normal outcome: the call returns True with fired False and a rejectionReason (NoTransition, GuardFailed, Finished, NotStarted, ReentrancyLimit). False plus a message means bad input or a persistence failure, and the machine is unchanged. Events are raised synchronously on this thread after the change is committed. Never throws.")]
        public bool Fire(string trigger, out bool fired, out string newState, out string rejectionReason, out string message)
        {
            fired = false;
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
                        newState = state.Current;
                        Snapshot next = state.Clone();
                        AddHistory(next, "rejected", trimmedTrigger, state.Current, null, "ReentrancyLimit", "Trigger '" + trimmedTrigger + "' was declined: the re-entrancy limit (" + MaxReentrancyDepth + ") was reached.");
                        state = next;
                    }
                    rejectionReason = "ReentrancyLimit";
                    return true;
                }
                fireDepth.Value++;
                depthTaken = true;

                var pending = new List<Action>();
                lock (syncRoot)
                {
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
                        AddHistory(next, "transition", firedTrigger, from, to, null, null);
                        if (!PersistLocked(next, out message)) return false;

                        state = next;
                        fired = true;
                        newState = to;
                        pending.Add(() => RaiseSafely(StateExited, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                        pending.Add(() => RaiseSafely(TransitionFired, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                        pending.Add(() => RaiseSafely(StateEntered, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                        if (target.Final)
                            pending.Add(() => RaiseSafely(MachineFinished, new StateMachineTransitionEventArgs(from, to, firedTrigger)));
                    }
                }
                foreach (Action raise in pending) raise();
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                fired = false;
                newState = null;
                rejectionReason = null;
                message = NeverThrowsGuard.Failure(nameof(Fire), ex);
                return false;
            }
            finally { if (depthTaken) ReleaseDepth(); }
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
                lock (syncRoot) currentState = state.Current;
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
                lock (syncRoot) isStarted = state.Started;
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
                lock (syncRoot) isFinal = IsFinalLocked(state);
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
                    IEnumerable<HistoryEntry> slice = state.History.Skip(Math.Max(0, state.History.Count - maxEntries));
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
                    string trimmedKey = key.Trim();
                    if (!state.Context.ContainsKey(trimmedKey) && state.Context.Count >= StateMachineCore.MaxContextEntries) { message = "The context is full (" + StateMachineCore.MaxContextEntries + " keys)."; return false; }
                    Snapshot next = state.Clone();
                    next.Context = new Dictionary<string, string>(state.Context, StringComparer.OrdinalIgnoreCase) { [trimmedKey] = value };
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
                lock (syncRoot) exists = state.Context.TryGetValue(key.Trim(), out value);
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
                    string trimmedKey = key.Trim();
                    if (!state.Context.ContainsKey(trimmedKey)) return true;
                    Snapshot next = state.Clone();
                    var copy = new Dictionary<string, string>(state.Context, StringComparer.OrdinalIgnoreCase);
                    copy.Remove(trimmedKey);
                    next.Context = copy;
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
                    if (state.Context.Count == 0) return true;
                    Snapshot next = state.Clone();
                    next.Context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        [Description("Persists the machine's state, context and history under LocalApplicationData so a flow can resume after a crash or restart. Call after the definition is loaded and before Start. If saved state for this machineName exists and matches the definition it is restored (restored True) without raising events; a saved state from a different definition is refused. From then on every change is written first and only committed if the write succeeds. Never throws.")]
        public bool EnablePersistence(string machineName, out string statePath, out bool restored, out string message)
        {
            statePath = null;
            restored = false;
            message = null;
            try
            {
                if (!RequireLive(out message)) return false;
                if (!StateMachineCore.TryResolveMachineFolder(machineName, out string folder, out message)) return false;

                lock (syncRoot)
                {
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

                        if (File.Exists(path))
                        {
                            if (!TryLoadSaved(path, hash, out next, out message)) return false;
                            didRestore = true;
                        }
                        else next = state.Clone();

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
                lock (syncRoot) ClearPersistenceFields(true);
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
                    if (persistFolder != null && string.Equals(persistFolder, folder, StringComparison.OrdinalIgnoreCase)) { message = "Persistence is enabled for '" + machineName.Trim() + "' on this component; call DisablePersistence first."; return false; }
                    if (!Directory.Exists(folder)) return true;

                    FileStream lockStream;
                    try { lockStream = new FileStream(Path.Combine(folder, ".machine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException) { message = "The saved state for '" + machineName.Trim() + "' is in use by another StateMachineUtils component or process."; return false; }

                    using (lockStream)
                    {
                        string path = Path.Combine(folder, "state.json");
                        if (File.Exists(path)) { File.Delete(path); discarded = true; }
                    }
                    try { File.Delete(Path.Combine(folder, ".machine.lock")); Directory.Delete(folder, false); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* leftover empty folder is harmless; the state itself is gone */ }
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { discarded = false; message = NeverThrowsGuard.Failure(nameof(DiscardPersistedState), ex); return false; }
        }

        private bool TryLoadSaved(string path, string definitionHash, out Snapshot restored, out string message)
        {
            restored = null;
            message = null;
            PersistedState saved;
            try { saved = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException ex) { message = "The saved state is corrupt (" + ex.Message + "); call DiscardPersistedState to start fresh."; return false; }
            if (saved == null) { message = "The saved state is empty; call DiscardPersistedState to start fresh."; return false; }
            if (saved.schemaVersion != PersistedSchemaVersion) { message = "The saved state uses schema version " + saved.schemaVersion + ", which this component does not understand; call DiscardPersistedState to start fresh."; return false; }
            if (!string.Equals(saved.definitionHash, definitionHash, StringComparison.Ordinal)) { message = "The saved state was made with a different definition; call DiscardPersistedState to abandon it, or load the original definition."; return false; }

            var snapshot = new Snapshot { Started = saved.started, Sequence = saved.sequence };
            if (saved.started)
            {
                StateDef current = definition.FindState(saved.currentState);
                if (current == null) { message = "The saved state names '" + saved.currentState + "', which is not a state of this definition; call DiscardPersistedState to start fresh."; return false; }
                snapshot.Current = current.Name;
                if (!DateTime.TryParse(saved.enteredUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime entered)) entered = Clock();
                snapshot.EnteredUtc = entered.ToUniversalTime();
            }
            if (saved.context != null)
                snapshot.Context = new Dictionary<string, string>(saved.context.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.OrdinalIgnoreCase);
            if (saved.history != null)
                snapshot.History = saved.history.Skip(Math.Max(0, saved.history.Count - maximumHistoryEntries)).ToList();
            restored = snapshot;
            return true;
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
                    enteredUtc = snapshot.EnteredUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
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

        private bool RequireLive(out string message)
        {
            message = null;
            lock (syncRoot)
            {
                if (!disposed) return true;
            }
            message = "This StateMachineUtils component has been disposed.";
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

        /// <summary>After the definition is replaced: back to "not started" with no history, context kept.</summary>
        private void StopMachineLocked()
        {
            Snapshot stopped = new Snapshot { Context = state.Context, Sequence = state.Sequence };
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

        /// <summary>Undoes one re-entrancy increment. A handler may dispose the component mid-call, which disposes the counter, so this must not throw.</summary>
        private void ReleaseDepth()
        {
            try { fireDepth.Value--; }
            catch (ObjectDisposedException) { /* the component was disposed by a handler; nothing left to count */ }
        }

        private static string JoinProblems(List<string> problems) =>
            string.Join(" ", problems.Take(10)) + (problems.Count > 10 ? " (and " + (problems.Count - 10) + " more)" : string.Empty);

        /// <summary>Releases the persistence ownership lock and the per-thread re-entrancy counter.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot)
                {
                    disposed = true;
                    ClearPersistenceFields(true);
                }
                fireDepth.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
