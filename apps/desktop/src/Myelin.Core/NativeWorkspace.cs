using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Myelin.Core.Models;
using Myelin.Core.Native;

namespace Myelin.Core
{
    public unsafe class NativeWorkspace : IDisposable
    {
        private WorkspaceHandle* _handle;
        private int _disposedFlag;

        public NativeWorkspace(string? rootPath = null)
        {
            if (rootPath != null)
            {
                fixed (byte* p = Encoding.UTF8.GetBytes(rootPath + "\0"))
                {
                    _handle = NativeMethods.myelin_workspace_create(p);
                }
            }
            else
            {
                _handle = NativeMethods.myelin_workspace_create(null);
            }
        }

        public ulong OpenScratch(string initialText = "")
        {
            ThrowIfDisposed();
            fixed (byte* p = Encoding.UTF8.GetBytes(initialText + "\0"))
            {
                return NativeMethods.myelin_workspace_open_scratch(_handle, p);
            }
        }

        public ulong OpenFile(string path)
        {
            ThrowIfDisposed();
            fixed (byte* p = Encoding.UTF8.GetBytes(path + "\0"))
            {
                return NativeMethods.myelin_workspace_open_file(_handle, p);
            }
        }

        public bool CloseDocument(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_workspace_close_doc(_handle, docId) == 1;
        }

        public nuint GetLineCount(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_get_line_count(_handle, docId);
        }

        public string GetLine(ulong docId, nuint lineIdx)
        {
            ThrowIfDisposed();
            byte* strPtr = NativeMethods.myelin_doc_get_line(_handle, docId, lineIdx);
            if (strPtr == null) return string.Empty;

            try
            {
                return Marshal.PtrToStringUTF8((IntPtr)strPtr) ?? string.Empty;
            }
            finally
            {
                NativeMethods.myelin_free_string(strPtr);
            }
        }

        public List<string> GetVisibleLines(ulong docId, nuint startLine, nuint endLine)
        {
            ThrowIfDisposed();
            byte* jsonPtr = NativeMethods.myelin_doc_get_visible_lines_json(_handle, docId, startLine, endLine);
            if (jsonPtr == null) return new List<string>();

            try
            {
                string json = Marshal.PtrToStringUTF8((IntPtr)jsonPtr) ?? "[]";
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            finally
            {
                NativeMethods.myelin_free_string(jsonPtr);
            }
        }

        public List<List<StyledSpan>> GetStyledLines(ulong docId, nuint startLine, nuint endLine)
        {
            ThrowIfDisposed();
            byte* jsonPtr = NativeMethods.myelin_doc_get_styled_lines_json(_handle, docId, startLine, endLine);
            if (jsonPtr == null) return new List<List<StyledSpan>>();

            try
            {
                string json = Marshal.PtrToStringUTF8((IntPtr)jsonPtr) ?? "[]";
                return JsonSerializer.Deserialize<List<List<StyledSpan>>>(json) ?? new List<List<StyledSpan>>();
            }
            finally
            {
                NativeMethods.myelin_free_string(jsonPtr);
            }
        }

        public bool InsertAtCursor(ulong docId, string text)
        {
            ThrowIfDisposed();
            fixed (byte* p = Encoding.UTF8.GetBytes(text + "\0"))
            {
                return NativeMethods.myelin_doc_insert_at_cursor(_handle, docId, p) == 0;
            }
        }

        public bool Backspace(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_backspace(_handle, docId) == 0;
        }

        public bool Undo(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_undo(_handle, docId) == 1;
        }

        public bool Redo(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_redo(_handle, docId) == 1;
        }

        public ulong GetVersion(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_version(_handle, docId);
        }

        public bool DeleteForward(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_delete_forward(_handle, docId) == 0;
        }

        public void SetSelection(ulong docId, nuint anchorLine, nuint anchorCol, nuint headLine, nuint headCol)
        {
            ThrowIfDisposed();
            NativeMethods.myelin_doc_set_selection(_handle, docId, anchorLine, anchorCol, headLine, headCol);
        }

        public (nuint anchorLine, nuint anchorCol, nuint headLine, nuint headCol) GetSelection(ulong docId)
        {
            ThrowIfDisposed();
            nuint al = 0, ac = 0, hl = 0, hc = 0;
            if (NativeMethods.myelin_doc_get_selection(_handle, docId, &al, &ac, &hl, &hc) == 0)
                return (al, ac, hl, hc);
            return (0, 0, 0, 0);
        }

        public (nuint line, nuint col) GetCursor(ulong docId)
        {
            ThrowIfDisposed();
            nuint line = 0, col = 0;
            if (NativeMethods.myelin_doc_get_cursor(_handle, docId, &line, &col) == 0)
            {
                return (line, col);
            }
            return (0, 0);
        }

        public bool SetCursor(ulong docId, nuint line, nuint col)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_set_cursor(_handle, docId, line, col) == 0;
        }

        public bool IsDirty(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_is_dirty(_handle, docId) == 1;
        }

        public bool Save(ulong docId)
        {
            ThrowIfDisposed();
            return NativeMethods.myelin_doc_save(_handle, docId) == 0;
        }

        public static FileNode? ScanDirectory(string dirPath, nuint maxDepth = 4)
        {
            fixed (byte* p = Encoding.UTF8.GetBytes(dirPath + "\0"))
            {
                byte* jsonPtr = NativeMethods.myelin_workspace_scan_dir_json(p, maxDepth);
                if (jsonPtr == null) return null;

                try
                {
                    string json = Marshal.PtrToStringUTF8((IntPtr)jsonPtr) ?? "{}";
                    return JsonSerializer.Deserialize<FileNode>(json);
                }
                finally
                {
                    NativeMethods.myelin_free_string(jsonPtr);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposedFlag != 0 || _handle == null)
            {
                throw new ObjectDisposedException(nameof(NativeWorkspace));
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
                NativeMethods.myelin_workspace_destroy(handle);
            }
            GC.SuppressFinalize(this);
        }

        ~NativeWorkspace()
        {
            Dispose();
        }
    }
}
