# AGENTS.md — Myelin IDE Architecture & Contributor Guide

Welcome to the **Myelin IDE** project. This document is a comprehensive technical reference designed for AI agents, core contributors, and developers working on or extending this codebase.

---

## 1. Project Vision & Philosophy

**Myelin** is a native, high-performance, polyglot open-source Integrated Development Environment (IDE) built with a decoupled two-tier architecture:
- **Systems Core (Rust)**: High-throughput, memory-safe engine handling text rope buffers, transactional history (undo/redo), language server protocol (LSP) clients, and native pseudo-terminal (ConPTY) execution.
- **Presentation Layer (C# / Avalonia UI)**: Cross-platform, hardware-accelerated Skia rendering engine providing a modern, natural, and fluid developer experience inspired by VS Code and Zed.

### Key Architectural Pillars:
1. **Zero-Latency Text & Terminal Rendering**: The text and terminal surfaces are custom Skia hardware-accelerated controls rather than heavy DOM-like web views.
2. **Safe C-ABI FFI Interoperability**: Communication between C# and Rust uses a minimal overhead C-ABI dynamic library (`myelin_ffi.dll`) generated via `csbindgen`.
3. **True Native Terminal**: Built directly on Windows ConPTY / Unix PTY with direct keystroke streaming, avoiding separate input boxes.
4. **Vector-Only Aesthetics**: Zero bitmap/emoji dependencies; all iconography uses scalable SVG `StreamGeometry` vector paths.

---

## 2. Repository Layout & Component Map

```text
myelin/
├── run.bat                                # Root launcher script
├── Myelin IDE/                            # Primary solution workspace
│   ├── Cargo.toml                         # Cargo workspace configuration
│   ├── run.bat                            # Local build & launch script
│   ├── bindings/
│   │   └── NativeMethods.g.cs             # Auto-generated csbindgen P/Invoke definitions
│   │
│   ├── crates/                            # Rust Systems Core
│   │   ├── myelin-text/                   # Crop rope text buffer, 2D points, syntax lexer, history
│   │   ├── myelin-core/                   # Workspace, document manager, directory scanner
│   │   ├── myelin-terminal/               # ConPTY / PTY session management, async background I/O
│   │   ├── myelin-lsp/                    # Out-of-process JSON-RPC LSP client infrastructure
│   │   └── myelin-ffi/                    # C-ABI export layer producing myelin_ffi.dll
│   │
│   └── apps/desktop/                      # C# / .NET 8 Desktop Client
│       ├── Myelin.sln                     # Visual Studio / dotnet solution
│       ├── src/
│       │   ├── Myelin.Core/               # Safe C# wrapper around myelin_ffi.dll
│       │   │   ├── Native/                # Generated P/Invoke bindings
│       │   │   ├── NativeWorkspace.cs     # Safe WorkspaceHandle wrapper
│       │   │   ├── NativeTerminal.cs      # Safe TerminalHandle wrapper (PTY)
│       │   │   ├── Models/                # FileNode, StyledSpan data models
│       │   │   └── Commands/              # CommandRegistry and fuzzy search
│       │   └── Myelin.UI/                 # Avalonia MVVM Desktop Application
│       │       ├── App.axaml              # Theme, brush transitions, typography styles
│       │       ├── Styles/Icons.axaml     # SVG vector geometry resource dictionary
│       │       ├── ViewModels/            # MainWindowViewModel, BottomPanelViewModel, etc.
│       │       └── Views/
│       │           ├── MainWindow.axaml   # Main workbench layout (Activity Bar, Sidebar, Tabs, Panel)
│       │           ├── EditorCanvas.cs    # Skia-rendered text editor canvas
│       │           ├── TerminalCanvas.cs  # Skia-rendered interactive direct-input terminal canvas
│       │           └── CommandPaletteOverlay.axaml # Floating Command Palette / Quick Open modal
│       └── tests/
│           └── Myelin.Core.Tests/         # xUnit interop & memory management tests
```

---

## 3. Subsystem Architecture

### 3.1 Text Engine (`crates/myelin-text`)
- Backed by `crop::Rope`, providing $O(\log N)$ inserts, deletes, and slice operations.
- **Coordinate System**:
  - `Point`: 0-indexed `(line, column)` 2D coordinate.
  - `point_to_byte` / `byte_to_point`: Strict newline (`\r`, `\n`) boundary mapping ensuring characters and spaces map accurately to byte offsets.
- **Transactional History**:
  - `History` tracks reversible `Transaction` objects (insertions, deletions) supporting full undo/redo stacks.
- **Syntax Tokenizer**:
  - `SimpleLexer`: Fast single-pass scanner extracting keyword, type, function, string, number, and comment spans with VS Code Dark+ hex colors (`#C586C0`, `#4EC9B0`, `#DCDCAA`, `#CE9178`, `#B5CEA8`, `#6A9955`).

### 3.2 Native Terminal Subsystem (`crates/myelin-terminal`)
- Uses `portable-pty` to allocate native pseudo-terminals (Windows ConPTY).
- Background worker thread reads stdout/stderr into a thread-safe ring buffer (`Arc<Mutex<Vec<u8>>>`).
- Strips/sanitizes ANSI escape sequences and exposes direct stdin write access.
- `TerminalCanvas.cs` renders the terminal screen grid directly and pipes keystrokes (`Enter`, `Backspace`, `Tab`, ANSI navigation arrows, `Ctrl+C`, `Ctrl+D`, `Ctrl+L`) to the PTY stdin.

### 3.3 Interop Layer (`crates/myelin-ffi` & `Myelin.Core`)
- Exposes pure C functions marked `#[no_mangle] pub unsafe extern "C"`.
- Opaque pointers (`*mut WorkspaceHandle`, `*mut TerminalHandle`) are owned by safe C# `IDisposable` wrappers.
- String allocations returned across the FFI boundary use `CString::into_raw()` and are freed via `myelin_free_string(ptr)` using `Marshal.PtrToStringUTF8`.

### 3.4 Presentation Layer (`Myelin.UI`)
- **Workbench Layout**: 48px Activity Bar, collapsible sidebar (`Ctrl+B`), tab strip with active accent indicators and dirty dots, dockable bottom panel (`Ctrl+J` / `Ctrl+~`), status bar (`#007ACC`), and floating command palette (`Ctrl+Shift+P` / `Ctrl+P`).
- **Smooth GUI Transitions**: Avalonia `BrushTransition` and `DoubleTransition` applied to buttons, tree views, list items, and splitters.
- **Precise Monospace Rendering**: Monospace character width is dynamically measured (`MeasureCharWidth()`) to ensure text, spaces, and blinking pill cursors align pixel-perfectly.

---

## 4. Development & Build Instructions

### Prerequisites
- **.NET 8 SDK** (accessible in `PATH` or `%LOCALAPPDATA%\Microsoft\dotnet`)
- **Rust Toolchain (1.75+)** (accessible in `PATH` or `%USERPROFILE%\.cargo\bin`)

### Launching the IDE
To compile and launch the IDE on Windows:
```cmd
.\run.bat
```
*(Alternatively, execute `run.bat` inside `Myelin IDE\`)*.

### Running Automated Tests
```powershell
# Run all Rust unit tests
cd "Myelin IDE"
cargo test --workspace

# Run all .NET integration & interop tests
dotnet test "apps/desktop/Myelin.sln"
```

### Regenerating FFI Bindings
When adding or modifying C-ABI functions in `crates/myelin-ffi/src/lib.rs`:
```powershell
cargo build -p myelin-ffi
# csbindgen automatically outputs updated bindings to bindings/NativeMethods.g.cs
# Copy bindings to apps/desktop/src/Myelin.Core/Native/NativeMethods.g.cs
```

---

## 5. Critical Guidelines for AI Agents

When working on this repository, agents MUST adhere to the following rules:

1. **FFI Lifetime Safety**:
   - Always free strings returned from Rust via `myelin_free_string(ptr)`.
   - Never de-reference raw pointer handles on the C# side after `Dispose()` has been called.
2. **Text Coordinate Accuracy**:
   - Do not assume 1 character = 1 byte (support multi-byte UTF-8).
   - Ensure newline boundaries (`\r`, `\n`) are preserved when performing line/column calculations.
3. **Icon & UI Consistency**:
   - Never introduce emoji characters into the UI. All new icons MUST be defined as vector `StreamGeometry` paths in [`Styles/Icons.axaml`](file:///c:/Users/amin/Projects/agent/myelin/Myelin%20IDE/apps/desktop/src/Myelin.UI/Styles/Icons.axaml).
   - Maintain rounded corners (`CornerRadius="4"` - `"8"`) on all new controls to preserve the modern, natural aesthetic.
4. **Terminal Non-Blocking Execution**:
   - Terminal PTY I/O must never block the Avalonia UI dispatcher thread. Keep polling/streaming decoupled on background tasks or lightweight timer ticks.
