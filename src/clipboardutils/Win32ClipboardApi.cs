using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClipboardAutomation
{
    /// <summary>
    /// The real clipboard, through the raw Win32 clipboard functions (no OLE, so any thread will do). Data is
    /// copied as bytes out of the global-memory blocks the clipboard hands over; the one handle-based format that is
    /// worth copying, <c>CF_ENHMETAFILE</c>, goes through <c>GetEnhMetaFileBits</c>.
    /// </summary>
    internal sealed class Win32ClipboardApi : IClipboardApi
    {
        private const int OpenAttempts = 10;
        private const int OpenRetryDelayMs = 25;

        public long SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

        public bool Open(out string error)
        {
            // OpenClipboard fails while another process (a clipboard manager, or the application that just
            // copied) has the clipboard open, usually for a few milliseconds.
            for (int attempt = 0; ; attempt++)
            {
                if (NativeMethods.OpenClipboard(IntPtr.Zero))
                {
                    error = null;
                    return true;
                }
                if (attempt >= OpenAttempts - 1)
                {
                    error = "The clipboard could not be opened; another application is holding it (" + LastError() + ").";
                    return false;
                }
                Thread.Sleep(OpenRetryDelayMs);
            }
        }

        public void Close() => NativeMethods.CloseClipboard();

        public IReadOnlyList<uint> EnumerateFormats()
        {
            var formats = new List<uint>();
            uint format = NativeMethods.EnumClipboardFormats(0);
            while (format != 0)
            {
                formats.Add(format);
                format = NativeMethods.EnumClipboardFormats(format);
            }
            return formats;
        }

        public string GetFormatName(uint id)
        {
            var name = new StringBuilder(256);
            return NativeMethods.GetClipboardFormatName(id, name, name.Capacity) > 0 ? name.ToString() : null;
        }

        public uint RegisterFormat(string name) => NativeMethods.RegisterClipboardFormat(name);

        public bool IsFormatAvailable(uint id) => NativeMethods.IsClipboardFormatAvailable(id);

        public ReadResult ReadFormat(uint id, long maxBytes)
        {
            if (id == ClipboardFormats.CF_ENHMETAFILE)
                return ReadEnhMetafile(maxBytes);
            if (ClipboardFormats.IsHandleBased(id))
                return ReadResult.Unsupported("It holds a GDI handle rather than data that can be copied.");

            // For a format the owner has not rendered yet this asks the owner to produce it, which can take a
            // while, or never return if the owner is hung; the component runs this on a thread it can abandon.
            IntPtr handle = NativeMethods.GetClipboardData(id);
            if (handle == IntPtr.Zero)
                return ReadResult.Failed("The clipboard returned no data for it (" + LastError() + ").");

            long size = (long)(ulong)NativeMethods.GlobalSize(handle);
            if (size > maxBytes || size > int.MaxValue)
                return ReadResult.TooLarge(size);
            if (size == 0)
                return ReadResult.Ok(new byte[0]);

            IntPtr pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return ReadResult.Failed("Its memory could not be locked (" + LastError() + ").");
            try
            {
                var data = new byte[size];
                Marshal.Copy(pointer, data, 0, (int)size);
                return ReadResult.Ok(data);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }

        public bool Empty(out string error)
        {
            if (!NativeMethods.EmptyClipboard())
            {
                error = "The clipboard could not be emptied (" + LastError() + ").";
                return false;
            }
            error = null;
            return true;
        }

        public bool WriteFormat(uint id, byte[] data, out string error)
        {
            if (id == ClipboardFormats.CF_ENHMETAFILE)
                return WriteEnhMetafile(data, out error);
            if (ClipboardFormats.IsHandleBased(id))
            {
                error = "it holds a GDI handle rather than data that can be copied";
                return false;
            }

            // Zero-length data still needs a block to hand over, so an empty format is written as one zero byte.
            IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)(uint)Math.Max(data.Length, 1));
            if (handle == IntPtr.Zero)
            {
                error = "memory for it could not be allocated (" + LastError() + ")";
                return false;
            }

            IntPtr pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                error = "its memory could not be locked (" + LastError() + ")";
                return false;
            }
            try
            {
                if (data.Length > 0)
                    Marshal.Copy(data, 0, pointer, data.Length);
                else
                    Marshal.WriteByte(pointer, 0);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }

            if (NativeMethods.SetClipboardData(id, handle) == IntPtr.Zero)
            {
                // On failure the clipboard has NOT taken ownership: the block is still ours to free.
                string reason = LastError();
                NativeMethods.GlobalFree(handle);
                error = "the clipboard refused it (" + reason + ")";
                return false;
            }
            error = null;
            return true;
        }

        private static ReadResult ReadEnhMetafile(long maxBytes)
        {
            IntPtr handle = NativeMethods.GetClipboardData(ClipboardFormats.CF_ENHMETAFILE);
            if (handle == IntPtr.Zero)
                return ReadResult.Failed("The clipboard returned no data for it (" + LastError() + ").");

            uint size = NativeMethods.GetEnhMetaFileBits(handle, 0, null);
            if (size == 0)
                return ReadResult.Failed("Its size could not be read (" + LastError() + ").");
            if (size > maxBytes)
                return ReadResult.TooLarge(size);

            var data = new byte[size];
            if (NativeMethods.GetEnhMetaFileBits(handle, size, data) != size)
                return ReadResult.Failed("Its data could not be read (" + LastError() + ").");
            return ReadResult.Ok(data);
        }

        private static bool WriteEnhMetafile(byte[] data, out string error)
        {
            IntPtr metafile = NativeMethods.SetEnhMetaFileBits((uint)data.Length, data);
            if (metafile == IntPtr.Zero)
            {
                error = "the metafile could not be rebuilt (" + LastError() + ")";
                return false;
            }
            if (NativeMethods.SetClipboardData(ClipboardFormats.CF_ENHMETAFILE, metafile) == IntPtr.Zero)
            {
                string reason = LastError();
                NativeMethods.DeleteEnhMetaFile(metafile);
                error = "the clipboard refused it (" + reason + ")";
                return false;
            }
            error = null;
            return true;
        }

        private static string LastError()
        {
            int code = Marshal.GetLastWin32Error();
            return "Win32 error " + code + ": " + new Win32Exception(code).Message;
        }
    }

    /// <summary>Presses and releases Ctrl+V as one <c>SendInput</c> batch, so real user input cannot fall in between.</summary>
    internal sealed class Win32KeySender : IKeySender
    {
        private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

        public bool SendPasteChord(out string error)
        {
            var batch = new[]
            {
                Key(NativeMethods.VK_CONTROL, false),
                Key(NativeMethods.VK_V, false),
                Key(NativeMethods.VK_V, true),
                Key(NativeMethods.VK_CONTROL, true)
            };

            uint sent = NativeMethods.SendInput((uint)batch.Length, batch, InputSize);
            if (sent == batch.Length)
            {
                error = null;
                return true;
            }

            int code = Marshal.GetLastWin32Error();
            // Part of the chord may have gone through: make sure neither key is left held down.
            NativeMethods.SendInput(2, new[] { Key(NativeMethods.VK_V, true), Key(NativeMethods.VK_CONTROL, true) }, InputSize);
            error = "Ctrl+V could not be sent (Win32 error " + code + "). Is the desktop locked, or is the target running at a higher privilege level?";
            return false;
        }

        private static NativeMethods.INPUT Key(ushort virtualKey, bool keyUp)
        {
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
            input.U.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0u
            };
            return input;
        }
    }
}
