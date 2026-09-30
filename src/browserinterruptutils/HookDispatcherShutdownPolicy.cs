namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The pure decision behind <c>BrowserPopupHookThread</c>'s dispatcher teardown, with no
    /// <c>System.Windows.Automation</c>/WPF dependency so it compiles (and is unit-tested) in both
    /// build configurations. Deliberately plain <c>&lt;c&gt;</c> tags for the hook type, which does not
    /// exist under <c>-p:UseWPF=false</c>.
    /// <para>
    /// Why it exists: a hook thread that failed its setup or registration returns WITHOUT ever
    /// entering <c>Dispatcher.Run()</c>. Nothing pumps that thread's dispatcher, so a synchronous
    /// <c>Dispatcher.InvokeShutdown()</c> (a Send-priority <c>Invoke</c> onto the dispatcher's
    /// thread) never returns and <c>Start</c> would hang, holding its lifecycle lock, instead of
    /// returning false. Such a thread needs no dispatcher shutdown at all - it is already exiting on
    /// its own, so the caller only does a bounded <c>Thread.Join</c>. For a thread that IS pumping,
    /// the shutdown is requested asynchronously (<c>BeginInvokeShutdown</c>) and likewise followed
    /// by a bounded join, so even a wedged pump cannot block the caller.
    /// </para>
    /// </summary>
    internal static class HookDispatcherShutdownPolicy
    {
        /// <summary>
        /// True only when the thread entered (or is guaranteed to enter) its dispatcher pump and no
        /// shutdown has been started or finished yet.
        /// </summary>
        /// <param name="pumpExpected">The hook thread completed its setup and will run (or is running) <c>Dispatcher.Run()</c>.</param>
        /// <param name="shutdownStarted">The dispatcher's <c>HasShutdownStarted</c>.</param>
        /// <param name="shutdownFinished">The dispatcher's <c>HasShutdownFinished</c>.</param>
        public static bool ShouldRequestShutdown(bool pumpExpected, bool shutdownStarted, bool shutdownFinished)
        {
            return pumpExpected && !shutdownStarted && !shutdownFinished;
        }
    }
}
