# Writing tools: development changes

These changes are on the development branch and are not part of the published preview.3 executables.

## Toolbar and tool inspector

All seven tools remain on the writing palette. Tap a tool to switch immediately; tap the active tool again to open or close its slim customization sidebar. The settings button does the same. Starting a pen stroke or editing text dismisses tool settings. Fingers continue to navigate without producing ink.

The inspector shows the current tool's settings: saved pens, thickness, a color swatch grid with a selected-color indicator, custom hex colors, pressure/monoline, precision erasing, shapes, or text size. Color accepts `#RRGGBB` or `#AARRGGBB`. Pen and highlighter retain separate colors. The palette's color button opens the inspector at the color section.

Three saved pens are available directly on the palette in spacious layouts. Applying another saved pen brings it into that quick-access group. All saved pens remain available in the inspector. Narrow layouts keep a single row; swipe the palette or use the mouse wheel if its tools overflow. Existing dragging, docking, keyboard shortcuts, and saved placement remain supported.

Below 1280 DIPs, tool settings overlay the right side of the workspace, preserving the writing viewport's width. On wider windows they occupy a narrow adjacent column. The inspector closes as writing begins; paper settings still use their explicit Apply workflow.

## Lasso selection actions

Circle ink and/or typed objects with the lasso. Tap inside the selection with the pen or mouse, tap it with a finger after moving the pen out of range, or tap the small selection-actions button. Choose **Copy**, **Duplicate**, or **Delete**. **Paste** inserts a copied selection on the current page. Right-click and the keyboard shortcuts below are also available.

| Action | Shortcut in lasso mode |
| --- | --- |
| Copy selection | Ctrl+C |
| Duplicate selection | Ctrl+D |
| Delete selection | Delete / Backspace |
| Paste copied objects | Ctrl+V |

Duplicates and pasted objects receive new identities and become selected so they can be moved. Ink pressure/style and typed content are preserved. Copy also supplies standard ink data and plain typed text to the Windows clipboard. Copying leaves the selection intact; duplicate, delete, and paste each form one undoable edit and save through the normal ordered worker.

A finger tap opens the menu without panning. Finger dragging continues to pan; a pinch, lost/canceled touch capture, or palm contact with the pen in range does not open the menu. Pen dragging and selection handles retain native WPF movement/resizing.

## Scribble to erase

With **Pen** selected, a tight repeated back-and-forth scribble can erase covered handwriting or typed words. Detection runs after pen-up and requires several substantial reversals. A single strike-through, underline, small jitter, circular doodle, large diagram, highlighter stroke, or scribble on blank paper remains ink. Existing ink must be mostly covered and repeatedly crossed; long strokes extending outside the region are preserved. Typed-word removal preserves other words and line breaks.

The scribble itself disappears when it erases something. A short **Scribble erased · Undo** notice appears, and the normal back arrow/Ctrl+Z works. **One Undo restores both the erased content and the scribble.** This preserves an intentional mark if recognition was unwanted. A second Undo removes just that mark. Redo reapplies the erase.

Turn **Scribble to erase** off in pen settings to keep every scribble as ink. This preference is saved with the library. No notebook schema migration is needed.

## Verification

The Windows check harness covers custom colors and presets, inspector geometry, compact toolbar reachability, mixed ink/text duplication, deletion, clipboard paste across pages, object identities/pressure, touch menu arbitration, conservative scribble recognition, blank-paper/disabled behavior, typed-word removal, undo/redo, and restart persistence.

Physical Surface testing remains necessary for pen tap versus drag, palm suppression, gesture comfort, handwritten letters/formula doodles, display scaling, and long writing sessions. Include both handwriting and intended scribbles while tuning detection; false deletion must always remain recoverable with Undo.
