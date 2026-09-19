using System;

namespace ClipboardAutomation
{
    /// <summary>
    /// Decides which exceptions the never-throws public methods may convert into a
    /// <c>false</c> result plus a message. Duplicated in every component on purpose:
    /// the components are standalone and share no project references.
    /// </summary>
    internal static class NeverThrowsGuard
    {
        internal static bool IsRecoverable(Exception exception) =>
            exception is not OutOfMemoryException &&
            exception is not StackOverflowException &&
            exception is not AccessViolationException;

        internal static string Failure(string operation, Exception exception) =>
            operation + " failed unexpectedly (" + exception.GetType().Name + "): " + exception.Message;
    }
}
