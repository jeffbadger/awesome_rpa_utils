# TextExtractUtils design (approved plan)

Status: approved 2026-09-26; phase 1 in progress (WP1). Tables (WP6) are phase 2.

## Context
Intake automations constantly pull a few business fields (invoice number, total, due date, IBAN, policy number) out of messy text: email bodies, OCR output (OcrUtils), terminal screens (TerminalUtils), PDF text. Pega Robot Studio's regex service can do it, but only if the designer writes and maintains a regular expression, tolerates OCR slips by hand, and converts the captured text to a number or date themselves. TextExtractUtils lets the designer describe a field the way a person sees it — **the label(s) in front of it, where the value sits, and what type it is** — and returns a validated, normalized value or a stable reason code. Regex remains only as an escape hatch.

## Shape
- New component `TextExtractUtils` in `src/textextractutils/`, namespace/assembly `TextExtractAutomation`, `net8.0-windows;net10.0-windows`, **no NuGet dependencies**. Scaffold copied from `src/reconciliationutils/` (csproj, `Component` base, ctor pair, own `NeverThrowsGuard.cs` reporting exception type only, `InternalsVisibleTo` tests, README pack item).
- One instance holds one **template** (the field definitions) and the results of the last extraction; thread-safe via a private lock; everything in memory.
- Suite conventions: never-throws (`bool` + trailing `out string message`), unique method names, `[Category]`/`[Description]` on every public method, scalar/enum/JSON ports only, messages never quote the input text, failure sentinels (null / 0 / false / -1).

## Template model
- **Label field**: name, labels (`Invoice No|Invoice Number|Invoice #`), position (`SameLine`, `NextLine`, `Below`), type, required flag, occurrence policy (`RequireUnique` default, `First`, `Last`).
- **Pattern field** (escape hatch): name, a regex with one named group `value`, type. Compiled `CultureInvariant` with a match timeout (100 ms) so a bad pattern cannot hang the robot.
- **Table** (WP6): column header texts on a header line, typed columns, rows until a blank line, end label or end of text.
- Template JSON (`schemaVersion` 1) loaded/validated whole, canonical `GetTemplateJson` that round-trips (apply the ReconciliationUtils lessons: duplicate-property detection, findings report with paths, canonical size within the load limit, standard encoder widened to all Unicode ranges).

