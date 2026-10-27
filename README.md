# Surface-First Intelligent STEM Notebook

A Windows-native, handwriting-first notebook and intelligent workspace for STEM students, designed primarily for Microsoft Surface Pro devices.

Inspired by the natural handwriting and document organization experience of Goodnotes, this project brings lecture notes, annotated course materials, mathematical tools, engineering diagrams, programming, and context-aware AI into one workspace.

## Download and run

**[Surface Notebook v0.3.5](https://github.com/paulclayton31/Note-Taking-Application/releases/tag/v0.3.5)** is the stable release: a Home library with folders, a compact writing toolbar, thin vertical tool rail, pens that save edits automatically, color-wheel selection, lasso actions, easier undoable scribble erasing, notebook copy/paste and deletion, fully closable tabs, smoother page navigation, PDF import/export, easier rounded/vertical scratch-outs, draw-and-hold shapes, and Surface Pen side-button lasso. [Workspace guide](docs/v0.3-workspace.md) · [Writing tools and shortcuts](docs/writing-tools.md) · [PDF import/export guide](docs/pdf-workflows.md).

![Your notebook library](https://github.com/paulclayton31/Note-Taking-Application/releases/download/v0.3.5/home-light.png)

[See the writing workspace](https://github.com/paulclayton31/Note-Taking-Application/releases/download/v0.3.5/workspace-light.png) · [Tool customization sidebar](https://github.com/paulclayton31/Note-Taking-Application/releases/download/v0.3.5/tool-inspector.png)

**[Download the latest Windows app](https://github.com/paulclayton31/Note-Taking-Application/releases/latest)** from the repository's **Releases** section.

- [Windows x64 executable](https://github.com/paulclayton31/Note-Taking-Application/releases/latest/download/SurfaceNotebook-windows-x64.exe) for Intel/AMD devices.
- [Windows ARM64 executable](https://github.com/paulclayton31/Note-Taking-Application/releases/latest/download/SurfaceNotebook-windows-arm64.exe) for Snapdragon/ARM devices.

Download the matching `.exe` and double-click it. No ZIP extraction or separate .NET installation is needed. Windows may show the filename without `.exe`; its Type is **Application**. Check Windows **Settings > System > About > System type** if you are unsure which download to use.

The automatically generated **Source code** downloads contain the project files. Use one of the named `.exe` assets to run the app.

## Project status

**v0.3.5** refines the paper-first design of the **C# / .NET 10 / WPF notebook app**. The workspace retains v0.2's multiple notebooks/pages, ordered background autosave, committed-note recovery, and strictly pen-only writing. Fingers pan and zoom in every tool. The default pressure/curve settings remain intact.

The app supports pen/highlighter, stroke/precision erasing, native lasso transforms and copy/duplicate/delete/paste, geometric ink shapes, typed text, a draggable/dockable palette, a thin floating tool rail, automatic pen-edit saving, a color wheel, Fountain/Ball buttons, easier undoable scribble erasing, paper customization, notebook tabs, nested folders, recents, stars, search, saved pens, touch zoom, page duplication/reordering and deletion, independent notebook copy/paste, fully closable tabs, smooth page navigation, PDF notebooks and export, vertical scratch-outs, draw-and-hold shape snapping, Surface Pen side-button selection, and focus mode. Existing older libraries are upgraded with a consistent SQLite backup. v0.3.5 keeps schema 4 from v0.3.3; older libraries are upgraded after creating a consistent `library-v032-backup.db` backup. Older app versions cannot open schema 4. See the [release and upgrade notes](docs/releases/v0.3.5.md).

The capabilities below describe the broader intended product. Advanced PDF editing, STEM objects, AI, and cloud sync are future work. Physical pen/palm/touch validation is still required on each Surface model.

To start with the .NET 10 SDK installed:

```powershell
dotnet run --project src/SurfaceNotebook
```

See the [v0.3 workspace guide](docs/v0.3-workspace.md), [writing tools guide](docs/writing-tools.md), and [v0.2 storage background](docs/v0.2-notebooks.md). Use the library's **…** menu to import older `.stempage` files or export the current page. Notebook edits save automatically; wait for **All changes saved** before a forced shutdown.

## Vision

The application should feel like a digital notebook when users need simplicity and a powerful engineering workspace when they need advanced functionality.

Its goal is to help students take lecture notes, annotate textbooks and assignments, work through technical problems, and organize educational materials without constantly switching applications.

## Core experience

### Tablet mode

A clean, responsive canvas for writing with the Surface Pen, drawing diagrams, and annotating documents. Handwriting is the primary interaction, with controls designed to preserve space for the page.

### Laptop mode

An expanded workspace for viewing multiple documents, editing with a keyboard, using interactive STEM tools, and learning with AI assistance.

## Planned capabilities

| Area | Intended experience |
| --- | --- |
| Natural handwriting | Responsive, pressure-sensitive digital ink suitable for prolonged note-taking. |
| Document workspace | Multiple documents and customizable layouts, with handwritten and typed content working together. |
| Mathematics | Editable mathematical expressions and interactive graphs integrated into notebook pages. |
| Engineering | Diagrams and technical visualizations alongside written explanations and calculations. |
| Programming | Programming capabilities embedded in the learning and problem-solving workflow. |
| Context-aware AI | Explanations, hints, and feedback grounded in selected handwritten notes, expressions, or diagrams. |
| Extensible pages | Editable objects that combine ink, text, mathematics, visualizations, and imported documents. |
| Reliable ownership | Offline-first note-taking, automatic saving, and portable document exports. |

## Product principles

- **Handwriting comes first.** Everyday writing and page navigation should remain simple and responsive.
- **Tools support the page.** Advanced capabilities should be available when needed without overwhelming basic note-taking.
- **Work adapts to the device.** Pen, touch, keyboard, and mouse interactions should suit the Surface Pro's tablet and laptop configurations.
- **Students retain their work.** Core note-taking should work offline, save automatically, and support portable exports.
- **AI supports understanding.** Assistance should use the student's selected context to help explain ideas and guide problem-solving.

## Proposed development sequence

This sequence is a starting point for planning and may change as the interaction model is validated.

1. **Notebook foundation:** Validate the pen experience, page navigation, local persistence, and automatic saving.
2. **Study workflows:** Add notebook organization, document import and annotation, and export.
3. **STEM objects:** Introduce editable mathematics, graphs, diagrams, and programming capabilities.
4. **Intelligent assistance:** Add selection-based explanations, hints, and feedback.
5. **Expanded workspace:** Refine multiple-document layouts and transitions between tablet and laptop use.

## Development

Build the solution and run the verification harness:

```powershell
dotnet build SurfaceNotebook.slnx --configuration Release
dotnet run --project tests/SurfaceNotebook.Checks --configuration Release --no-build
```

GitHub Actions builds and checks the application on Windows. The release pipeline publishes self-contained single-file x64 and ARM64 executables after the automated checks and x64 bundled startup check pass. The stack is C#/.NET 10, WPF, and SQLite (`Microsoft.Data.Sqlite`). Versioned `.stempage` JSON files remain supported for portable page import/export.
