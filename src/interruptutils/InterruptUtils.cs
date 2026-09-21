using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace InterruptAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that watches for known popups on its own
    /// background threads and dismisses them while the automation is busy - the one way to
    /// handle a popup during a long wait, because an automation's steps run one at a time
    /// on a single thread and cannot be interrupted.
    /// <para>
    /// Describe each popup once with an <c>Add...Rule</c> method (by title, message text and/or
    /// owning process, plus what to click), call <see cref="Start"/>, and carry on. It notices
    /// new windows through window events (and a periodic scan as a safety net), clicks the
    /// named button on a worker thread, checks the popup went away, and records the outcome
    /// in a log you can query and in events.
    /// </para>
    /// </summary>
    [Description("Watches for known popups in the background and dismisses them while the automation is busy " +
                 "(for example during a long wait). Define rules by title, message and process, then Start. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class InterruptUtils : Component
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly PopupEngine _engine;
        private readonly IPopupHookSource _hook;
        private readonly IInstanceGuard _guard;
        private readonly object _lifeLock = new object();
        private Thread _worker;
        private CancellationTokenSource _cts;
        private Thread _lingeringWorker;
        private CancellationTokenSource _lingeringCts;
        private bool _disposed;

        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public InterruptUtils() : this(new Win32PopupProbe(), new PopupHookThread(), new NamedInstanceGuard())
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public InterruptUtils(IContainer container) : this()
        {
            container?.Add(this);
        }

        internal InterruptUtils(IPopupProbe probe, IPopupHookSource hook, IInstanceGuard guard = null)
        {
            _hook = hook;
            _guard = guard ?? new NoInstanceGuard();
            _engine = new PopupEngine(probe, Thread.Sleep, OnRecord);
        }

        #region Events

        /// <summary>Raised on a worker thread when a popup that matches a watch-only rule appears.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised when a popup matching a watch-only rule appears (it is not touched). Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<InterruptPopupEventArgs> PopupDetected;

        /// <summary>Raised on a worker thread after a popup was dismissed.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised after a popup was dismissed by a rule. Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<InterruptPopupEventArgs> PopupDismissed;

        /// <summary>Raised on a worker thread when a popup matched a rule but could not be dismissed.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised when a popup matched a rule but could not be dismissed; Detail says why. Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<InterruptPopupEventArgs> PopupDismissFailed;

        /// <summary>Raised on a worker thread when the handler itself has a problem, such as a rule that stopped for dismissing too many popups.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised when the handler has a problem, such as a rule that stopped for dismissing too many popups. Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<InterruptErrorEventArgs> InterruptError;

        #endregion

        #region Rules

        /// <summary>
        /// Adds a rule that dismisses a matching popup by clicking the button with the given text.
        /// </summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case), used in events, the log and the other rule methods.</param>
        /// <param name="titleContains">Text the popup's title must contain (ignoring case); empty to not check the title.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check the message.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="buttonText">The button to click, matched ignoring case and ignoring the <c>&amp;</c> access-key marker (so "Yes" finds "&amp;Yes").</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="exactButtonText"><c>true</c> to need the button's whole text; <c>false</c> to accept the first button that contains <paramref name="buttonText"/>.</param>
        /// <param name="className">The popup's window class; empty for the standard dialog class (<c>#32770</c>), or <c>*</c> for any class.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. At least one of <paramref name="titleContains"/>, <paramref name="messageContains"/> and <paramref name="processName"/> is required, so a rule can never match every dialog. Never throws.</returns>
        /// <remarks>
        /// Rules are tried in the order they were added and the first one that matches is used.
        /// Clicking a button such as "Yes" or "No" on a "Save changes?" popup is a decision about
        /// your data: write the rule for the popup you have decided how to answer.
        /// </remarks>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching popup by clicking the button with the given text. Needs at least one of title, message or process. Returns True if added; never throws.")]
        public bool AddDismissRuleByText(string ruleName, string titleContains, string messageContains, string processName,
            string buttonText, out string message, bool exactButtonText = true, string className = null)
        {
            message = default;
            try
            {
                // Strip the access-key marker before deciding whether anything is left: "&" alone
                // would otherwise pass as text and then match no button.
                string label = PopupRule.StripMnemonic((buttonText ?? string.Empty).Trim()).Trim();
                if (label.Length == 0)
                {
                    message = "buttonText may not be empty (an access-key marker on its own does not count).";
                    return false;
                }
                return AddRule(new PopupRule
                {
                    Action = PopupAction.ClickButtonText,
                    ButtonText = label,
                    ExactButtonText = exactButtonText
                }, ruleName, titleContains, messageContains, processName, className, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddDismissRuleByText", ex);
                return false;
            }
        }

        /// <summary>
        /// Adds a rule that dismisses a matching popup by clicking a standard button (OK, Cancel, Yes, ...) by its control ID.
        /// </summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="titleContains">Text the popup's title must contain (ignoring case); empty to not check the title.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check the message.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="button">The standard button to click. Works on a classic message box, whose buttons have these IDs; button text is language-independent this way.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="className">The popup's window class; empty for the standard dialog class (<c>#32770</c>), or <c>*</c> for any class.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching popup by clicking a standard button (OK, Cancel, Yes...) by control ID. Needs at least one of title, message or process. Returns True if added; never throws.")]
        public bool AddDismissRuleById(string ruleName, string titleContains, string messageContains, string processName,
            InterruptButton button, out string message, string className = null)
        {
            message = default;
            try
            {
                if (!Enum.IsDefined(typeof(InterruptButton), button))
                {
                    message = "button is not a defined InterruptButton value.";
                    return false;
                }
                return AddRule(new PopupRule
                {
                    Action = PopupAction.ClickButtonId,
                    ButtonId = (int)button
                }, ruleName, titleContains, messageContains, processName, className, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddDismissRuleById", ex);
                return false;
            }
        }

        /// <summary>
        /// Adds a rule that dismisses a matching popup by closing its window, for a popup with no button to click.
        /// </summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="titleContains">Text the popup's title must contain (ignoring case); empty to not check the title.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check the message.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="className">The popup's window class; empty for the standard dialog class (<c>#32770</c>), or <c>*</c> for any class.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        /// <remarks>Closing is what the window's close box does. For a message box with a Cancel button that means Cancel; for some windows it means "discard".</remarks>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching popup by closing its window (for a popup with no button to click). Needs at least one of title, message or process. Returns True if added; never throws.")]
        public bool AddCloseWindowRule(string ruleName, string titleContains, string messageContains, string processName,
            out string message, string className = null)
        {
            message = default;
            try
            {
                return AddRule(new PopupRule { Action = PopupAction.CloseWindow },
                    ruleName, titleContains, messageContains, processName, className, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddCloseWindowRule", ex);
                return false;
            }
        }

        /// <summary>
        /// Adds a rule that only reports a matching popup (<see cref="PopupDetected"/> and the log); the popup is never touched.
        /// </summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="titleContains">Text the popup's title must contain (ignoring case); empty to not check the title.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check the message.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="className">The popup's window class; empty for the standard dialog class (<c>#32770</c>), or <c>*</c> for any class.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        /// <remarks>Because the first matching rule wins, a watch-only rule placed before a dismiss rule for the same popup stops the dismiss rule from ever running.</remarks>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that only reports a matching popup and never touches it. Needs at least one of title, message or process. Returns True if added; never throws.")]
        public bool AddWatchOnlyRule(string ruleName, string titleContains, string messageContains, string processName,
            out string message, string className = null)
        {
            message = default;
            try
            {
                return AddRule(new PopupRule { Action = PopupAction.WatchOnly },
                    ruleName, titleContains, messageContains, processName, className, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddWatchOnlyRule", ex);
                return false;
            }
        }

        /// <summary>Removes a rule, and its dismissal count.</summary>
        /// <param name="ruleName">The rule to remove.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the rule existed and was removed; <c>false</c> if there is no such rule or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Removes a rule. Returns True if it existed; never throws.")]
        public bool RemoveRule(string ruleName, out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!_engine.RemoveRule(ruleName ?? string.Empty))
                {
                    message = "There is no rule named '" + ruleName + "'.";
                    return false;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("RemoveRule", ex);
                return false;
            }
        }

        /// <summary>Removes every rule and dismissal count. The log is kept.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Removes every rule. Returns True on success; never throws.")]
        public bool ClearRules(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                _engine.ClearRules();
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClearRules", ex);
                return false;
            }
        }

        /// <summary>
        /// Turns a rule off or on without removing it, so a step that drives a dialog itself is not
        /// interrupted. Turning a rule on also clears a stop caused by dismissing too many popups, and
        /// makes it look again at popups that are already open, including ones it had given up on. Turning a rule off returns once any
        /// dismissal already under way has finished (a few seconds at most), so the rule cannot act
        /// after this returns.
        /// </summary>
        /// <param name="ruleName">The rule to change.</param>
        /// <param name="enabled"><c>true</c> to turn the rule on; <c>false</c> to turn it off.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> if the rule exists and was changed; <c>false</c> if there is no such rule or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Turns a rule off or on. Turning it on also clears a runaway stop. Returns True on success; never throws.")]
        public bool SetRuleEnabled(string ruleName, bool enabled, out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!_engine.SetRuleEnabled(ruleName ?? string.Empty, enabled))
                {
                    message = "There is no rule named '" + ruleName + "'.";
                    return false;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("SetRuleEnabled", ex);
                return false;
            }
        }

        /// <summary>Lists the rules, with whether each is on, has stopped itself, and how many popups it has dismissed, as JSON.</summary>
        /// <param name="rulesJson">A JSON array (<c>[]</c> if there are no rules); empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Lists the rules with their state and dismissal counts as JSON. Returns True on success; never throws.")]
        public bool ListRulesJson(out string rulesJson, out string message)
        {
            rulesJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                var items = new List<object>();
                foreach (var rule in _engine.SnapshotRules())
                {
                    _engine.TryGetCount(rule.Name, out int count);
                    items.Add(new
                    {
                        name = rule.Name,
                        action = rule.Action.ToString(),
                        titleContains = rule.TitleContains ?? string.Empty,
                        messageContains = rule.MessageContains ?? string.Empty,
                        processName = rule.ProcessName ?? string.Empty,
                        className = rule.ClassName,
                        button = rule.Action == PopupAction.ClickButtonText ? rule.ButtonText
                            : rule.Action == PopupAction.ClickButtonId ? ((InterruptButton)rule.ButtonId).ToString() : string.Empty,
                        enabled = rule.Enabled,
                        stopped = rule.Tripped,
                        dismissals = count
                    });
                }
                rulesJson = JsonSerializer.Serialize(items, JsonOptions);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                rulesJson = string.Empty;
                message = NeverThrowsGuard.Failure("ListRulesJson", ex);
                return false;
            }
        }

        private bool AddRule(PopupRule rule, string ruleName, string titleContains, string messageContains,
            string processName, string className, out string message)
        {
            if (IsDisposed(out message))
                return false;

            // Normalize first, then validate what will actually be stored: a process name of
            // ".exe" trims to nothing, and a rule left with no criterion would match every dialog.
            string name = (ruleName ?? string.Empty).Trim();
            string title = (titleContains ?? string.Empty).Trim();
            string messageCriterion = (messageContains ?? string.Empty).Trim();
            string process = PopupRule.TrimExe(processName);
            message = PopupRule.ValidateCommon(name, title, messageCriterion, process);
            if (message != null)
                return false;

            rule.Name = name;
            rule.TitleContains = title;
            rule.MessageContains = messageCriterion;
            rule.ProcessName = process;
            rule.ClassName = string.IsNullOrWhiteSpace(className) ? PopupRule.DialogClass : className.Trim();
            return _engine.AddRule(rule, out message);
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts watching for popups on background threads and returns as soon as the window-event
        /// hooks are installed (normally a few milliseconds; it waits at most 5 seconds for the
        /// system to install them, after first waiting up to about 5 seconds for another running
        /// instance to stop, if there is one), then the automation carries on while popups matching a
        /// rule are dismissed.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it did not start.</param>
        /// <param name="sweepIntervalMs">How often, in milliseconds, to scan every window as a safety net for popups the window events missed (and ones already open now); 0 turns the scan off. 0 to 60000.</param>
        /// <param name="maxAttempts">How many times to try to dismiss one popup before giving up on it. 1 to 10.</param>
        /// <param name="maxDismissalsPerMinute">How many popups one rule may dismiss in a minute before it stops itself, so a popup that keeps coming back cannot loop forever. 1 to 1000.</param>
        /// <returns><c>true</c> if watching started; <c>false</c> if it is already running, a value is out of range, window events are unavailable in this session, a previous run is still shutting down, another instance would not stop, or the component is disposed. Never throws.</returns>
        /// <remarks>
        /// Popups owned by the automation's own process are never touched. Events are not delivered
        /// while the screen is locked or on a secure desktop. Stop it with <see cref="Stop"/>;
        /// disposing the component stops it too. Only one instance watches at a time, in this process
        /// or any other in the user's session: starting one asks a running one to stop first (it keeps
        /// its rules, counts and log), and this method fails if that instance has not stopped within a
        /// few seconds.
        /// </remarks>
        [Category("Interrupt - Lifecycle")]
        [Description("Starts watching for popups in the background and dismissing those that match a rule. Stops any other instance still watching (one that has this guard), in this process or another, and waits a few seconds for it. Returns once the hooks are installed (normally milliseconds). Returns True on success; never throws.")]
        public bool Start(out string message, int sweepIntervalMs = 1000, int maxAttempts = 3, int maxDismissalsPerMinute = 20)
        {
            message = default;
            try
            {
                if (sweepIntervalMs < 0 || sweepIntervalMs > 60000)
                {
                    message = "sweepIntervalMs must be between 0 and 60000.";
                    return false;
                }
                if (maxAttempts < 1 || maxAttempts > 10)
                {
                    message = "maxAttempts must be between 1 and 10.";
                    return false;
                }
                if (maxDismissalsPerMinute < 1 || maxDismissalsPerMinute > 1000)
                {
                    message = "maxDismissalsPerMinute must be between 1 and 1000.";
                    return false;
                }

                lock (_lifeLock)
                {
                    if (_disposed)
                    {
                        message = "The component has been disposed.";
                        return false;
                    }
                    if (_worker != null)
                    {
                        message = "Already running; call Stop first.";
                        return false;
                    }
                    if (_lingeringWorker != null)
                    {
                        if (_lingeringWorker.IsAlive)
                        {
                            message = "The previous run is still shutting down; try again shortly.";
                            return false;
                        }
                        // It has ended but the thread that stopped it has not yet cleaned up: do that here,
                        // so this Start cannot overlap the old run's guard being given back.
                        FinishLingeringRunLocked();
                    }

                    // Only one instance watches at a time, in this process or any other in the session:
                    // this asks a running one to stop and waits (a few seconds at most) for it to.
                    if (!_guard.TryAcquire(() => Stop(out _), out string guardMessage))
                    {
                        message = guardMessage;
                        return false;
                    }

                    // The guard is held from here: every way out that is not a running watch gives it back.
                    bool started = false;
                    try
                    {
                        _engine.SweepIntervalMs = sweepIntervalMs;
                        _engine.MaxAttempts = maxAttempts;
                        _engine.MaxDismissalsPerMinute = maxDismissalsPerMinute;
                        _engine.ResetRuntime();

                        // A hook failure is only queued here (it arrives on the hook thread); the worker
                        // records it, so InterruptError is raised on the worker thread like every event.
                        if (!_hook.Start(_engine.Enqueue, _engine.EnqueueDestroyed, _engine.EnqueueFault, out string hookMessage))
                        {
                            message = hookMessage ?? "Window events could not be started.";
                            return false;
                        }

                        // From here the hook is running, so a failure to get the worker going must undo it:
                        // Start either succeeds completely or leaves nothing behind.
                        var cts = new CancellationTokenSource();
                        try
                        {
                            var worker = new Thread(() => WorkerLoop(cts))
                            {
                                IsBackground = true,
                                Name = "InterruptUtils.Worker"
                            };
                            worker.Start();
                            _cts = cts;
                            _worker = worker;
                        }
                        catch
                        {
                            _cts = null;
                            _worker = null;
                            _hook.Stop();
                            cts.Dispose();
                            throw; // reported by the outer handler as the reason Start failed
                        }
                        started = true;
                    }
                    finally
                    {
                        if (!started)
                            _guard.Release();
                    }
                }

                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Start", ex);
                return false;
            }
        }

        /// <summary>Stops watching. Rules, counts and the log are kept, so <see cref="Start"/> can resume.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success, including when it was not running; <c>false</c> only if something unexpected failed. Never throws.</returns>
        [Category("Interrupt - Lifecycle")]
        [Description("Stops watching for popups. Rules and the log are kept. Returns True on success, including when not running; never throws.")]
        public bool Stop(out string message)
        {
            message = default;
            try
            {
                StopCore();
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Stop", ex);
                return false;
            }
        }

        /// <summary>Whether watching is currently running.</summary>
        /// <returns><c>true</c> while started and not stopped or disposed. Never throws.</returns>
        [Category("Interrupt - Lifecycle")]
        [Description("Returns True while watching for popups is running. Never throws.")]
        public bool IsRunning()
        {
            lock (_lifeLock)
                return _worker != null && !_disposed;
        }

        /// <summary>
        /// Stops the handler from touching popups until <see cref="Resume"/>, without stopping the
        /// watch. Use it around a step that drives a dialog itself. Popups that appear meanwhile and
        /// are still open when it resumes are then dealt with. Returns once any dismissal already under
        /// way has finished (a few seconds at most, against a slow application), so nothing the handler
        /// does can land after this returns.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Lifecycle")]
        [Description("Stops the handler touching popups until Resume, without stopping the watch. Returns True on success; never throws.")]
        public bool Pause(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                _engine.Paused = true;
                _engine.WaitForIdle(); // a dismissal already under way finishes before this returns
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Pause", ex);
                return false;
            }
        }

        /// <summary>Lets the handler touch popups again after <see cref="Pause"/>.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Lifecycle")]
        [Description("Lets the handler dismiss popups again after Pause. Returns True on success; never throws.")]
        public bool Resume(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                _engine.Paused = false;
                _engine.Wake();
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("Resume", ex);
                return false;
            }
        }

        #endregion

        #region Results

        /// <summary>How many popups a rule has dismissed.</summary>
        /// <param name="ruleName">The rule to ask about.</param>
        /// <param name="count">The number of popups the rule has dismissed; 0 if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if there is no such rule or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Results")]
        [Description("How many popups a rule has dismissed. Returns True on success; never throws.")]
        public bool GetDismissalCount(string ruleName, out int count, out string message)
        {
            count = 0;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (!_engine.TryGetCount(ruleName ?? string.Empty, out count))
                {
                    count = 0;
                    message = "There is no rule named '" + ruleName + "'.";
                    return false;
                }
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                count = 0;
                message = NeverThrowsGuard.Failure("GetDismissalCount", ex);
                return false;
            }
        }

        /// <summary>How many popups have been dismissed by all rules together, since the component was created.</summary>
        /// <param name="total">The number of popups dismissed; 0 if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Results")]
        [Description("How many popups all rules have dismissed together. Returns True on success; never throws.")]
        public bool GetTotalDismissals(out int total, out string message)
        {
            total = 0;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                total = _engine.TotalDismissals;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                total = 0;
                message = NeverThrowsGuard.Failure("GetTotalDismissals", ex);
                return false;
            }
        }

        /// <summary>Whether a popup that a dismiss rule matched is still open (it is being retried, or the handler gave up on it).</summary>
        /// <param name="hasUnresolvedPopup"><c>true</c> if such a popup is open; <c>false</c> if none is, or if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        /// <remarks>Reflects the handler's most recent pass, so it can lag a popup's arrival by a moment.</remarks>
        [Category("Interrupt - Results")]
        [Description("Whether a popup matched by a dismiss rule is still open (being retried, or given up on). Returns True on success; never throws.")]
        public bool HasUnresolvedPopup(out bool hasUnresolvedPopup, out string message)
        {
            hasUnresolvedPopup = false;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                hasUnresolvedPopup = _engine.UnresolvedCount > 0;
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                hasUnresolvedPopup = false;
                message = NeverThrowsGuard.Failure("HasUnresolvedPopup", ex);
                return false;
            }
        }

        /// <summary>The most recent log entry (a popup detected, dismissed or not dismissed, or a problem) as JSON.</summary>
        /// <param name="eventJson">A JSON object; <c>{}</c> if nothing has been logged; empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Results")]
        [Description("The most recent log entry as JSON ({} if none). Returns True on success; never throws.")]
        public bool GetLastEventJson(out string eventJson, out string message)
        {
            eventJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                PopupRecord last = _engine.LastRecord();
                eventJson = last == null ? "{}" : JsonSerializer.Serialize(ToJsonObject(last), JsonOptions);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                eventJson = string.Empty;
                message = NeverThrowsGuard.Failure("GetLastEventJson", ex);
                return false;
            }
        }

        /// <summary>The most recent log entries, oldest first, as a JSON array. The log keeps the last 500.</summary>
        /// <param name="maxEntries">How many of the most recent entries to return; 1 to 500.</param>
        /// <param name="logJson">A JSON array (<c>[]</c> if nothing has been logged); empty if this method returns <c>false</c>.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if <paramref name="maxEntries"/> is out of range or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Results")]
        [Description("The most recent log entries as a JSON array, oldest first (the log keeps the last 500). Returns True on success; never throws.")]
        public bool GetLogJson(int maxEntries, out string logJson, out string message)
        {
            logJson = string.Empty;
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                if (maxEntries < 1 || maxEntries > PopupEngine.LogCapacity)
                {
                    message = "maxEntries must be between 1 and " + PopupEngine.LogCapacity + ".";
                    return false;
                }
                var items = new List<object>();
                foreach (var record in _engine.GetLog(maxEntries))
                    items.Add(ToJsonObject(record));
                logJson = JsonSerializer.Serialize(items, JsonOptions);
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                logJson = string.Empty;
                message = NeverThrowsGuard.Failure("GetLogJson", ex);
                return false;
            }
        }

        /// <summary>Empties the log. Dismissal counts are kept.</summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        [Category("Interrupt - Results")]
        [Description("Empties the log. Counts are kept. Returns True on success; never throws.")]
        public bool ClearLog(out string message)
        {
            message = default;
            try
            {
                if (IsDisposed(out message))
                    return false;
                _engine.ClearLog();
                message = null;
                return true;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("ClearLog", ex);
                return false;
            }
        }

        private static object ToJsonObject(PopupRecord record) => new
        {
            kind = record.Kind.ToString(),
            rule = record.RuleName,
            title = record.Title,
            message = record.MessageText,
            process = record.ProcessName,
            processId = record.ProcessId,
            button = record.ButtonClicked,
            attempts = record.Attempts,
            timestampUtc = record.TimestampUtc.ToString("o"),
            detail = record.Detail
        };

        #endregion

        #region Worker

        private void WorkerLoop(CancellationTokenSource cts)
        {
            try
            {
                CancellationToken token = cts.Token;
                while (!token.IsCancellationRequested)
                {
                    long next;
                    try
                    {
                        next = _engine.Pump(Environment.TickCount64);
                    }
                    catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                    {
                        _engine.RecordError(string.Empty, NeverThrowsGuard.Failure("Popup handling", ex));
                        next = Environment.TickCount64 + 500; // do not spin on a repeating failure
                    }

                    long wait = next == long.MaxValue ? 500 : next - Environment.TickCount64;
                    _engine.WaitForWork((int)Math.Min(Math.Max(wait, 0), 500));
                }
            }
            finally
            {
                // If Stop gave up waiting for this worker (it was in the middle of a click on a
                // slow application), the run's resources are released here, once it has finished.
                ReleaseRun(cts, workerHasEnded: true);
            }
        }

        private void OnRecord(PopupRecord record)
        {
            switch (record.Kind)
            {
                case PopupRecordKind.Detected:
                    RaiseSafely(PopupDetected, ToPopupArgs(record));
                    break;
                case PopupRecordKind.Dismissed:
                    RaiseSafely(PopupDismissed, ToPopupArgs(record));
                    break;
                case PopupRecordKind.DismissFailed:
                    RaiseSafely(PopupDismissFailed, ToPopupArgs(record));
                    break;
                default:
                    RaiseSafely(InterruptError, new InterruptErrorEventArgs(record.RuleName, record.Detail, record.TimestampUtc));
                    break;
            }
        }

        private static InterruptPopupEventArgs ToPopupArgs(PopupRecord r) =>
            new InterruptPopupEventArgs(r.RuleName, r.Title, r.MessageText, r.ProcessName, r.ProcessId, r.ButtonClicked, r.Attempts, r.TimestampUtc, r.Detail);

        /// <summary>
        /// Raises an event, isolating each subscriber so one that throws cannot stop the others or
        /// the worker. There is no call in progress by the time a handler runs, so a failure is
        /// only logged at debug level.
        /// </summary>
        private void RaiseSafely<TArgs>(EventHandler<TArgs> handler, TArgs args) where TArgs : EventArgs
        {
            if (handler == null)
                return;

            foreach (EventHandler<TArgs> single in handler.GetInvocationList())
            {
                try
                {
                    single(this, args);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("InterruptUtils: an event handler threw: " + ex);
                }
            }
        }

        #endregion

        #region Teardown

        private bool IsDisposed(out string message)
        {
            lock (_lifeLock)
            {
                if (_disposed)
                {
                    message = "The component has been disposed.";
                    return true;
                }
            }
            message = null;
            return false;
        }

        /// <summary>
        /// Ends the watch: detaches the run's state under the lock, then stops the hook and joins
        /// the worker outside it, so a worker that is mid-click can never deadlock against a caller.
        /// </summary>
        private void StopCore()
        {
            Thread worker;
            CancellationTokenSource cts;
            lock (_lifeLock)
            {
                worker = _worker;
                cts = _cts;
                _worker = null;
                _cts = null;
                if (worker != null)
                {
                    // Recorded before the join, so a worker that ends first can still find its
                    // run to release; whichever of the two finishes last does the cleaning up.
                    _lingeringWorker = worker;
                    _lingeringCts = cts;
                }
            }
            if (worker == null)
                return;

            // Cancelling and joining the worker must happen even if unhooking throws: otherwise the
            // handler would go on dismissing popups while the component reports it is stopped.
            try
            {
                _hook.Stop();
            }
            finally
            {
                cts.Cancel();
                _engine.Wake();
                // If it does not end in time it is mid-click on a slow application and ends on its own
                // once that returns, releasing the run itself.
                ReleaseRun(cts, worker.Join(3000));
            }
        }

        /// <summary>
        /// Releases a finished run's cancellation source, the instance guard, and the engine too once
        /// the component has been disposed. Called both by the thread that stopped the run (after
        /// joining the worker) and by the worker as it exits; only the first call that finds the run
        /// finished acts.
        /// </summary>
        private void ReleaseRun(CancellationTokenSource cts, bool workerHasEnded)
        {
            lock (_lifeLock)
            {
                if (!workerHasEnded || !ReferenceEquals(_lingeringCts, cts))
                    return;
                FinishLingeringRunLocked();
            }
        }

        /// <summary>
        /// Cleans up a run whose worker has ended. The caller holds <c>_lifeLock</c>. The guard is given
        /// back here, in the same critical section that clears the run, so a restart can never slip in
        /// between and then have its own guard released by this run's late clean-up. Only now may another
        /// instance start watching: until the worker has ended it could still act on a popup. Giving the
        /// guard back does not wait for anything, so holding the lock is safe.
        /// </summary>
        private void FinishLingeringRunLocked()
        {
            CancellationTokenSource cts = _lingeringCts;
            _lingeringCts = null;
            _lingeringWorker = null;
            cts?.Dispose();
            _guard.Release();
            if (_disposed)
                _engine.Dispose();
        }

        /// <summary>Stops watching and releases the component's threads.</summary>
        /// <param name="disposing"><c>true</c> when called from <c>Dispose</c>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lifeLock)
                    _disposed = true;

                try
                {
                    StopCore();
                    // A worker that outlived Stop releases the engine itself when it finishes
                    // (ReleaseRun sees _disposed); otherwise there is nothing left using it.
                    lock (_lifeLock)
                    {
                        if (_lingeringCts == null)
                            _engine.Dispose();
                    }
                }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
                {
                    System.Diagnostics.Debug.WriteLine("InterruptUtils: dispose failed: " + ex.Message);
                }
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
