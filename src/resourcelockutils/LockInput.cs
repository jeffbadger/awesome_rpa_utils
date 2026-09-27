using System;
using System.IO;
using System.Linq;

namespace ResourceLockAutomation
{
    /// <summary>
    /// Checks every input before anything is done. Each check returns null when the input is valid, or the reason it is not. Reasons name the
    /// parameter and the rule, never the value: a resource name or holder can carry business or robot details.
    /// </summary>
    internal static class LockInput
    {
        // Windows device names cannot be file names, even with an extension (CON.1.lease opens the console).
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly char[] WindowsPathForbidden = { '"', '<', '>', '|', '*', '?' };

        internal static string Scope(LockScope scope) =>
            Enum.IsDefined(typeof(LockScope), scope) ? null : "scope is not a known LockScope (Process or Machine)";

        /// <summary>ASCII letters, digits, - _ and . (it becomes part of a file name on any file system); starts with a letter or digit, does not end with a dot, is not a
        /// Windows device name; at most 100 characters. Names are compared ignoring case.</summary>
        internal static string Resource(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return "resource is required";
            if (resource.Length > LockLimits.MaxResourceLength) return "resource may have at most " + LockLimits.MaxResourceLength + " characters";
            if (!resource.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.'))
                return "resource may contain only ASCII letters, digits, -, _ and .";
            if (!char.IsLetterOrDigit(resource[0])) return "resource must start with a letter or digit";
            if (resource[resource.Length - 1] == '.') return "resource must not end with a dot";
            string stem = resource.Split('.')[0];
            if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) return "resource must not be a Windows device name such as CON, NUL, COM1 or LPT1";
            return null;
        }

        /// <summary>Who holds the lock, as shown to other robots (typically the robot name): 1 to 128 characters, no control characters.</summary>
        internal static string Holder(string holder)
        {
            if (string.IsNullOrWhiteSpace(holder)) return "holder is required (typically the robot name)";
            if (holder.Length > LockLimits.MaxHolderLength) return "holder may have at most " + LockLimits.MaxHolderLength + " characters";
            if (holder.Any(char.IsControl)) return "holder must not contain line breaks or other control characters";
            return null;
        }

        internal static string LeaseSeconds(int leaseSeconds) =>
            leaseSeconds >= LockLimits.MinLeaseSeconds && leaseSeconds <= LockLimits.MaxLeaseSeconds
                ? null
                : "leaseSeconds must be between " + LockLimits.MinLeaseSeconds + " and " + LockLimits.MaxLeaseSeconds;

        internal static string WaitMilliseconds(int waitMilliseconds) =>
            waitMilliseconds >= 0 && waitMilliseconds <= LockLimits.MaxWaitMilliseconds
                ? null
                : "waitMilliseconds must be between 0 and " + LockLimits.MaxWaitMilliseconds;

        internal static string Capacity(int capacity) =>
            capacity >= LockLimits.MinCapacity && capacity <= LockLimits.MaxCapacity
                ? null
                : "capacity must be between " + LockLimits.MinCapacity + " and " + LockLimits.MaxCapacity;

        /// <summary>A token as returned by an acquire call: 32 hexadecimal characters.</summary>
        internal static string Token(string token)
        {
            if (string.IsNullOrEmpty(token)) return "token is required (the token returned when the lock was acquired)";
            if (token.Length != 32 || !token.All(Uri.IsHexDigit)) return "token is not a token returned by an acquire call";
            return null;
        }

        /// <summary>An absolute local folder path, or empty for the default. A network path belongs to a later Shared scope.</summary>
        internal static string FolderPath(string folderPath)
        {
            if (folderPath == null) return "folderPath is required (empty for the default folder)";
            if (folderPath.Length == 0) return null;
            if (folderPath.Length > LockLimits.MaxFolderPathLength) return "folderPath may have at most " + LockLimits.MaxFolderPathLength + " characters";
            // Explicit rather than Path.GetInvalidPathChars, which on Windows lets wildcards and quotes through (C:\RobotLocks* names no folder).
            if (folderPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || folderPath.Any(char.IsControl) || folderPath.IndexOfAny(WindowsPathForbidden) >= 0
                || folderPath.IndexOf(':', 2) >= 0)
                return "folderPath contains characters a Windows path cannot have (\" < > | * ? or a : after the drive letter)";
            if (folderPath.StartsWith(@"\\", StringComparison.Ordinal) || folderPath.StartsWith("//", StringComparison.Ordinal))
                return "folderPath must be a local folder; network paths are not supported by the Machine scope";
            if (!Path.IsPathFullyQualified(folderPath)) return "folderPath must be an absolute path, such as D:\\RobotLocks";
            return null;
        }

        /// <summary>The default lock folder: machine-wide, so Server Bots under different Windows accounts share it.</summary>
        internal static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AwesomeRpaUtils", "Locks");
    }
}
