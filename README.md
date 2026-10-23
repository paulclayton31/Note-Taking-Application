# Surface-First Intelligent STEM Notebook

A Windows-native, handwriting-first notebook and intelligent workspace for STEM students, designed primarily for Microsoft Surface Pro devices.

Inspired by the natural handwriting and document organization experience of Goodnotes, this project brings lecture notes, annotated course materials, mathematical tools, engineering diagrams, programming, and context-aware AI into one workspace.

## Project status

The repository now contains an early **C# / .NET 10 / WPF pen prototype**. It supports a single ruled page, pressure-sensitive ink, pen colors and width, whole-stroke erasing, undo/redo, and manual save/open of editable pages. Surface hardware validation is still required.

The capabilities below describe the broader intended product. Autosave, multi-page notebooks, PDF annotation, STEM objects, and AI are not implemented yet.

To start with the .NET 10 SDK installed:

```powershell
dotnet run --project src/SurfaceNotebook
```

See [prototype setup, architecture, and Surface test checklist](docs/pen-prototype.md).

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

GitHub Actions builds and checks the application on Windows and publishes a self-contained x64 prototype artifact. The intended stack is C#/.NET 10, WPF, and SQLite for the later notebook persistence layer. The prototype currently uses versioned JSON page files to validate editable ink round-tripping before adding the notebook database.
