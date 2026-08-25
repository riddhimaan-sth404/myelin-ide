# Myelin Desktop Client (`apps/desktop/`)

The `apps/desktop/` directory contains the cross-platform presentation layer for **Myelin IDE**, built with **C#**, **.NET 8**, and **Avalonia UI**.

---

## Projects Overview

```
apps/desktop/
├── Myelin.sln                          # Master Visual Studio solution
├── src/
│   ├── Myelin.Core/                    # Safe .NET wrapper library around native FFI
│   │   ├── NativeTerminal.cs           # IDisposable P/Invoke wrapper for ConPTY sessions
│   │   ├── NativeWorkspace.cs          # IDisposable P/Invoke wrapper for Rust workspace
│   │   ├── Models/                     # FileNode, StyledSpan models
│   │   └── Commands/                   # CommandRegistry with fuzzy search support
│   └── Myelin.UI/                      # Avalonia UI application
│       ├── App.axaml                   # Application theme, brushes, transitions
│       ├── ViewModels/                 # MVVM CommunityToolkit ViewModels
│       │   ├── MainWindowViewModel.cs  # Main workbench lifecycle, tabs, Cargo tasks
│       │   ├── BottomPanelViewModel.cs # Terminal, Output, Problems panel management
│       │   └── CommandPaletteViewModel.cs # Fuzzy action/file search overlay
│       ├── Views/
│       │   ├── MainWindow.axaml        # Splitter-based IDE layout
│       │   ├── EditorCanvas.cs         # Hardware-accelerated Skia text editor canvas
│       │   ├── TerminalCanvas.cs       # Direct-input interactive terminal canvas
│       │   └── CommandPaletteOverlay.axaml # Floating quick open / command palette
│       └── Assets/                     # Vector icons and Material Icon themes
└── tests/
    └── Myelin.Core.Tests/              # Interop & memory lifecycle test suite
```

---

## Key Components

### 1. `EditorCanvas.cs`
- Custom `Control` rendering text lines using Avalonia's `DrawingContext` and `FormattedText`.
- Supports pixel-perfect monospace alignment, syntax token highlights, line number gutters, cursor rendering, multi-line selection ranges, and clipboard interactions (`Ctrl+C`, `Ctrl+V`, `Ctrl+X`).

### 2. `TerminalCanvas.cs`
- Interactive terminal control connected directly to `NativeTerminal` (ConPTY).
- Full keyboard input capture (Backspace, Enter, Tab, Navigation Arrows, Ctrl sequences).
- VT100 / ConPTY ANSI escape sequence parsing, 16-color ANSI palette, automatic column wrapping, and scrollback buffering.

### 3. `MainWindowViewModel.cs`
- Manages open document tabs, dirty states, and workspace root folders.
- Coordinates Cargo tasks (`cargo build`, `cargo check`, `cargo test`, `cargo run`) with automatic dirty file auto-saving and live compiler diagnostic parsing.

### 4. `Myelin.Core.Tests`
- xUnit test suite validating UTF-8 FFI interop, string freeing, document memory management, and workspace synchronization with `myelin_ffi.dll`.

---

## Building & Running

### Build Solution
```bash
dotnet build apps/desktop/Myelin.sln
```

### Run Application
```bash
dotnet run --project apps/desktop/src/Myelin.UI/Myelin.UI.csproj
```

### Run Unit Tests
```bash
dotnet test apps/desktop/tests/Myelin.Core.Tests/Myelin.Core.Tests.csproj
```
