# BrowserInterruptUtils — Detecting and Dismissing In-Browser Popups

**Status:** Implemented (all six implementation tasks done, repository integration complete). **Pending live verification on a real Windows host with a real
browser** — see [Verification status](#verification-status) and the pending checks in [TESTING.md](../../TESTING.md#browserinterruptutils-needs-a-desktop-and-a-real-browser-live-checks-pending).
The Pega usability review is deferred and still pending (see [Deferred](#deferred)).

**Component:** Standalone `BrowserInterruptUtils` for Pega Robot Studio, in namespace/assembly `BrowserInterruptAutomation` (folder `src/browserinterruptutils`).
Documentation: [README](../../src/browserinterruptutils/README.md) and [Documentation/](../../src/browserinterruptutils/Documentation/README.md).

## Context

`InterruptUtils` (`src/interruptutils`) watches for native popups on a background thread and dismisses them by rule while an automation is busy. It works entirely against Win32 HWNDs (`SetWinEventHook`, `EnumWindows`, `BM_CLICK`) and its own docs and code explicitly acknowledge it cannot see or act on anything rendered *inside* a browser page. In practice this leaves two real gaps during unattended browser automation:

1. Native JS dialogs (`alert`/`confirm`/`prompt`/`beforeunload`) — Chromium renders these as owner-drawn top-level windows with no native `Button`/`Static` child controls, so even though `InterruptUtils` could theoretically notice the window exists, it can't read the message text or click a button (`DialogUtils`' own README documents this exact limitation and tells users to fall back to `KeyboardUtils`/`MouseUtils`, which is fragile — no title/message-based targeting, just blind key/click sequences).
2. In-page custom modal overlays (styled `<div>` dialogs, cookie-consent banners, permission-prompt bars) — these have no HWND at all and are invisible to any Win32-based tool by construction.

A repo-wide search confirmed this is genuinely greenfield: there is no Selenium/Playwright/WebDriver/CDP/Puppeteer/CefSharp plumbing or NuGet reference anywhere in the codebase. The component closes both gaps using **Windows UI Automation (UIA)** — the same substrate `UIAutomationUtils` already uses and already documents as reaching "browser-hosted UI" — rather than Chrome DevTools Protocol, because UIA requires no special browser launch flags (CDP needs `--remote-debugging-port`, often disabled in attended/production environments), works across any UIA-exposing browser (Chrome, Edge, Firefox), and keeps the component dependency-free like the rest of the suite.

Decisions confirmed with the user:
- **Substrate: UIA**, not CDP.
- **Scope: both** native JS dialogs and in-page ARIA-role overlays (not native-dialogs-only).
- **No exerciser or test-harness entry for v1** — matches how `InterruptUtils` itself shipped; defer manual verification to a live Robot Studio/Robot Runtime check.

## 1. Naming

Keep `BrowserInterruptUtils` / `BrowserInterruptAutomation` / `src/browserinterruptutils`, following the repo's standard `<Name>Utils` → `<Name>Automation` convention. `UIAutomationUtils`'s `UIAutomation` assembly name is a documented one-off exception for a namespace-collision reason that doesn't apply here. The class's `<summary>` opens by scoping itself explicitly (native JS dialogs + ARIA-role overlays via UIA) so "Interrupt" doesn't imply parity with every conceivable browser popup (e.g. headless sessions, which are out of scope — see §9).

## 2. One rule model, two scopes

Detection cost and mechanism differ fundamentally between the two cases (cheap top-level-window scan vs. expensive bounded page-subtree walk), and dismissal mechanics differ too (`WindowPattern.Close` only exists for a native dialog). So: one shared internal `BrowserPopupRule` model and one shared `BrowserPopupEngine` (matching/retry/runaway-breaker/log machinery, mirroring `PopupEngine`), but the **public API exposes scope through distinct method names** — `AddNativeDialogDismissRuleByName` vs. `AddPageOverlayDismissRuleByName`, etc. — the same way `InterruptUtils` already uses distinct methods instead of one method with an action enum. This keeps invalid combinations (e.g. "close" on an overlay, which has no `WindowPattern`) unrepresentable in the API rather than a runtime validation error.

## 3. Public API surface

Every method is `bool ... (out string message)` except `IsRunning`; never throws (`catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))`, per the [Never-Throws Standard](../coding-standards/never-throws-standard.md)). Shipped: 22 methods, 0 properties, 4 events.

**Rules — native dialogs** (matches a fresh top-level Window-pattern element owned by the browser process):
```csharp
bool AddNativeDialogDismissRuleByName(string ruleName, string nameContains, string messageContains,
    string processName, string targetElementName, out string message,
    bool exactTargetElementName = true, string roleContains = null)
bool AddNativeDialogDismissRuleByAutomationId(string ruleName, string nameContains, string messageContains,
    string processName, string targetAutomationId, out string message, string roleContains = null)
bool AddNativeDialogCloseRule(string ruleName, string nameContains, string messageContains,
    string processName, out string message, string roleContains = null)   // WindowPattern.Close
bool AddNativeDialogWatchOnlyRule(string ruleName, string nameContains, string messageContains,
    string processName, out string message, string roleContains = null)
```

**Rules — in-page overlays** (matches a non-top-level element inside the page's own rendered subtree; no close action — no `WindowPattern` exists for it):
```csharp
bool AddPageOverlayDismissRuleByName(string ruleName, string nameContains, string messageContains,
    string processName, string targetElementName, out string message,
    bool exactTargetElementName = true, string roleContains = null, string automationIdContains = null)
bool AddPageOverlayDismissRuleByAutomationId(string ruleName, string nameContains, string messageContains,
    string processName, string targetAutomationId, out string message, string roleContains = null, string automationIdContains = null)
bool AddPageOverlayWatchOnlyRule(string ruleName, string nameContains, string messageContains,
    string processName, out string message, string roleContains = null, string automationIdContains = null)
```

**Shared rule management** (identical shape to `InterruptUtils`): `RemoveRule`, `ClearRules`, `SetRuleEnabled`, `ListRulesJson`. `ValidateCommon` requires a scope-specific criterion, and a process name alone is never enough: a `NativeDialog` rule needs at least one of `nameContains`/`messageContains`/`roleContains` (a `CloseWindowPattern` rule needs `nameContains` or `messageContains`); a `PageOverlay` rule needs at least one of `roleContains`/`nameContains`/`automationIdContains`. (Changed after the final review: a process-only rule could act on the user's main browser window, or match every element on a page.)

**Matching fields, UIA-adapted:**
- `nameContains` → `AutomationElement.Name`.
- `automationIdContains` → `AutomationElement.AutomationId` — **documented as best-effort for page content**: Chromium does not guarantee DOM `id`/`data-*` passthrough; reliable mainly for native-dialog buttons.
- `roleContains` → `AutomationElement.LocalizedControlTypeProperty` — the ARIA-role bridge (Chromium maps `role="dialog"`/`"alertdialog"` into this property); the primary way to target "any ARIA dialog" generically.
- `messageContains` → first non-empty descendant `Text`-control-type element's `Name`, found via a small bounded walk, computed lazily only when needed.
- `processName` → same `TrimExe`/`OrdinalIgnoreCase` match as `PopupRule.ProcessNamesMatch` (typical values: `chrome`, `msedge`, `firefox`). **Required for every rule** (see "Post-merge review fixes"): the component cannot identify browsers, so a rule with no process would apply to the whole desktop.

**Actions:** `InvokeByName`/`InvokeByAutomationId` (try `InvokePattern.Invoke()`, fall back to `TogglePattern.Toggle()` for controls that only expose Toggle), `CloseWindowPattern` (native-dialog only), `WatchOnly`.

**Lifecycle** (identical shape, plus overlay-specific tuning):
```csharp
bool Start(out string message, int sweepIntervalMs = 1000, int overlaySweepIntervalMs = 2000,
    int maxAttempts = 3, int maxDismissalsPerMinute = 20,
    int maxOverlayNodes = 5000, int maxOverlayDepth = 50)
bool Stop(out string message)
bool IsRunning()
bool Pause(out string message)
bool Resume(out string message)
```
`overlaySweepIntervalMs` has an enforced floor (500 ms when above 0) since it drives cross-process COM subtree walks, unlike the cheap native-dialog `EnumWindows` sweep. `maxOverlayNodes`/`maxOverlayDepth` bound that walk the same way `UIAutomationUtils.GetDescendantsSummaryJson`'s `maxNodes` already does for "a browser-hosted control with hundreds of children per level."

**Results/query and events** — identical shape to `InterruptUtils` (`GetDismissalCount`, `GetTotalDismissals`, `HasUnresolvedPopup`, `GetLastEventJson`, `GetLogJson(maxEntries, ...)` at 500-entry capacity, `ClearLog`; events `PopupDetected`/`PopupDismissed`/`PopupDismissFailed`/`InterruptError`, raised on the worker thread only, via `RaiseSafely` so one throwing subscriber can't block others). Log/event payloads add `scope` (`NativeDialog`/`PageOverlay`) and `role` fields.

**Constructors:** `public BrowserInterruptUtils()`, `public BrowserInterruptUtils(IContainer container)`, `internal BrowserInterruptUtils(IBrowserPopupProbe probe, IBrowserPopupHookSource hook, IInstanceGuard guard = null)` — same DI seam as `InterruptUtils`.

## 4. Internal architecture

**Native-dialog discovery (cheap):**
- Hook: one persistent `Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, TreeScope.Children, handler)`. The callback does nothing but a process-name check and enqueue — same "never block the callback" discipline as `PopupHookThread`.
- Sweep (safety net for missed events / already-open popups): periodic Win32 `EnumWindows`.
- Cost when no `NativeDialog` rule exists: effectively zero beyond the one event-handler registration.

**Page-overlay discovery (expensive, opt-in per browser process):**
- Hook: `Automation.AddStructureChangedEventHandler(browserWindowElement, TreeScope.Subtree, handler)`, registered dynamically **only** on browser windows whose process is named by a registered `PageOverlay` rule (added/removed via `WatchWindow`/`UnwatchWindow` from engine to hook thread). The callback does no walking; it only raises a throttled "window changed" notification.
- Sweep (fallback + coverage for overlays open before `Start`): periodic bounded breadth-first subtree walk from the browser window's element, capped by `maxOverlayNodes`/`maxOverlayDepth`, adapting `UIAutomationUtils.GetDescendantsSummaryJson`'s bounding pattern.
- Cost when no `PageOverlay` rule names a process: zero subtree-walk cost for that process's windows. Once a candidate is found, dismissal only walks that candidate's own small descendant set, never the full page.

**Concrete UIA shapes** (why the two paths differ mechanically — *design assumptions, unverified against a real browser*):
- Native JS dialog: a real top-level HWND, `ControlType.Window`, `WindowPattern` present, `Name` carries the prompt text, children are real `Button`/`Edit` elements supporting `InvokePattern`/`ValuePattern` — this is the case `DialogUtils` says `BM_CLICK` fails against, because UIA talks to Chromium's own accessibility provider instead of a native control handle.
- In-page overlay: no HWND, no `WindowOpenedEvent` — a subtree grafted into the *already-open* tab's tree, arbitrarily deep, potentially across an iframe boundary. No `WindowPattern`, so dismissal is always `Invoke`/`Toggle`, never `Close`.

**Threading/lifecycle:** mirrors `InterruptUtils`' two-thread model (dedicated hook thread with its own message pump, plus a worker loop calling `BrowserPopupEngine.Pump(nowTick)` on the same `{0,150,400,1000,2000}` re-check cascade, `AutoResetEvent`-gated `WaitForWork`, same runaway-dismissal breaker, same `maxAttempts`, 500-entry log, 100-rule cap). All actual UIA property/pattern reads happen only on the worker thread, never inside a COM callback. Candidates are tracked by runtime ID (an equatable `BrowserElementRef`) instead of `IntPtr hwnd`, since most overlay candidates have no HWND. `InstanceGuard.cs` is duplicated with a distinct mutex name (`Local\BrowserInterruptAutomation.Watcher`) so both components can watch independently.

**Probe/hook seam (what's testable without a live browser):** `BrowserPopupEngine` never touches `System.Windows.Automation` directly — everything crosses `IBrowserPopupProbe`/`IBrowserPopupHookSource` as plain POCOs (`BrowserWindowInfo`, `BrowserElementInfo`). This is the same discipline that makes `PopupEngineTests.cs` possible via `FakeProbe`/`FakeHookSource`.

## 5. File layout

`src/browserinterruptutils/`: `BrowserInterruptUtils.cs` (public Component/API), `BrowserPopupEngine.cs`, `BrowserPopupRule.cs`, `IBrowserPopupProbe.cs`, `BrowserPopupHookThread.cs`, `UiaBrowserPopupProbe.cs`, `StructureChangedThrottle.cs` (added), `NativeMethods.cs`, `InstanceGuard.cs`, `NeverThrowsGuard.cs`, `BrowserInterruptEventArgs.cs`, `BrowserInterruptUtils.csproj`, `README.md`, `Documentation/`.

`src/browserinterruptutils/BrowserInterruptUtils.Tests/` (excluded from the main DLL via `<Compile Remove="BrowserInterruptUtils.Tests/**/*.cs" />`): `BrowserInterruptUtilsTests.cs`, `BrowserPopupEngineTests.cs`, `BrowserPopupRuleTests.cs`, `StructureChangedThrottleTests.cs`, `UiaTests.cs` and `HookThreadUiaTests.cs` (real-UIA smoke tests, Windows only), `TestSupport.cs` (fakes), `DocumentationSyncTests.cs`.

## 6. csproj

`InterruptUtils.csproj`'s zero-dependency shape with `UseWPF=true` (needed for `System.Windows.Automation`): `net8.0-windows;net10.0-windows`, `AssemblyName`/`RootNamespace` `BrowserInterruptAutomation`, `EnableWindowsTargeting`, `GenerateDocumentationFile`, README pack item, zero `PackageReference`, `InternalsVisibleTo` → `BrowserInterruptUtils.Tests`, no `ProjectReference` to any other component.

## 7. Implementation sequence (as executed)

1. **Component foundation** — public contracts, rule model, event args, probe/hook interfaces, guards, csproj/test scaffolding.
2. **Engine core** — `BrowserPopupEngine.cs` against the probe/hook interfaces only, with `TestSupport.cs` fakes.
3. **Real UIA probe** — `UiaBrowserPopupProbe.cs`.
4. **Real UIA hook** — `BrowserPopupHookThread.cs`.
5. **Public API wiring** — `BrowserInterruptUtils.cs`.
6. **Repository integration** — `src/AwesomeRpaUtils.sln`, root `README.md` (component row, "twenty-seven" DLL count, xunit list), `CONTRIBUTING.md`, `CrossReference.md`, `scripts/Package-Release.ps1`'s hardcoded `$releaseAssemblies` (`Pack-NuGet.ps1` globs `.csproj` and needed no change; `Package-Documentation.ps1` discovers `src/*` on its own), `TESTING.md`, component README + `Documentation/`, documentation-sync tests, this plan document, and the README pack item in the csproj.
7. **Pega usability review** — deferred (see [Deferred](#deferred)).

## 8. Verification

**Feasible in CI (fakes only, no live UIA/browser):** rule validation/CRUD across every matching field, the retry cascade, runaway-dismissal breaker, max-attempts give-up, Pause/Resume semantics, `NativeDialog` vs `PageOverlay` scope gating, close-only-valid-for-native-dialog enforcement, log capacity/JSON shape, instance-guard behavior, the structure-changed throttle, and documentation sync.

**Needs a real Windows desktop but not a real browser (self-skips off Windows):** `UiaTests` and `HookThreadUiaTests`, small smoke tests against an in-process WPF window, confirming the real probe and hook drive real `System.Windows.Automation`.

**Live/manual only (no exerciser for v1):** end-to-end behavior against a real Chromium `alert()`/`confirm()` and a real ARIA `role="dialog"` overlay in an actual browser tab; recorded as pending in `TESTING.md`.

### Verification status

- **Done:** the fake-driven suite passes under `-p:UseWPF=false` on Linux, except `Start_StopsAnotherRunningInstanceThatSharesTheGuard`, which relies on Windows named-mutex behaviour (it also fails for the `InterruptUtils` counterpart on Linux).
- **Compile-verified only:** `UiaBrowserPopupProbe`, `BrowserPopupHookThread`, `UiaTests`, `HookThreadUiaTests`. The development host is Linux and has no `Microsoft.WindowsDesktop.App` runtime, so none of these has been executed.
- **Pending (real Windows host, real browsers):** everything listed under "BrowserInterruptUtils" in [TESTING.md](../../TESTING.md#browserinterruptutils-needs-a-desktop-and-a-real-browser-live-checks-pending), including whether JS `alert`/`confirm`/`prompt` are top-level windows at all in current Chrome/Edge, the names/roles a browser really exposes, structure-changed latency and whether the untuned 250 ms throttle is right, first-query latency (Chromium builds its accessibility tree lazily), iframe reach, `Pause` behavior while a dialog is being driven, and headless behavior.

## 9. Acceptance criteria

- [x] Native JS dialogs are dismissed via UIA `InvokePattern`/`WindowPattern.Close`, with no mouse movement or `BM_CLICK` — *implemented; live behavior pending*.
- [x] In-page ARIA-role overlays are detected via structure-changed-driven and periodic bounded subtree scanning, gated per-process by registered `PageOverlay` rules — *implemented; live behavior pending*.
- [x] `BrowserPopupEngine` is fully testable via hand-rolled fakes with no live UIA/browser dependency.
- [x] Rule CRUD, lifecycle, results/query, and events match `InterruptUtils`' never-throws contract and JSON shapes (plus `scope`/`role`).
- [x] Runaway-dismissal breaker, retry cascade, log capacity, and rule cap match `InterruptUtils`' documented constants.
- [x] `src/AwesomeRpaUtils.sln`, root `README.md`, `CrossReference.md`, `scripts/Package-Release.ps1`, and `TESTING.md` are all updated.
- [ ] Functional tests pass on both target frameworks *on a Windows host*; live browser/desktop checks are recorded as pending, not claimed.

## 10. Assumptions and boundaries

- **Headless browser sessions are unsupported** — no HWND, no UIA tree exists at all. Documented prominently as the most likely "why doesn't this work" question.
- **Attended-session constraint carries over from `InterruptUtils`**, likely more strictly for UIA (no delivery on a locked/secure desktop) — documented as inherited, to be verified in the live checks.
- **Cross-origin iframe content**: Chromium generally surfaces this through its unified accessibility tree, so a bounded subtree walk should reach it — a claim to verify empirically, documented as unverified.
- **Non-UIA-exposing browsers are out of scope by construction** — no per-vendor special-casing beyond `processName` matching.
- **No CDP, no browser-driver binaries, no special browser launch flags** — zero new NuGet dependencies beyond `UseWPF=true` (already precedented by `UIAutomationUtils`).
- **No exerciser/test-harness entry for v1** — matches `InterruptUtils`' own ship history; deferred, not skipped forever.

## Deviations from the plan as implemented

- **`Pause` is a hard stop for starting probe actions only.** The engine holds an action lock around the final paused/enabled/registered re-check, the probe call and the recording of its result (never during discovery), and `WaitForIdle` takes that lock. `Pause`, `RemoveRule`, `ClearRules` and `SetRuleEnabled(false)` call it, so no new invoke/close call starts for what was switched off once they return. It does not stop the browser acting on a call that already returned (UIA returns first; the engine confirms `VerifyDelayMs` = 400 ms later), so an action issued just before may still take effect and its `Dismissed`/`DismissFailed` may be recorded after they return; the verification is read-only and keeps running while paused, and a retry it schedules is held until `Resume`. Consequence: the UIA invoke/close has no timeout, so these methods can block for as long as a hung target application stays hung. Documented in the README and Lifecycle page.
- **Overlay-target lookup reuses `FindOverlayCandidates`.** Finding the button to invoke inside an already-matched popup calls the same bounded-walk primitive with the popup as the root, because the probe interface had no "find one descendant" method. The real probe therefore returns every element it visits, not only dialog-shaped ones, so an overlay rule matches any element in the walk: rules should combine `roleContains` with a name or message (documented in Rules). Only elements passing the non-message criteria of at least one enabled overlay rule are tracked (so the 2000-candidate cap is a safety net, reported once as an `InterruptError`), and native top-level windows are only described when an enabled native rule (or an overlay rule wanting to watch the browser) names their process; to be examined in the live checks.
- **The hook throttles structure-changed notifications.** `BrowserPopupHookThread` raises at most one notification per 250 ms per watched window (`StructureChangedCoalesceMs`; leading edge plus ONE trailing notification per burst, decided by the pure `StructureChangedThrottle.Decide`/`TryConsumeTrailing`), because the engine runs one full walk per dequeued notification and does not de-duplicate. The first signal raises immediately; signals suppressed inside the cooldown coalesce into one pending trailing notification, delivered by a lazily-created per-window single-shot `System.Threading.Timer` when the cooldown ends (re-armed if a newer raise restarted the cooldown; cancelled by `UnwatchWindow`/`Stop`). This was added after review: with `overlaySweepIntervalMs = 0` a purely leading-edge throttle could drop a signal after the worker's walk and leave a late overlay undiscovered, with no periodic sweep to recover it. The 250 ms value is untuned.
- **The throttle state is one atomic word (review-driven).** The per-window last-raised timestamp and trailing-pending flag were two fields, so a timer callback could read the old timestamp, have a leading raise restart the cooldown and a new suppressed signal set the flag, then clear that new flag and lose the trailing (a late overlay never walked with `overlaySweepIntervalMs: 0`). `StructureChangedThrottle` now packs `(lastRaisedMs << 1) | pending` into one `long` and every transition (`Decide`, `TryConsumeTrailing`) is a single compare-and-swap loop on it, so a stale consumer fails its CAS and re-decides; `Decide` also returns the arm delay from the snapshot it committed. Covered by a deterministic interleaving test and a multi-threaded stress test.
- **`BROWSERINTERRUPT_UIA` compile constant and the `-p:UseWPF=false` Linux test technique.** `UiaBrowserPopupProbe.cs` and `BrowserPopupHookThread.cs` (the only files referencing `System.Windows.Automation`) are compiled only when `UseWPF` is true, and the two `new` calls that reference them in `BrowserInterruptUtils.cs` sit behind `#if BROWSERINTERRUPT_UIA`. That lets the rest of the component and its fake-driven tests build and run on a Linux host with `-p:UseWPF=false`; it is a local convenience, not a supported build configuration.
- **Process-name cache eviction.** The engine's process-name cache outlives a single pass (names are learned only from top-level windows), so it evicts a process ID's entry as soon as no tracked candidate of either scope carries it, bounding stale-name risk from PID reuse.
- **A dismissal is counted only after the popup is seen to close (`VerifyDelayMs`, 400 ms).** UIA `Invoke`/`Close` return before the browser acts. After a successful call the candidate stays tracked and unresolved with a verification deadline; at the deadline `IsAlive` decides. Gone: record `Dismissed`, count it, feed the runaway window. Still open: a failed attempt (no `Dismissed`; retry, or `DismissFailed` "the action succeeded but the popup is still open" once `maxAttempts` is spent), so a do-nothing invoke is bounded to `maxAttempts` per popup. The check is read-only, so it takes no action lock; a retry goes through the normal path and its `Paused`/rule re-checks. `Stop` inside the window drops the pending confirmation.
- **The probe's element cache holds strong references with generation-based eviction.** A weak cache could lose the wrapper of a live overlay to a GC between discovery and the engine's re-check (an overlay ref has no `Hwnd` to re-resolve from). `GenerationalCache` (UIA-independent, unit-tested) keeps two dictionaries, promotes hits, and rotates on size (8192) or age (60 s), not per walk, since one sweep makes several walks. An element with neither a runtime ID nor a window handle is not describable (it would alias every other such element).
- **Records reach subscribers only after the pass's state is final.** Records produced while the worker is inside `Pump` are buffered and delivered when the pass ends, after counts, removed candidates, the unresolved count and the process-name cache are all updated, so a subscriber (or code it wakes) never sees `Dismissed` while `HasUnresolvedPopup` is still true. Records raised outside a pump (a fault reported from another thread) are delivered at once.
- **Candidates are pinned (`Retain`/`Release`).** The generational cache alone could evict a window-less overlay's element (more than 16,384 distinct elements walked before evaluation, e.g. four default 5,000-node windows in one pass), and `IsAlive`/`TryResolve` then reported a live popup dead, dropping it or falsely confirming a pending dismissal. `IBrowserPopupProbe` gained `Retain`/`Release`: the engine pins a candidate when it admits it (native and overlay; a cap-refused one is never pinned) and releases it on every removal path (dismissal, orphaning by owner-window death, reap, reset); the real probe keeps pinned elements in a separate dictionary of strong references that cache rotation never evicts. `IsAlive` is tri-state in effect: a window-less ref that is neither pinned nor cached reports **alive** (unknown is not dead), so a lost ref is bounded by `MaxAttempts`, owner-window death or the reap instead of being silently dropped; only a definite "gone" (destroyed window, unavailable pinned/cached element) reports dead.
- **Parked candidates are reaped, overlay and native.** Watch-only matches, failed dismissals and browser windows tracked only as watch owners sit at `NextDue == long.MaxValue` and are otherwise revisited only when a sweep re-finds them, so transient banners accumulated until the 2000-candidate cap blocked new popups. A liveness pass every 2000 ms (`ReapIntervalMs`, at most 256 `IsAlive` checks per pass with a rotating cursor) drops parked candidates of either scope that are definitively gone (releasing their pins; a native window goes through the normal removal: unwatch, orphaned overlays dropped, process-name cache evicted). It runs independently of `sweepIntervalMs`/`overlaySweepIntervalMs` (0 turns discovery off, never this cleanup), and the pump's next-due includes it whenever any parked candidate exists, so a sweeps-off worker still wakes to reap. Absence from a bounded walk is deliberately not taken as closure, and unknown liveness is never reaped. No `Dismissed` is recorded for a reaped candidate (only a verified action produces one).
- **Overlays inherit their window's process identity.** An in-page element's UIA `ProcessId` can be a renderer's, with no cached name, which made every process-scoped overlay rule reject it. An overlay found under a watched window is stamped with that window's PID (name via the process-name cache) for rule matching, the own-process safety check and the records; the admission-time substitution of the rule's process name was removed (an overlay under a window whose name is not yet known is simply admitted by a later sweep).
- **Native rules refuse main-window-like windows.** A native rule with only a name substring can match the browser's main window (a tab title): `WindowPattern.Close` would close the browser, and an invoke rule's subtree walk would cover the page and press the first page control with the target's name. The real probe reports `BrowserElementInfo.IsMainWindowLike` (a `WS_MINIMIZEBOX`/`WS_MAXIMIZEBOX` in `GWL_STYLE`, read with 32/64-bit-correct `GetWindowLong(Ptr)`; an unreadable style counts as main-window-like), and the engine records one `DismissFailed` without spending retries and makes no probe call at all (no target walk, no invoke, no close) for every acting native rule; watch-only still reports. (Originally only close was guarded; see "Post-merge review fixes".) That Chromium/Firefox JS dialogs lack those style bits is a pending live check.
- **Child enumeration is lazy.** The probe walks children with `GetFirstChild`/`GetNextSibling` and stops enqueuing once the node budget is reached, so a huge sibling list is never enumerated past `maxOverlayNodes` (or materialized).
- **`IsAlive` checks the window handle first.** A ref with an `Hwnd` is only as alive as that window (`IsWindow`), since a just-destroyed window's UIA element can keep answering from a stale provider; a dead handle also evicts its cache entry.
- **`IsAlive` reports dead only when it is definitive.** Only a destroyed window (`IsWindow` false) or an `ElementNotAvailableException` from a `Current` read of a live-resolved, pinned or cached element is `Dead`; any other read/provider failure (COM error, timeout, other `InvalidOperationException`) is `Unknown`, reported alive, never evicts a cache entry, and makes an action fail with a "could not be determined" reason rather than "already closed". An alive/unknown answer at the verification deadline is a failed attempt bounded by `MaxAttempts` (the retry), so a transient error can never record a still-open popup as dismissed. The rules live in the UIA-independent `LivenessClassifier` (unit-tested on Linux).
- **Disabling a rule releases the candidates it selected.** `ApplyRuleChanges` resets a candidate whose selected rule is now disabled (clears the rule and any parked `Failed`/`Reported`/attempt state, due now) so the enabled rules are evaluated again; a pending dismissal verification is left intact and still confirmed, and if that verification finds the popup still open under a disabled rule the candidate is released the same way instead of being parked. Removing a rule already reset its candidates (including a pending one, which records nothing).
- **Property reads are uncached.** About eight cross-process reads per visited element; `CacheRequest` was deliberately not adopted. Identity (runtime ID) is read first so an undescribable element costs one read.
- **`BrowserControlType` was removed.** It was planned (§5) as a small public enum but nothing consumed it.
- **Repository integration added beyond the plan's list:** `CONTRIBUTING.md` (the `UseWPF` test-running note), the short "Browser popups" cross-references in the `InterruptUtils` and `DialogUtils` READMEs, and the README pack item in the csproj.

## Post-merge review fixes (PR 1)

- **`processName` is required for every rule (both scopes).** An empty process meant "watch every browser process", but nothing identifies browsers: overlay rules made every visible top-level window of every application described, watched and swept each interval, and native rules with no process could act on non-browser dialogs. `BrowserPopupRule.ValidateCommon` now rejects an empty (or whitespace/`.exe`-only) `ProcessName` ("processName is required: ..."), and the engine's "no process = any process" machinery (`_overlayWatchesAnyProcess`, `_nativeWatchesAnyProcess`, the name-less branches of the interest checks) and `MatchesCheap`'s empty-process branch were removed. Rule matching, overlay watching/sweeping and native describing are all keyed to the named processes only.
- **Native invoke rules refuse main-window-like windows.** The main-window guard covered only `CloseWindowPattern`; an invoke rule on a browser main window (tab title matching the rule) walked the whole page and pressed the first control with the target's name. `Act` now refuses every acting native rule on such a window (one `DismissFailed`, attempts 0, no probe call); watch-only is unaffected.

## Deferred

- **Pega usability review** (`project-docs/pega-usability-reviews/BrowserInterruptUtils-pega-usability-review.md` and an index entry) — **pending**; not written as part of this work, not a v1 blocker.
- **Exerciser / test-harness support** — not planned for v1 (see decisions above).
- **Live Windows/browser verification** — pending (see [Verification status](#verification-status)). First open question: whether Chrome/Edge JS `alert`/`confirm`/`prompt` appear as top-level windows (the `NativeDialog` scope this design assumes) or only as in-page or child elements, in which case users need a `PageOverlay` rule with `roleContains: dialog`. Also open: walk cost on a large page (consider `CacheRequest` and a lower default `maxOverlayNodes`).