## Matching rules (deterministic, explainable)
1. **Normalize** a working copy of the text: Unicode NFKC, ligatures, smart quotes/dashes to ASCII, every white-space or control character to one space (runs are kept, so columns survive; a space in a label matches any run of spaces, see decision 5), zero-width characters dropped, line endings unified. Values are reported from the original text (`raw`) and the normalized/converted form (`value`).
2. **Find labels** case-insensitively (ordinal, culture-independent) at word boundaries, followed by optional separators (`:` `#` `.` `-` `=` and spaces). OCR tolerance is limited and deterministic: only listed confusions (`l/I/1`, `O/0`, `rn/m`, `S/5`) inside the label, at most one per 6 characters, never in labels of 3 characters or fewer. No general fuzzy matching.
3. **Locate the value**: `SameLine` = after the label on that line, up to the next known label; `NextLine` = next non-blank line; `Below` = the cell under the label's columns on the next non-blank line (aligned forms and screens; cells are separated by two or more spaces; blank lines are skipped because OCR output often double-spaces a form, and the cell must still overlap the label's columns).
4. **Take the value by type**, which decides both what to capture and whether it is valid:
   - `Text` (rest of the span, trimmed), `Code` (one token: letters/digits/`-/._`), `Integer`, `Decimal`, `Amount` (currency symbol or ISO code, grouping, parentheses or trailing minus; explicit decimal style `DotDecimal`/`CommaDecimal`, never the machine culture; exact, never rounded), `Date` (explicit formats, validated at setup like ReconciliationUtils' `DateCore`, normalized to `yyyy-MM-dd`), `Email`, `Iban` (mod-97 checked), `Percentage`.
   - OCR digit repair (`O`→`0`, `l/I`→`1`) applies only inside numeric types.
5. **Outcome per field**: `found` + `value`, or a reason code: `MissingLabel`, `MissingValue`, `InvalidValue`, `AmbiguousValue` (label or value found more than once under `RequireUnique`), `PatternTimeout`. Required fields that fail are counted separately.

## Public surface (initial)
| Group | Methods |
|---|---|
| Template | `AddLabelFieldSimple(name, labels, type)` (SameLine, required, unique), `AddLabelField(name, labels, position, type, decimalStyle, dateFormats, required, occurrence)`, `AddPatternField(name, pattern, type, decimalStyle, dateFormats)`, `AddTableColumn(table, header, type, decimalStyle, dateFormats)` (phase 2), `ClearTemplate`, `LoadTemplateJson`, `GetTemplateJson`, `ValidateTemplateJson`, `ConfigureLimits` |
| Run | `ExtractFromText(text, out foundCount, out missingRequiredCount)` |
| Read | `GetField(name, out found, out value, out raw, out reason, out lineNumber)`, `GetResultJson`, `ResetFieldCursor` + `TryReadNextField(out hasItem, out name, out value, out raw, out reason, out lineNumber)` (one bool output: reason is null when found), `TryReadNextRow(table, out hasItem, out rowIndex, out rowJson)` + `GetRowValue(column, out found, out value, out raw, out reason)` (phase 2), `ClearResults` |
- Enums as drop-downs: `ValuePosition`, `FieldType`, `DecimalStyle`, `Occurrence`. `dateFormats` is text (`dd/MM/yyyy|yyyy-MM-dd`) used only by Date fields; `decimalStyle` is used only by Decimal, Amount and Percentage fields. Both are validated when the field is added (see decision 4).
- A setup change discards results (same lifecycle as ReconciliationUtils; document it in a "which calls discard results" section guarded by a test).

## Limits (defaults / maximums)
Text 1,000,000 / 10,000,000 characters; fields 200; labels per field 20; label length 128; pattern length 1,024 and timeout 100 ms; table rows 10,000; template JSON 256,000 characters (canonical form). Over a limit fails the call whole.

## Work packages (one PR each; stop for merge approval)
- **WP1** Scaffold, frozen public contract (stubs), template model, builders, template JSON load/validate/canonical, convention and boundary tests.
- **WP2** Normalization and label locator (positions, separators, OCR confusion table, stop-at-next-label), with golden tests on email/OCR/terminal samples.
- **WP3** Types and converters (copy `ExactDecimal` and the date-format checker from `src/reconciliationutils/`; components stay standalone), culture-independence tests.
- **WP4** `ExtractFromText`, results snapshot, readers, cursors, reasons, lifecycle.
- **WP5** Pattern fields with timeout and ReDoS tests.
- **WP6 (phase 2, after the first release)** Tables (fixed-width by header positions) and row cursor.
- **WP7** Docs (README, Documentation: QuickStart, Labels, Types, Tables, Patterns, Limits, a worked email-intake example with LocalQueueUtils), registration (`src/AwesomeRpaUtils.sln`, `$releaseAssemblies` in `scripts/Package-Release.ps1`, root README row/links/test line **and the DLL count words "twenty-four" → "twenty-five"** which ReconciliationUtils' sync test checks, `CrossReference.md`, `TESTING.md`), Pega usability review + index, documentation sync/example tests (CRLF-safe).
- **WP8** Measurement (large emails, 10,000-line reports), packaging dry runs, Component Browser discovery, release on your go-ahead.

## Verification
- `dotnet test src/textextractutils/TextExtractUtils.Tests` on Linux; CI runs it on Windows; Release build 0 warnings; solution builds.
- Golden-file tests from realistic samples (invoice email, OCR text with slips, TerminalUtils screen dump, fixed-width report); property tests that normalization never changes reported `raw`; no message contains input text (marker test); culture tests (tr-TR, ar-SA, de-DE); mutation checks on matching and type rules.
- Pending (recorded, not claimed): Robot Studio designer wiring (drop-downs, scalar ports, cursor loops) and a live run on OcrUtils output.

## Decisions (confirmed by Jeff)
1. **Tables are phase 2**: WP6 moves after the first release. Phase 1 ships WP1–WP5, WP7, WP8 without `AddTableColumn`, `TryReadNextRow` and `GetRowValue`; phase 2 adds them (additive, no phase 1 signature changes), with its own docs and release.
2. **OCR label tolerance as specified** (the small confusion table, at most one slip per 6 characters, none in labels of 3 characters or fewer).
3. **Name** `TextExtractUtils` / `TextExtractAutomation`.
4. **Decimal style is a drop-down** (`DecimalStyle`: `DotDecimal`, `CommaDecimal`), one per field, never guessed. `AddLabelField(name, labels, position, type, decimalStyle, dateFormats, required, occurrence)` and `AddPatternField(name, pattern, type, decimalStyle, dateFormats)` replace the single `format` string; in JSON the options are `decimalStyle` (numeric types only) and `dateFormats` (an array, Date only).

First step of WP1: commit this plan as `project-docs/plans/2026-09-26-textextractutils-design.md` in the WP1 PR.
5. **Spaces are kept, not collapsed** (WP2): collapsing would destroy the columns `Below` depends on, so every white-space character becomes one space and a space in a label matches any run of spaces instead. Every character still maps back to the original, so `raw` is always the text as it appeared.
6. **OCR slip budget** (WP2): labels of 3 or fewer letters and digits match exactly; longer labels allow one listed confusion per six letters and digits, **rounded up** (so a 4- to 6-letter label such as `Total` tolerates one slip, a 7- to 12-letter one two). Listed confusions: I/L/1 (and a `|` in the text), O/0, S/5, and RN read as M or M as RN.
