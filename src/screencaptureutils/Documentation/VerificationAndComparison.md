# Verification & Comparison

Confirming the screen actually changed, and telling "still working" apart
from "already done" — visual checks in the same spirit as MouseUtils'
[Verification & Synchronization](../../mouseutils/Documentation/VerificationAndSynchronization.md)
methods, but over a whole region's appearance instead of one pixel. All
coordinates here are **absolute screen pixels**.

None of these throw. `GetRegionHash`/`WaitForRegionToChangeSimple`/
`CompareRegionToBaselineSimple` overload the meaning of `false` — it covers
both a normal negative outcome (`message == null`) and a real failure
(`message` set); each has a disambiguated overload
(`WaitForRegionToChange`'s `timedOut`, `CompareRegionToBaseline`'s
`comparisonCompleted`) for designers who'd rather branch on a Boolean.

## `GetRegionHash(int left, int top, int width, int height)`

**Scenario:** Log a compact fingerprint of a status panel at each step of a
long automation, so a later diff of the log can show roughly when the panel's
appearance changed, without keeping full-resolution screenshots around.

```csharp
screenCapture.GetRegionHash(800, 40, 200, 30, out string hash, out _);
Logger.Info($"Status panel hash: {hash}");
```

**Keep the region tight.** The hash downsamples the whole region to an 8x8
grid before hashing, so a small text change inside a large region (e.g. a
single line changing within a full panel) can leave the averaged result
unchanged. Crop to just the text/indicator that's expected to change, not
the whole surrounding panel.

## `WaitForRegionToChangeSimple(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs)`

**Scenario:** After submitting a form, a "Processing..." label is expected to
change once the backend responds — instead of a fixed `Thread.Sleep`, poll
just that label's region until its appearance changes.

```csharp
submitButton.Click();
bool changed = screenCapture.WaitForRegionToChangeSimple(
    400, 300, 150, 20, 30000, 250, out string message);

if (!changed)
{
    if (message != null)
        throw new InvalidOperationException("Poll aborted: " + message);
    throw new TimeoutException("Status label did not change within 30 seconds.");
}
```

## `WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out bool timedOut)`

**Scenario:** Same as above, but the automation wants to branch on timeout
vs. a real failure without testing `message` for `null`.

```csharp
bool changed = screenCapture.WaitForRegionToChange(
    400, 300, 150, 20, 30000, 250, out bool timedOut, out string message);

if (!changed)
{
    if (timedOut)
        Logger.Warn("Status label never changed — backend may be stuck.");
    else
        Logger.Error("Poll aborted: " + message);
}
```

## `CompareRegionToBaselineSimple(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent)`

**Scenario:** A visual-regression check — does a report's header still
render the way it did when the automation was last verified by a human, or
did an app update subtly break the layout/font/colors?

```csharp
bool matches = screenCapture.CompareRegionToBaselineSimple(
    0, 0, 800, 60, @"C:\baselines\report_header.png",
    2.0, out double actualDifferencePercent, out string message);

if (!matches)
{
    if (message != null)
        Logger.Error("Comparison could not run: " + message);
    else
        Logger.Warn($"Header differs from baseline by {actualDifferencePercent:F1}% (tolerance 2.0%).");
}
```

Capture the baseline once, from a known-good run, with the exact same
region coordinates the comparison will later use:

```csharp
screenCapture.CaptureRegionToFile(0, 0, 800, 60, @"C:\baselines\report_header.png", out _);
```

## `CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out bool comparisonCompleted)`

**Scenario:** Same visual-regression check, but the automation wants to
distinguish "ran and found it out of tolerance" from "couldn't run at all"
(e.g. a missing baseline file after a fresh deployment) without testing
`message` for `null`.

```csharp
bool matches = screenCapture.CompareRegionToBaseline(
    0, 0, 800, 60, @"C:\baselines\report_header.png", 2.0,
    out double actualDifferencePercent, out bool comparisonCompleted, out string message);

if (!comparisonCompleted)
    throw new InvalidOperationException("Baseline comparison could not run: " + message);
if (!matches)
    Logger.Warn($"Header differs from baseline by {actualDifferencePercent:F1}%.");
```

A baseline image whose dimensions don't exactly match `width`/`height`
fails with `comparisonCompleted == false` rather than stretching/cropping to
fit — recapture the baseline if the target region's size has legitimately
changed.
