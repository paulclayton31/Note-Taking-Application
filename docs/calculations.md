# Calculate with Gemini

Calculate now sends a reviewed image or typed question to Gemini instead of relying on Windows handwriting recognition.

1. Choose **Calculate** (C).
2. Circle handwriting/objects, then choose **Solve selection**. For a printed PDF question, diagram, or image region, choose **Select area** and drag a rectangle with your pen or mouse. Press Esc to cancel area selection. You can also choose **Type question**.
3. Review the exact image that will be sent. Add a question such as “Find V3” or corrections to an ambiguous symbol.
4. Expand **Gemini API key**, choose **Get a key**, and create a key for your own project in Google AI Studio. Paste it into the masked key field. **Keep billing disabled in Google AI Studio to use the free tier.** The app cannot determine your project's billing tier. A billing-enabled project can incur charges, even for a free-tier-eligible model.
5. Optionally choose **Remember on this Windows account**. The key is encrypted using Windows user protection in `%LOCALAPPDATA%/SurfaceNotebook/Credentials/gemini-key.bin`, outside notebook data. **Forget saved key** deletes it. No shared developer credential is distributed with the app.
6. Choose **Solve with Gemini**. Opening a preview or selecting content never uploads it automatically.
7. Check **What I read**, **Answer**, and **Solution / clarification**. If Gemini cannot read a symbol or lacks necessary information, correct the question and solve again. Changing the question invalidates the old result.
8. Choose **Add to note** to save the reviewed solution. Your handwriting is preserved. Undo/redo, duplication, portable pages, and PDF export retain the cached answer. Tap an AI card with Calculate to review its saved image and explanation offline, or explicitly request another solution.

## Free-tier behavior and privacy

This prototype uses `gemini-3.5-flash-lite` through Google's Gemini Developer API. It sends the preview PNG and question directly from the desktop app to Google over HTTPS, using your key in an authentication header. The key is never stored in a page, portable export, PDF, or request URL.

Quota errors show a message; the app does not automatically retry or fall back to another model. Free availability and limits depend on Google, your project, and region. The app does not enable billing. It does not provide a shared backend or quota pool.

Google's free tier can use submitted content to improve its products. Only send notes you are comfortable submitting. Area previews include all visible content inside the rectangle, including nearby writing and PDF backgrounds. The exact image is shown before submission. No whole notebook or hidden page content is sent.

Responses are AI-generated and can misread handwriting or solve incorrectly. This first version does not execute model-generated code or independently verify every answer. It asks for plain-text, checkable equations; math typesetting and calculator-tool verification are future improvements.

## Limits and errors

Images render independently of the viewport zoom, at up to 2× page resolution with a 2048-pixel longest side. Requests accept up to 8 MB of PNG data and 12,000 question characters. Requests time out after 90 seconds; Stop and Close cancel pending work and prevent late results from being added. Saved source images share the page's 32 MB image budget.

Invalid keys, unavailable models, network errors, incomplete responses, and free-tier quota errors leave the original page unchanged. The legacy offline expression editor remains available when opening a calculation card created before this version.

## Validation

Windows regression checks use a simulated HTTP handler: no real keys, notebook uploads, or paid API requests occur in CI. Checks cover the endpoint/header/body, exact preview bytes, structured responses, clarification, quota and credential errors, cancellation, encrypted key storage, persistence, undo/redo, and export. Release screenshots contain a labeled example, not a live Gemini result. Real handwriting accuracy still needs validation with the user's own free-tier key.

References: [Gemini image input](https://ai.google.dev/gemini-api/docs/image-understanding), [API reference](https://ai.google.dev/api/generate-content), [pricing](https://ai.google.dev/gemini-api/docs/pricing).
