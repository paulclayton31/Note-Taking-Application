# PDF notebooks

Use **Import PDF** in the library header, or **Import PDF as notebook** in the workspace's library menu. Each source page becomes a page in a new notebook in the current folder. Page order and physical dimensions are preserved, including landscape pages. Write, highlight, add typed notes, and use the lasso above the fixed PDF background. The source PDF is not modified. Printed PDF content is a background, so the ink eraser and scribble erase affect your annotations, not printed content.

Use **Export page as PDF** in page actions, or **Export notebook as PDF** for all pages in order. Paper backgrounds, handwriting pressure/shape, highlighter transparency, and typed notes are included. Paper and annotations export as vector graphics; newly typed notes export as glyph outlines. Ordinary imported PDF pages retain their source vectors and selectable source text. Rotated/cropped pages and pages with PDF annotations use the cached page image on export for reliable visual placement; their printed text is not selectable in that output.

Import and export show progress and allow cancellation. Import prepares the entire PDF before creating the notebook in one transaction. Export writes and verifies a temporary PDF before replacing the selected destination. Invalid or canceled imports do not create partial notebooks; canceled exports preserve an existing destination file.

PDF pages have fixed paper dimensions. The paper-settings button is disabled on those pages; notebook-wide paper changes apply only to ordinary pages. PDF backgrounds survive restart, page/notebook duplication, and `.stempage` import/export. The original external PDF is not needed after import. Deleting a PDF page or notebook follows the same confirmed, permanent deletion behavior as other notes.

## Scope and compatibility

- Imports accept unlocked PDFs up to 100 MB and 200 pages. Pages must fit the app's 3–36-inch paper range. Prepared notebook data is limited to 300 MB; split larger documents before importing.
- Preview images render at up to 192 DPI, capped at 3072 pixels on the longer edge. Import stores one self-contained source PDF page and its preview per page.
- Password-protected PDFs need an unlocked copy first. Form and signature fields must be printed or flattened to a regular PDF before import, to avoid losing visible field values. Interactive forms, signature validation, attachments, and bookmarks are not notebook features.
- PDF workflows run locally on supported Windows versions. Rendering is local, using a bundled PDFium renderer; Adobe Reader and cloud services are not required.
- This change upgrades the library to schema 4, with a consistent `library-v032-backup.db` backup before the migration. Older app versions cannot open schema 4. Portable pages now use version 3; versions 1 and 2 remain readable.

## Vertical scratch-outs

Up-and-down scratch-outs accept rounded turns, a shorter finishing pass, and narrow contact with connected handwriting. Target detection counts the scratch passes instead of requiring a percentage of the whole stroke or repeated exits from its bounding box. Horizontal and diagonal gestures remain supported. Long diagram lines crossing the scratch region are protected. One Undo restores the erased handwriting and scratch; a second Undo removes the scratch. Scribble erasing can still be disabled in tool options.
