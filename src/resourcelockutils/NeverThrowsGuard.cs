using System;

namespace ResourceLockAutomation
{
    internal static class NeverThrowsGuard
    {
        internal static bool IsRecoverable(Exception exception) =>
            exception is not OutOfMemoryException &&
            exception is not StackOverflowException &&
            exception is not AccessViolationException;

        /// <summary>
        /// The message for an unexpected recoverable failure. It names the operation and the exception type only: the exception's own text can
        /// echo a file path or a holder's details, so that text is never surfaced.
        /// </summary>
        internal static string Failure(string operation, Exception exception)
        {
            LastUnexpected = exception;
            return operation + " failed unexpectedly (" + exception.GetType().Name + ").";
        }

        /// <summary>The last unexpected exception, for diagnostics in tests only; never surfaced by the component.</summary>
        internal static Exception LastUnexpected { get; private set; }
    }
}
