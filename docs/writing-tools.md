# v0.3.1 writing tools

See the [release notes and downloads](releases/v0.3.1.md).

## Thin tool rail

All seven tools remain on the writing palette. Tap a tool to switch, then tap the active tool again to open or close its vertical rail. The settings button does the same. The rail is 64 DIPs wide, roughly the thickness of the main toolbar, and floats above the page without narrowing the writing viewport. Starting a pen stroke or editing text dismisses it.

Pens appear as colored buttons. Swipe the pen list to reach more; short windows replace the list with a button that opens all pens. The **+** button adds a pen. Drag the vertical thickness slider to change width. **Fountain** enables pressure-sensitive ink; **Ball** uses monoline. The ellipsis opens less-used options, including scribble erasing, precision erasing, shapes, text size, and toolbar docking.

The main palette still supports dragging, docking, keyboard movement, and saved position. It keeps one row in narrow windows; swipe horizontally or use the mouse wheel if tools overflow.

## Pens save their edits

Select a pen, then change its thickness, color, or style. The pen updates immediately and saves automatically. Switching to another pen and back restores the edited settings; closing/reopening retains them. There is no separate Save current pen action. Existing handwriting keeps its original color, width, and pressure.

**Add pen** opens a color wheel and lets you choose a name and Fountain/Ball style. Only the Add pen button commits it. Closing the picker before adding leaves existing pens unchanged. Up to 24 pens are supported. Long-press/right-click a rail pen to rename or remove it; the app keeps at least one pen available.

The color button recolors the selected pen automatically. The wheel chooses hue and saturation, with a brightness slider for darker/lighter ink. Optional hex input accepts `#RRGGBB` or `#AARRGGBB`. Pen and highlighter colors remain independent. The wheel supports pen, touch, mouse, arrow keys, and the hex field.

v0.3.0 could detach the current settings from the selected saved pen. When necessary, v0.3.1 preserves those current settings as a recovered pen alongside the existing pens. The library schema does not change.

## Lasso selection actions

Circle ink and/or typed objects with the lasso. Tap inside the selection with the pen or mouse, tap with a finger after moving the pen out of range, or tap the selection-actions button. Choose Copy, Duplicate, Delete, or Paste. Copies work across pages and receive independent identities. Duplicate, delete, and paste each form one undoable edit.

| Action | Shortcut in lasso mode |
| --- | --- |
| Copy | Ctrl+C |
| Duplicate | Ctrl+D |
| Delete | Delete / Backspace |
| Paste | Ctrl+V |

Finger dragging continues to pan. Pinching, canceled touch capture, and palm contact with the pen in range do not open selection actions. Pen dragging and handles retain native movement/resizing.

## Easier scribble erasing

With Pen selected, use a short scratch-out of about three back-and-forth passes over handwriting or typed words. v0.3.1 needs fewer reversals, accepts diagonal and flatter scratches, and is more forgiving when crossing the middle of handwritten strokes. Recognition considers the path in its own direction and still requires repeated sharp reversals and contact with existing content.

Single strike-throughs, underlines, tiny jitter, waves, circular doodles, highlighter marks, and scratches on blank paper remain ordinary ink in the regression fixtures. Long ink strokes mostly outside the scratched region are preserved. Typed-word removal preserves neighboring words and line breaks.

When a scratch erases content, its mark disappears and a short Undo notice appears. **One Undo restores the erased content and the scratch itself.** A second Undo removes just the scratch. Redo reapplies the erase. Disable Scribble to erase from the rail's ellipsis menu when desired.

## Verification

Windows checks cover independent pen edits across switches/restarts, legacy current-pen recovery, add/cancel behavior, wheel color/touch input, pressure style, vertical-slider commands, short-window rail reachability, mixed lasso operations, short/flat/diagonal scratches, undo/redo, and durable storage.

Physical Surface testing remains necessary for handwriting variety, pen/palm behavior, scratch-out comfort, and sustained lecture use. Undo remains available when a real mark is interpreted as erasing.
