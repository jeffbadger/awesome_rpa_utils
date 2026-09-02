using System;

namespace TerminalAutomation
{
    internal static class NeverThrowsGuard
    {
        internal static bool IsRecoverable(Exception exception) => exception is not OutOfMemoryException && exception is not StackOverflowException && exception is not AccessViolationException;
        internal static string Failure(string operation, Exception exception) => $"{operation} failed unexpectedly ({exception.GetType().Name}): {exception.Message}";
    }
}
