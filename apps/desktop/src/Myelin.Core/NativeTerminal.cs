using System;
using System.Runtime.InteropServices;
using System.Text;
using Myelin.Core.Native;

namespace Myelin.Core
{
    public unsafe class NativeTerminal : IDisposable
    {
        private TerminalHandle* _handle;
        private int _disposedFlag;

        public string? WorkingDir { get; }
        public string? ShellPath { get; }
        public string? ShellArgs { get; }

        public NativeTerminal(ushort cols = 120, ushort rows = 30, string? workingDir = null, string? shellPath = null, string? shellArgs = null)
        {
            WorkingDir = workingDir;
            ShellPath = shellPath;
            ShellArgs = shellArgs;

            byte[]? dirBytes = workingDir != null ? Encoding.UTF8.GetBytes(workingDir + "\0") : null;
            byte[]? shellBytes = shellPath != null ? Encoding.UTF8.GetBytes(shellPath + "\0") : null;
            byte[]? argsBytes = shellArgs != null ? Encoding.UTF8.GetBytes(shellArgs + "\0") : null;

            fixed (byte* pDir = dirBytes)
            fixed (byte* pShell = shellBytes)
            fixed (byte* pArgs = argsBytes)
            {
                _handle = NativeMethods.myelin_terminal_create_profile(cols, rows, pDir, pShell, pArgs);
            }

            if (_handle == null)
            {
                throw new InvalidOperationException(
                    $"Failed to create native terminal session with shell '{shellPath ?? "default"}' (PTY spawn failed).");
            }
        }

        /// <summary>True when the underlying native handle exists and has not been disposed.</summary>
        public bool IsValid => !IsDisposed && _handle != null;

        public bool IsDisposed => _disposedFlag != 0;

        public bool Write(string text)
        {
            if (_disposedFlag != 0 || _handle == null) return false;
            try
            {
                fixed (byte* p = Encoding.UTF8.GetBytes(text + "\0"))
                {
                    return NativeMethods.myelin_terminal_write(_handle, p) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public string ReadAvailable()
        {
            if (_disposedFlag != 0 || _handle == null) return string.Empty;
            byte* strPtr = null;
            try
            {
                strPtr = NativeMethods.myelin_terminal_read_available(_handle);
                if (strPtr == null) return string.Empty;
                return Marshal.PtrToStringUTF8((IntPtr)strPtr) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (strPtr != null)
                {
                    NativeMethods.myelin_free_string(strPtr);
                }
            }
        }

        public string ReadAvailableRaw()
        {
            if (_disposedFlag != 0 || _handle == null) return string.Empty;
            byte* strPtr = null;
            try
            {
                strPtr = NativeMethods.myelin_terminal_read_raw(_handle);
                if (strPtr == null) return string.Empty;
                return Marshal.PtrToStringUTF8((IntPtr)strPtr) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (strPtr != null)
                {
                    NativeMethods.myelin_free_string(strPtr);
                }
            }
        }

        public bool Resize(ushort cols, ushort rows)
        {
            if (_disposedFlag != 0 || _handle == null) return false;
            try
            {
                return NativeMethods.myelin_terminal_resize(_handle, cols, rows) == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool IsAlive
        {
            get
            {
                if (_disposedFlag != 0 || _handle == null) return false;
                return NativeMethods.myelin_terminal_is_alive(_handle) == 1;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposedFlag != 0 || _handle == null)
            {
                throw new ObjectDisposedException(nameof(NativeTerminal));
            }
        }

        public void Dispose()
        {
            // Interlocked guard: exactly one caller (finalizer or explicit
            // Dispose) wins the race and destroys the native handle.
            if (Interlocked.Exchange(ref _disposedFlag, 1) == 1) return;

            var handle = _handle;
            _handle = null;
            if (handle != null)
            {
                NativeMethods.myelin_terminal_destroy(handle);
            }
            GC.SuppressFinalize(this);
        }

        ~NativeTerminal()
        {
            Dispose();
        }
    }
}
