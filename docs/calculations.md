# Handwritten calculations (v0.4 preview)

Choose **Calculate** on the writing toolbar, or press **C**. Circle handwritten variables and expressions, then choose **Review selection**. Calculate has its own workflow; the ordinary Lasso tool and Surface Pen barrel shortcut keep their selection actions.

Windows reads the selected handwriting locally. Review and correct the text before adding a result: handwriting recognition can confuse names, digits, and mathematical operators. No recognition runs while you write. If Windows' handwriting component is unavailable, install **Handwriting** in your language's Windows Settings, or use **Type calculation**. The calculator itself works without handwriting recognition or a network connection.

For a first example, write these on separate lines:

```text
F = m * a
m = 2
a = 3
```

Review the interpretation. The result preview shows **F → 6**. Choose **Add to page** to insert a calculation card. Your handwritten strokes remain untouched. With Calculate selected, tap the card to reopen it, change `m` to `4`, and choose **Update calculation**: the result becomes **12**. Draft edits and recognition do not change the notebook until you choose Add or Update. Cancel discards the draft.

## Supported math

- Variable assignments and numerical expressions, one per line or separated by semicolons. Formulas can appear above their inputs. Each card has its own variables.
- `+`, `-`, `*`, `/`, right-associative `^`, parentheses, unary signs, and postfix percent (`50% * 80` gives `40`). `-2^2` gives `-4`; use `(-2)^2` for `4`.
- Decimal points and scientific notation (`.5`, `1e-3`). Use dots for decimals; commas separate function arguments.
- Constants `pi` / `π` and `e`.
- `sqrt`, `abs`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `ln`, `log` / `log10`, `exp`, `floor`, `ceil`, `round`, and two-argument `min` / `max`. Trigonometry uses radians; `round` rounds midpoint values away from zero.
- Common handwriting symbols `×`, `÷`, `−`, `²`, and `³`. Numeric coefficients such as `2x` work, but use `m * a` between named variables: `ma` is a single name.
- Names are case-sensitive (`m` and `M` differ). Duplicate definitions, missing inputs, circular dependencies, division by zero, invalid real-number domains, and non-finite results show an error and prevent insertion.

Results use numerical floating-point arithmetic and display up to 12 significant digits. This preview does not solve symbolic equations or support units, stacked fractions, matrices, graphs, or automatic links to later handwriting edits. Enter fractions with `/`; use plain numeric values for variables. Correct the reviewed source to update a card.

## Saving and recovery

Adding or updating a card is one undoable edit. Ordinary lasso actions move, copy, resize, duplicate, and delete cards. Calculation source and results survive restarting, page/notebook duplication, portable-page export/import, and copying. PDF export includes the visible calculation and result; PDF backgrounds remain fixed.

The Text tool does not overwrite a calculation's generated result, and word-level scribble erasing skips calculation cards. Use Calculate to edit a card or Lasso / its context menu to delete it.

Schema **4** and portable-page version **3** are unchanged. Calculations add optional metadata to text objects. Older versions can display the saved result text but may discard the editable formula metadata when resaving it. Keep an existing library backup when trying a preview and use the preview to edit calculation cards.

## Preview validation

The portable calculation checks run on Linux and Windows. The Windows harness exercises calculation review, insertion/edit/Undo/Redo, source-ink preservation, copy isolation, durable metadata, PDF export, touch filtering, and barrel-tool restoration. It also reports whether Windows handwriting recognition is available; availability on a CI image does not establish recognition quality on a Surface. Physical testing is still needed for handwriting variety, language packs, palm behavior, and ARM64 execution.
