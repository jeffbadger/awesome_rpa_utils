using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// Pega Robot Studio-ready component that watches for known browser popups - native JS
    /// dialogs (<c>alert</c>/<c>confirm</c>/<c>prompt</c>) and in-page ARIA modal overlays - on
    /// its own background threads and dismisses them while the automation is busy. Mirrors
    /// <c>InterruptAutomation.InterruptUtils</c> architecturally, wiring together the data
    /// contracts, the thread-free <see cref="BrowserPopupEngine"/>, the real UIA probe
    /// (<c>UiaBrowserPopupProbe</c>) and the real UIA hook (<c>BrowserPopupHookThread</c>) built
    /// in earlier phases.
    /// <para>
    /// Describe each popup once with an <c>Add...Rule</c> method (by name, message text, process
    /// and/or role, plus what to do about it), call <see cref="Start"/>, and carry on. It notices
    /// new windows and page-structure changes through UI Automation events (and periodic sweeps
    /// as a safety net), acts on a worker thread, and records the outcome in a log you can query
    /// and in events.
    /// </para>
    /// </summary>
    [Description("Watches for known browser popups (native dialogs and in-page overlays) in the background and " +
                 "dismisses them while the automation is busy. Define rules by name/message/process/role, then " +
                 "Start. Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class BrowserInterruptUtils : Component
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly BrowserPopupEngine _engine;
        private readonly IBrowserPopupHookSource _hook;
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
        public BrowserInterruptUtils() : this(CreateDefaultProbe(), CreateDefaultHook(), new NamedInstanceGuard())
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public BrowserInterruptUtils(IContainer container) : this()
        {
            container?.Add(this);
        }

        internal BrowserInterruptUtils(IBrowserPopupProbe probe, IBrowserPopupHookSource hook, IInstanceGuard guard = null)
        {
            _hook = hook;
            _guard = guard ?? new NoInstanceGuard();
            _engine = new BrowserPopupEngine(probe, hook, OnRecord);
        }

        // The production probe/hook are real System.Windows.Automation-backed types, which only
        // exist in this assembly when UseWPF (or UseWindowsForms) is true - see the matching
        // "#if BROWSERINTERRUPT_UIA" comment in BrowserInterruptUtils.csproj. Isolating the two
        // `new` calls behind these factory methods, rather than referencing the types directly
        // in the constructor initializer above, is what lets this whole file (and therefore the
        // internal test constructor above) stay compiled - and this file's own tests runnable -
        // under the local -p:UseWPF=false convenience Tasks 3-4 established for running the
        // fake-driven tests on a non-Windows dev host, where UiaBrowserPopupProbe.cs and
        // BrowserPopupHookThread.cs are excluded from the build entirely.
#if BROWSERINTERRUPT_UIA
        private static IBrowserPopupProbe CreateDefaultProbe() => new UiaBrowserPopupProbe();
        private static IBrowserPopupHookSource CreateDefaultHook() => new BrowserPopupHookThread();
#else
        private static IBrowserPopupProbe CreateDefaultProbe() =>
            throw new PlatformNotSupportedException("BrowserInterruptUtils requires UI Automation (build with UseWPF=true).");
        private static IBrowserPopupHookSource CreateDefaultHook() =>
            throw new PlatformNotSupportedException("BrowserInterruptUtils requires UI Automation (build with UseWPF=true).");
#endif

        #region Events

        /// <summary>Raised on a worker thread when a popup that matches a watch-only rule appears.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised when a popup matching a watch-only rule appears (it is not touched). Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<BrowserPopupEventArgs> PopupDetected;

        /// <summary>Raised on a worker thread after a popup was dismissed.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised after a popup was dismissed by a rule. Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<BrowserPopupEventArgs> PopupDismissed;

        /// <summary>Raised on a worker thread when a popup matched a rule but could not be dismissed.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised when a popup matched a rule but could not be dismissed; Detail says why. Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<BrowserPopupEventArgs> PopupDismissFailed;

        /// <summary>Raised on a worker thread when the handler itself has a problem, such as a rule that stopped for dismissing too many popups.</summary>
        [Category("Interrupt - Events")]
        [Description("Raised when the handler has a problem, such as a rule that stopped for dismissing too many popups. Raised on a worker thread, not the automation's thread.")]
        public event EventHandler<BrowserInterruptErrorEventArgs> InterruptError;

        #endregion

        #region Rules

        /// <summary>Adds a rule that dismisses a matching native dialog by invoking the descendant element with the given name (typically a button).</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the popup's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="targetElementName">The name of the element to invoke (for example a button's text).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="exactTargetElementName"><c>true</c> to need the element's whole name; <c>false</c> to accept the first descendant whose name contains it.</param>
        /// <param name="roleContains">Text the popup's UIA localized control type must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching native dialog by invoking the named descendant element. Returns True if added; never throws.")]
        public bool AddNativeDialogDismissRuleByName(string ruleName, string nameContains, string messageContains, string processName,
            string targetElementName, out string message, bool exactTargetElementName = true, string roleContains = null)
        {
            message = default;
            try
            {
                string target = (targetElementName ?? string.Empty).Trim();
                if (target.Length == 0)
                {
                    message = "targetElementName may not be empty.";
                    return false;
                }
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.NativeDialog,
                    Action = BrowserPopupAction.InvokeByName,
                    TargetElementName = target,
                    ExactTargetElementName = exactTargetElementName
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains: null, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddNativeDialogDismissRuleByName", ex);
                return false;
            }
        }

        /// <summary>Adds a rule that dismisses a matching native dialog by invoking the descendant element with the given automation ID.</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the popup's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="targetAutomationId">The automation ID of the element to invoke, matched exactly (ignoring case).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="roleContains">Text the popup's UIA localized control type must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching native dialog by invoking the descendant element with the given automation ID. Returns True if added; never throws.")]
        public bool AddNativeDialogDismissRuleByAutomationId(string ruleName, string nameContains, string messageContains, string processName,
            string targetAutomationId, out string message, string roleContains = null)
        {
            message = default;
            try
            {
                string target = (targetAutomationId ?? string.Empty).Trim();
                if (target.Length == 0)
                {
                    message = "targetAutomationId may not be empty.";
                    return false;
                }
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.NativeDialog,
                    Action = BrowserPopupAction.InvokeByAutomationId,
                    TargetAutomationId = target
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains: null, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddNativeDialogDismissRuleByAutomationId", ex);
                return false;
            }
        }

        /// <summary>Adds a rule that dismisses a matching native dialog by closing its window, for a dialog with no element worth invoking.</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the popup's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="roleContains">Text the popup's UIA localized control type must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching native dialog by closing its window. Returns True if added; never throws.")]
        public bool AddNativeDialogCloseRule(string ruleName, string nameContains, string messageContains, string processName,
            out string message, string roleContains = null)
        {
            message = default;
            try
            {
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.NativeDialog,
                    Action = BrowserPopupAction.CloseWindowPattern
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains: null, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddNativeDialogCloseRule", ex);
                return false;
            }
        }

        /// <summary>Adds a rule that only reports a matching native dialog (<see cref="PopupDetected"/> and the log); the dialog is never touched.</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the popup's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the popup's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning process's name, with or without <c>.exe</c>; empty to not check the process.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="roleContains">Text the popup's UIA localized control type must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that only reports a matching native dialog and never touches it. Returns True if added; never throws.")]
        public bool AddNativeDialogWatchOnlyRule(string ruleName, string nameContains, string messageContains, string processName,
            out string message, string roleContains = null)
        {
            message = default;
            try
            {
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.NativeDialog,
                    Action = BrowserPopupAction.WatchOnly
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains: null, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddNativeDialogWatchOnlyRule", ex);
                return false;
            }
        }

        /// <summary>Adds a rule that dismisses a matching in-page overlay by invoking the descendant element with the given name (typically a button).</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the overlay's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the overlay's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning browser process's name, with or without <c>.exe</c>; empty to watch every browser process.</param>
        /// <param name="targetElementName">The name of the element to invoke (for example a button's text).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="exactTargetElementName"><c>true</c> to need the element's whole name; <c>false</c> to accept the first descendant whose name contains it.</param>
        /// <param name="roleContains">Text the overlay's UIA localized control type must contain; empty to not check it.</param>
        /// <param name="automationIdContains">Text the overlay's own automation ID must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        /// <remarks>A rule that does not name a process watches every browser process; overlay discovery only ever costs anything for a process at least one such rule is interested in.</remarks>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching in-page overlay by invoking the named descendant element. Returns True if added; never throws.")]
        public bool AddPageOverlayDismissRuleByName(string ruleName, string nameContains, string messageContains, string processName,
            string targetElementName, out string message, bool exactTargetElementName = true, string roleContains = null, string automationIdContains = null)
        {
            message = default;
            try
            {
                string target = (targetElementName ?? string.Empty).Trim();
                if (target.Length == 0)
                {
                    message = "targetElementName may not be empty.";
                    return false;
                }
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.PageOverlay,
                    Action = BrowserPopupAction.InvokeByName,
                    TargetElementName = target,
                    ExactTargetElementName = exactTargetElementName
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddPageOverlayDismissRuleByName", ex);
                return false;
            }
        }

        /// <summary>Adds a rule that dismisses a matching in-page overlay by invoking the descendant element with the given automation ID.</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the overlay's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the overlay's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning browser process's name, with or without <c>.exe</c>; empty to watch every browser process.</param>
        /// <param name="targetAutomationId">The automation ID of the element to invoke, matched exactly (ignoring case).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="roleContains">Text the overlay's UIA localized control type must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that dismisses a matching in-page overlay by invoking the descendant element with the given automation ID. Returns True if added; never throws.")]
        public bool AddPageOverlayDismissRuleByAutomationId(string ruleName, string nameContains, string messageContains, string processName,
            string targetAutomationId, out string message, string roleContains = null)
        {
            message = default;
            try
            {
                string target = (targetAutomationId ?? string.Empty).Trim();
                if (target.Length == 0)
                {
                    message = "targetAutomationId may not be empty.";
                    return false;
                }
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.PageOverlay,
                    Action = BrowserPopupAction.InvokeByAutomationId,
                    TargetAutomationId = target
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains: null, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddPageOverlayDismissRuleByAutomationId", ex);
                return false;
            }
        }

        /// <summary>Adds a rule that only reports a matching in-page overlay (<see cref="PopupDetected"/> and the log); the overlay is never touched.</summary>
        /// <param name="ruleName">A name for the rule, unique among rules (ignoring case).</param>
        /// <param name="nameContains">Text the overlay's own name must contain (ignoring case); empty to not check it.</param>
        /// <param name="messageContains">Text the overlay's message must contain (ignoring case); empty to not check it.</param>
        /// <param name="processName">The owning browser process's name, with or without <c>.exe</c>; empty to watch every browser process.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the rule was not added.</param>
        /// <param name="roleContains">Text the overlay's UIA localized control type must contain; empty to not check it.</param>
        /// <param name="automationIdContains">Text the overlay's own automation ID must contain; empty to not check it.</param>
        /// <returns><c>true</c> if the rule was added; <c>false</c> if an argument is invalid, the name is taken, or the component is disposed. Never throws.</returns>
        [Category("Interrupt - Rules")]
        [Description("Adds a rule that only reports a matching in-page overlay and never touches it. Returns True if added; never throws.")]
        public bool AddPageOverlayWatchOnlyRule(string ruleName, string nameContains, string messageContains, string processName,
            out string message, string roleContains = null, string automationIdContains = null)
        {
            message = default;
            try
            {
                return AddRule(new BrowserPopupRule
                {
                    Scope = BrowserPopupScope.PageOverlay,
                    Action = BrowserPopupAction.WatchOnly
                }, ruleName, nameContains, messageContains, processName, roleContains, automationIdContains, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                message = NeverThrowsGuard.Failure("AddPageOverlayWatchOnlyRule", ex);
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
        /// Turns a rule off or on without removing it. Turning a rule on also clears a stop
        /// caused by dismissing too many popups.
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
                    _engine.TryGetCount(rule.RuleName, out int count);
                    items.Add(new
                    {
                        name = rule.RuleName,
                        scope = rule.Scope.ToString(),
                        action = rule.Action.ToString(),
                        nameContains = rule.NameContains ?? string.Empty,
                        messageContains = rule.MessageContains ?? string.Empty,
                        processName = rule.ProcessName ?? string.Empty,
                        roleContains = rule.RoleContains ?? string.Empty,
                        automationIdContains = rule.AutomationIdContains ?? string.Empty,
                        target = rule.Action == BrowserPopupAction.InvokeByAutomationId ? rule.TargetAutomationId
                            : rule.Action == BrowserPopupAction.InvokeByName ? rule.TargetElementName : string.Empty,
                        exactTargetName = rule.ExactTargetElementName,
                        enabled = rule.Enabled,
                        stopped = _engine.IsRuleTripped(rule.RuleName),
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

        private bool AddRule(BrowserPopupRule rule, string ruleName, string nameContains, string messageContains,
            string processName, string roleContains, string automationIdContains, out string message)
        {
            if (IsDisposed(out message))
                return false;

            // Normalize first, then let the engine validate what will actually be stored: a
            // process name of ".exe" trims to nothing, and a rule left with no criterion would
            // match every popup (see BrowserPopupRule.ValidateCommon).
            rule.RuleName = (ruleName ?? string.Empty).Trim();
            rule.NameContains = (nameContains ?? string.Empty).Trim();
            rule.MessageContains = (messageContains ?? string.Empty).Trim();
            rule.ProcessName = BrowserPopupRule.TrimExe(processName);
            rule.RoleContains = (roleContains ?? string.Empty).Trim();
            rule.AutomationIdContains = (automationIdContains ?? string.Empty).Trim();
            return _engine.AddRule(rule, out message);
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts watching for browser popups on background threads and returns as soon as the
        /// UI Automation hooks are installed, then the automation carries on while popups
        /// matching a rule are dismissed.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason it did not start.</param>
        /// <param name="sweepIntervalMs">How often, in milliseconds, to scan for native-dialog windows the hook missed (and ones already open now); 0 turns the scan off. 0 to 60000.</param>
        /// <param name="overlaySweepIntervalMs">How often, in milliseconds, to sweep every watched browser window's page for overlays; 0 turns the periodic sweep off (discovery still happens on page-structure change events). 0 to 60000; clamped up to a small floor by the engine when above zero.</param>
        /// <param name="maxAttempts">How many times to try to dismiss one popup before giving up on it. 1 to 10.</param>
        /// <param name="maxDismissalsPerMinute">How many popups one rule may dismiss in a minute before it stops itself. 1 to 1000.</param>
        /// <param name="maxOverlayNodes">The most elements a single overlay-discovery walk may visit. 1 to 100000.</param>
        /// <param name="maxOverlayDepth">The deepest a single overlay-discovery walk may descend. 1 to 1000.</param>
        /// <returns><c>true</c> if watching started; <c>false</c> if it is already running, a value is out of range, the hook is unavailable, a previous run is still shutting down, another instance would not stop, or the component is disposed. Never throws.</returns>
        /// <remarks>
        /// Popups owned by the automation's own process are never touched. Stop it with
        /// <see cref="Stop"/>; disposing the component stops it too. Only one instance watches at
        /// a time, in this process or any other in the user's session: starting one asks a
        /// running one to stop first (it keeps its rules, counts and log), and this method fails
        /// if that instance has not stopped within a few seconds.
        /// </remarks>
        [Category("Interrupt - Lifecycle")]
        [Description("Starts watching for browser popups in the background and dismissing those that match a rule. Stops any other instance still watching (one that has this guard), in this process or another, and waits a few seconds for it. Returns True on success; never throws.")]
        public bool Start(out string message, int sweepIntervalMs = 1000, int overlaySweepIntervalMs = 2000,
            int maxAttempts = 3, int maxDismissalsPerMinute = 20, int maxOverlayNodes = 5000, int maxOverlayDepth = 50)
        {
            message = default;
            try
            {
                if (sweepIntervalMs < 0 || sweepIntervalMs > 60000)
                {
                    message = "sweepIntervalMs must be between 0 and 60000.";
                    return false;
                }
                if (overlaySweepIntervalMs < 0 || overlaySweepIntervalMs > 60000)
                {
                    message = "overlaySweepIntervalMs must be between 0 and 60000.";
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
                if (maxOverlayNodes < 1 || maxOverlayNodes > 100000)
                {
                    message = "maxOverlayNodes must be between 1 and 100000.";
                    return false;
                }
                if (maxOverlayDepth < 1 || maxOverlayDepth > 1000)
                {
                    message = "maxOverlayDepth must be between 1 and 1000.";
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
                        _engine.OverlaySweepIntervalMs = overlaySweepIntervalMs;
                        _engine.MaxAttempts = maxAttempts;
                        _engine.MaxDismissalsPerMinute = maxDismissalsPerMinute;
                        _engine.MaxOverlayNodes = maxOverlayNodes;
                        _engine.MaxOverlayDepth = maxOverlayDepth;
                        _engine.ResetRuntime();

                        // A hook failure is only queued here (it arrives on the hook thread); the worker
                        // records it, so InterruptError is raised on the worker thread like every event.
                        if (!_hook.Start(_engine.EnqueueFault, out string hookMessage))
                        {
                            message = hookMessage ?? "UI Automation events could not be started.";
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
                                Name = "BrowserInterruptUtils.Worker"
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
        [Description("Stops watching for browser popups. Rules and the log are kept. Returns True on success, including when not running; never throws.")]
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
        [Description("Returns True while watching for browser popups is running. Never throws.")]
        public bool IsRunning()
        {
            lock (_lifeLock)
                return _worker != null && !_disposed;
        }

        /// <summary>
        /// Stops the handler from touching popups until <see cref="Resume"/>, without stopping
        /// the watch. Use it around a step that drives a dialog or overlay itself. Popups that
        /// appear meanwhile and are still open when it resumes are then dealt with.
        /// </summary>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the component is disposed. Never throws.</returns>
        /// <remarks>
        /// Unlike <c>InterruptUtils.Pause</c>, this does not wait for an in-flight dismissal to
        /// finish: <see cref="BrowserPopupEngine"/> has no poll-and-verify action loop (a click or
        /// close either succeeds or fails synchronously within a single <c>Pump</c> pass), so
        /// there is no multi-step action window for this call to wait out.
        /// </remarks>
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
                hasUnresolvedPopup = _engine.HasUnresolvedPopup;
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
                BrowserPopupRecord last = _engine.LastRecord();
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
                if (maxEntries < 1 || maxEntries > BrowserPopupEngine.LogCapacity)
                {
                    message = "maxEntries must be between 1 and " + BrowserPopupEngine.LogCapacity + ".";
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

        private static object ToJsonObject(BrowserPopupRecord record) => new
        {
            kind = record.Kind.ToString(),
            rule = record.RuleName,
            scope = record.Scope,
            name = record.Name,
            role = record.Role,
            message = record.MessageText,
            process = record.ProcessName,
            processId = record.ProcessId,
            target = record.TargetInvoked,
            attempts = record.Attempts,
            timestampUtc = record.TimestampUtc.ToString("o"),
            detail = record.Detail
        };

        #endregion

        #region Worker

        /// <summary>
        /// The one genuinely new piece of infrastructure this component needed: nothing else
        /// drives <see cref="BrowserPopupEngine.Pump"/>. Loops calling it, sleeping between
        /// passes for whatever time it says the next candidate is due (bounded to 500ms so a
        /// disposed <c>AutoResetEvent</c>/cancellation is never waited on for longer than that),
        /// and waking early via <see cref="BrowserPopupEngine.WaitForWork"/> the moment a hook
        /// event, rule change or fault arrives. Exactly mirrors <c>InterruptUtils.WorkerLoop</c>.
        /// </summary>
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
                // If Stop gave up waiting for this worker, the run's resources are released here,
                // once it has finished.
                ReleaseRun(cts, workerHasEnded: true);
            }
        }

        /// <summary>
        /// The engine's record sink, passed to its constructor: called synchronously from within
        /// <see cref="BrowserPopupEngine.Pump"/> as each record is produced, which is always on
        /// this component's own worker thread (the only thread that ever calls <c>Pump</c>) - so
        /// there is no separate "new since last drain" bookkeeping to do here, unlike a design
        /// where the log would need to be diffed after the fact.
        /// </summary>
        private void OnRecord(BrowserPopupRecord record)
        {
            switch (record.Kind)
            {
                case BrowserPopupRecordKind.Detected:
                    RaiseSafely(PopupDetected, ToPopupArgs(record));
                    break;
                case BrowserPopupRecordKind.Dismissed:
                    RaiseSafely(PopupDismissed, ToPopupArgs(record));
                    break;
                case BrowserPopupRecordKind.DismissFailed:
                    RaiseSafely(PopupDismissFailed, ToPopupArgs(record));
                    break;
                default:
                    RaiseSafely(InterruptError, new BrowserInterruptErrorEventArgs(record.RuleName, record.Detail, record.TimestampUtc));
                    break;
            }
        }

        private static BrowserPopupEventArgs ToPopupArgs(BrowserPopupRecord r) =>
            new BrowserPopupEventArgs(r.RuleName, r.Scope, r.Name, r.MessageText, r.Role, r.ProcessName, r.ProcessId, r.TargetInvoked, r.Attempts, r.TimestampUtc, r.Detail);

        /// <summary>
        /// Raises an event, isolating each subscriber so one that throws cannot stop the others or
        /// the worker.
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
                    System.Diagnostics.Debug.WriteLine("BrowserInterruptUtils: an event handler threw: " + ex);
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
        /// the worker outside it, so a worker that is mid-pass can never deadlock against a caller.
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
                // If it does not end in time it is mid-pass and ends on its own once that
                // returns, releasing the run itself.
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
        /// between and then have its own guard released by this run's late clean-up.
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
                    System.Diagnostics.Debug.WriteLine("BrowserInterruptUtils: dispose failed: " + ex.Message);
                }
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
