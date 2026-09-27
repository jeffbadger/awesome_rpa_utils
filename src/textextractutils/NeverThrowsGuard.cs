using System;

namespace TextExtractAutomation
{
    internal static class NeverThrowsGuard
    {
        internal static bool IsRecoverable(Exception exception) =>
            exception is not OutOfMemoryException &&
            exception is not StackOverflowException &&
            exception is not AccessViolationException;

        /// <summary>
        /// The message for an unexpected recoverable failure. It names the operation and the exception type only:
        /// the exception's own text can echo source data (a JSON fragment, a field value), and this component
        /// handles business text, so that text is never surfaced.
        /// </summary>
        internal static string Failure(string operation, Exception exception) =>
            operation + " failed unexpectedly (" + exception.GetType().Name + ").";
    }
}
