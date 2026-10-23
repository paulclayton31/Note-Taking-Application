# Pen prototype

## Scope

The first prototype is a single ruled page built with C#/.NET 10 and WPF. It has pressure-sensitive ink, four pen colors, a width slider, whole-stroke erasing (including inverted-tip erasing), a bounded undo/redo history, and manual save/open of editable `.stempage` files.

Pen is the default input. Mouse drawing is opt-in. Touch drawing is deliberately blocked; touch panning and zooming are future work. Use scrollbars or the mouse wheel to navigate the page for now. Hardware palm rejection and pen latency are acceptance checks, not guarantees from the framework.

## Run

Install the .NET 10 SDK for your architecture, then run from the repository root:

```powershell
dotnet build SurfaceNotebook.slnx --configuration Release
dotnet run --project src/SurfaceNotebook
```

Visual Studio with the .NET desktop development workload can also open `SurfaceNotebook.slnx`.

Run the executable verification harness (no third-party test packages):

```powershell
dotnet run --project tests/SurfaceNotebook.Checks --configuration Release
```

The harness checks pressure/style/identity persistence, replacement saves, rejection of invalid files, undo branching, and WPF window initialization with wired undo/redo controls. It does not simulate hardware pen input.

Build an unpackaged, self-contained application:

```powershell
dotnet publish src/SurfaceNotebook -c Release -r win-x64 --self-contained true -o artifacts/win-x64
# For an ARM Surface, replace win-x64 with win-arm64 and use a matching output directory.
```

MSIX signing, installation, and Store distribution are later work. GitHub Actions builds, runs checks, and produces a downloadable x64 artifact. Download and extract it, then run `SurfaceNotebook.exe`.

## Shortcuts

| Action | Shortcut |
| --- | --- |
| New page | Ctrl+N |
| Open page | Ctrl+O |
| Save as | Ctrl+S |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y |

Opening a different page, starting a new page, and closing the window prompt before discarding edits. Saving always opens a Save As dialog in this prototype. There is **no autosave or crash recovery yet**.

## Architecture

- `Models/PageDocument.cs`: versioned, UI-independent page-space stroke data, including pressure and stable stroke identities.
- `Services/InkMapper.cs`: WPF ink conversion at the UI boundary.
- `Services/PageStore.cs`: JSON loading/validation and same-directory temporary-file replacement on save.
- `Services/InkHistory.cs`: up to 30 snapshots for single-page undo/redo.
- `Controls/PenCanvas.cs`: pen-only policy with opt-in mouse ink.
- `ViewModels/PenViewModel.cs`: observable pen width and status.
- `MainWindow`: WPF canvas coordination, editing controls, file dialogs, and shortcuts.

The UI deliberately keeps input handling near the InkCanvas. Document and storage code remain separate. SQLite notebook indexing, incremental autosave, typed objects, and a richer command-based editing model will follow after the pen experience is validated. Whole-page history snapshots are unsuitable for large notebooks and will need a memory budget or edit commands.

## Surface acceptance session

Record your Surface model, CPU architecture, pen model, Windows version, display refresh rate, and app build. Use the actual device for these checks:

1. Write small letters, quick loops, diagonals, and slow curves; look for dropped input, hooks, jitter, or obvious trailing ink.
2. Vary pressure and line width. Verify color changes affect new strokes only.
3. Rest your palm on the page before and during writing. Touch alone must produce no ink. Verify a pen still writes after touching the page.
4. Erase with the toolbar and inverted pen. Undo/redo both erasing and clearing. Verify ink and eraser mode remain correct after flipping the pen.
5. Disable Mouse ink and verify clicks do not draw. Enable it and verify mouse drawing works.
6. Save a page, close, reopen, and compare strokes, pressure-derived appearance, colors, and widths.
7. Open a malformed file; the current page must remain intact with a readable error. Cancel Save As and verify edits remain marked unsaved.
8. Write for 20 minutes and monitor responsiveness, memory, and accidental touch input. Repeat with the keyboard detached, rotated display, and normal display scaling.

Capture failures before adding more features. The next milestone is dependable automatic saving and multi-page notebook navigation, followed by PDF annotation.
