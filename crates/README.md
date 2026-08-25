# Myelin Systems Core (`crates/`)

The `crates/` directory contains the native Rust engine powering **Myelin IDE**. It provides high-performance text manipulation, pseudo-terminal session hosting, language server protocol abstractions, and a safe C-ABI bridge.

---

## Crate Overview

```
crates/
├── myelin-text/       # High-performance rope buffer, coordinates, syntax tokenization, undo history
├── myelin-core/       # Workspace manager, multi-document lifecycle, directory tree scanner
├── myelin-terminal/   # Native ConPTY / PTY host, ANSI stream processor, background I/O
├── myelin-lsp/        # Language Server Protocol client infrastructure and JSON-RPC transport
└── myelin-ffi/        # C-ABI export layer compiling `myelin_ffi.dll`
```

---

### 1. `myelin-text`
- **Core Buffer**: Built on `crop::Rope` for $O(\log N)$ text inserts, deletes, and splits.
- **2D Point System**: `Point { row, col }` with byte-to-point and point-to-byte coordinate conversion.
- **Transactional History**: Undo/Redo stack supporting multi-step transactional edits and text reconstructions.
- **Syntax Lexer**: Fast single-pass tokenization for Rust, C#, JSON, and general code structures mapping tokens to SGR/hex styling spans.

### 2. `myelin-core`
- **Workspace**: Top-level coordination managing open documents keyed by a numeric `u64` document ID.
- **Document Management**: Handles disk synchronization, dirty states, version counters, cursor states, and undo/redo operations.
- **Directory Scanner**: Efficient recursive filesystem traversal providing hierarchical file/folder trees.

### 3. `myelin-terminal`
- **PTY Session**: Uses `portable-pty` for pseudo-terminal allocation (Windows ConPTY, Linux/macOS PTY).
- **Asynchronous Output Streaming**: Background worker thread continually drains process output into a thread-safe synchronized buffer.
- **ANSI Engine**: Strips and sanitizes ANSI escape sequences across split read chunks and provides raw streaming access.
- **Process Lifecycle**: Full support for process execution, input piping (`write_input`), process termination detection (`is_alive`), and terminal window resizing (`resize`).

### 4. `myelin-lsp`
- **JSON-RPC Transport**: Client protocol abstraction communicating with out-of-process language servers over standard I/O pipes.
- **LSP Protocol Types**: Structures for text synchronization (`textDocument/didOpen`, `textDocument/didChange`), completions, hover, and diagnostics.

### 5. `myelin-ffi`
- **C-ABI Export Boundary**: Exposes `#[no_mangle] pub unsafe extern "C"` functions consumed via P/Invoke.
- **Opaque Pointers**: Encapsulates `Workspace` and `TerminalSession` in opaque structs (`*mut WorkspaceHandle`, `*mut TerminalHandle`).
- **Memory Safety**: Clean string allocation via `CString::into_raw()` paired with explicit `myelin_free_string(ptr)` freeing routines.

---

## Building & Testing

### Build All Crates
```bash
cargo build --workspace
```

### Run Tests
```bash
cargo test --workspace
```

All 27+ unit tests verify rope operations, point conversions, UTF-8 multibyte deletions, backspace handling, undo/redo mechanics, and terminal ANSI streaming.
