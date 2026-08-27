# OcrUtils Documentation

Real-world usage examples for every method on the `OcrUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Plain Text](PlainText.md) — region/image to a single recognized string
- [Structured Results](StructuredResults.md) — positioned lines/words, and finding text on screen
- [Language](Language.md) — checking installed OCR language packs
- [Wait for Text](WaitForText.md) — polling a region until expected text appears

All examples assume an `OcrUtils` instance named `ocr`, as it would appear
dropped onto a Pega Robot Studio automation's design surface (or instantiated
directly: `var ocr = new OcrUtils();`).
