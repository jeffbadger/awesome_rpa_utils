# Pega Robotics Review

Scope: every public `MouseUtils` method. Overloads and convenience wrappers are
reviewed together when they share one implementation and risk profile.

## Project assessment

The toolbox-friendly `Component`, categories, descriptions, primitive ports, and
usual `bool`/message contract fit Robot Studio well. Operations still require an
interactive unlocked Windows desktop and cannot inject into a higher-integrity
target. Confirm Robot Studio and Robot Runtime can load `net10.0-windows`; Pega's
guidance says the project target must match Robot Runtime.

Key findings:

- **High:** `DragAndDrop`, `DragAndHold`, and `BezierDragAndDrop` can fail after
  button-down without releasing left; `RubberBandSelect` inherits this defect.
- **High:** `Dispose` does not undo hiding, clipping, input blocking, system-cursor
  replacement, or injected down states. Exception/termination paths need cleanup.
- **Medium:** `ClickAndRestore` can return `true` while reporting restore failure.
- **Medium:** sleeps block the Pega automation thread and cannot be cancelled.
- **Medium:** coordinate APIs must share the host's DPI coordinate model.
- **Low:** silent coercion of invalid numeric inputs can hide bad design wiring.

## Method reviews

### Position

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `GetX` | Reads cursor screen X. | Check the result: `0` is valid and the failure sentinel. Prefer `GetPosition` for one X/Y snapshot and physical APIs when matching pixels. |
| `GetY` | Reads cursor screen Y. | Same as `GetX`; negative Y is valid above the primary monitor. |
| `GetPosition` | Reads X/Y atomically into `Point`. | Correct choice for a snapshot, but a primitive `out x/out y` overload would wire more easily than a struct in Pega. |
| `MoveTo` | Immediately moves the real cursor. | Off-screen input can be clamped; success does not prove hover/focus or the intended target. Validate bounds and target. |
| `MoveBy` | Adds offsets to the current position. | User movement can race it; addition can overflow and the result can clamp. Use checked/bounded calculation. |
| `SmoothMoveTo` (both) | Interpolates a straight path; default is 25 × 5 ms. | Silently coerces `steps < 1`, accepts negative delay as zero, blocks the Pega thread, and lacks cancellation. Validate/bound inputs. |
| `JiggleMouse` | Nudges and restores the pointer to generate activity. | May trigger hover behavior and should not be used to bypass enterprise lock policy. An execution-state API is cleaner where policy permits. |

### Clicks

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `Click` | Injects down, waits 20 ms, then up. | A failed up can leave the button held. Add best-effort release and target verification. |
| `ClickAt` | Moves, waits, and clicks. | Pointer/target can change between move and click. Use `SafeClickAt` or an optional target guard. |
| `DoubleClick`, `DoubleClickAt` | Sends two clicks 50 ms apart. | Fixed timing ignores system configuration and target stability; make timing bounded/configurable and verify target. |
| `LeftClick`, `RightClick`, `MiddleClick` | Named wrappers over `Click`. | Designer-friendly, but inherit stuck-button, focus, UIPI, and wrong-target risks. |
| `LeftClickAt`, `RightClickAt` | Named wrappers over `ClickAt`. | Inherit coordinate/DPI/race risks; `MiddleClickAt` is missing from the convenience surface. |
| `LeftDoubleClick`, `RightDoubleClick`, `LeftDoubleClickAt` | Named double-click wrappers. | Inherit timing/target risks; `RightDoubleClickAt` and middle variants are missing. |
| `MouseDown`, `MouseUp` | Expose stateful button injection. | Every successful down must reach an up in `finally`; add an emergency release during component cleanup. |
| `ClickAndHold` | Holds a button for a duration, then releases. | No `finally`; interruption/release failure can strand it. Reject negative duration and guarantee cleanup. |
| `ClickWithModifiers` | Sends modifier/button down and up batches. | Ordering and one cleanup retry are good. Reject undefined flag bits; total up failure can still strand input. |
| `ClickAndRestore` | Clicks elsewhere and restores the cursor in `finally`. | **Defect:** restore failure may return `true` with an error. Return `false` and preserve the native error. |
| `ClickWithRetry` | Retries the entire move/click. | Ambiguous partial failure can double-submit non-idempotent actions. Retry only known-safe failures and verify UI outcome. |
| `TripleClick` | Sends three clicks with fixed gaps. | App behavior varies and partial success changes selection. Make timing configurable; avoid blind business-action retries. |

### Drag and drop

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `DragAndDrop` (both) | Presses left, interpolates, releases. | **High:** movement failure returns without button-up. Put release in `finally`, validate step/delay values, and verify the drop target. |
| `RubberBandSelect` (both) | Holds modifiers around `DragAndDrop`. | Modifier cleanup is good, but nested drag can leave left down; undefined flags are ignored and cleanup failures discarded. |
| `DragAndHold` | Drags, pauses at destination, releases. | Same missing button-up `finally`; long hold blocks the automation thread. |

### Wheel and scrolling

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `Scroll` | Injects a vertical wheel delta. | Zero can report useless success; high-resolution deltas and target-under-cursor behavior need documentation/verification. |
| `ScrollUp`, `ScrollDown` (all) | Convert notch count to a signed delta. | Absolute value hides negative wiring errors; reject negative counts and split extreme values into realistic events. |
| `ScrollHorizontal` | Injects horizontal wheel input. | Apps may ignore/invert it; provide UI Automation scrollbar fallback and verify outcome. |
| `ScrollRight`, `ScrollLeft` (all) | Directional horizontal wrappers. | Inherit compatibility limits; negative counts are silently treated as positive. |
| `ScrollHorizontalAt` | Moves to a point and scrolls there. | Does not restore the operator cursor or verify receiver. Add restore/target guard; a vertical `ScrollAt` is missing. |

### Cursor state

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `SetCursor`, `ReplaceSystemCursor` | Replace user-session system cursor slots. | Global and persistent, excessive for routine RPA, invalid enums unchecked, and disposal does not reset. Prefer a local overlay. |
| `SetCursorFromFile` | Loads `.cur`/`.ani` and replaces a slot. | Validate extension, size/trust and absolute path; track cleanup rather than relying on every caller. |
| `ResetSystemCursors` | Reloads all configured cursors. | Can overwrite concurrent changes by the user/another process. Restore only state owned by this component. |
| `HideCursor`, `ShowCursor` | Balance a thread-local native display counter using one flag. | Pega callbacks may switch threads, native results are ignored, and disposal does not rebalance. Track owner thread and clean up. |
| `IsCursorVisible` | Returns this instance's hidden flag. | Not actual Windows visibility; multiple instances/other apps make it unreliable. Rename or use `GetCursorInfo`. |
| `ClipCursor` | Confines the pointer session-wide. | Hazardous attended state, accepts off-screen bounds, and is not released on disposal. Use briefly with unconditional cleanup. |
| `ReleaseCursorClip` | Clears all cursor confinement. | Also clears a clip owned by another app. Save and restore the previous rectangle/ownership. |
| `GetCursorClip` | Returns current clip as `Rectangle`. | Full-screen does not prove no explicit clip; primitive edge outputs would be easier in Pega. |

### Button state and screens

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `IsLeftButtonDown`, `IsRightButtonDown`, `IsMiddleButtonDown` | Query asynchronous high-bit state. | Cannot distinguish physical/injected input, report errors, respect swapped meanings, or query X buttons. Use only as a momentary heuristic. |
| `GetDoubleClickTimeMs` | Reads user double-click time. | Straightforward and useful for timing; no material method-specific issue. |
| `SetDoubleClickTimeMs` | Changes double-click time session-wide. | Avoid altering operator preferences. Capture/restore the exact prior value; `0` means 500 ms, not the user's previous setting. |
| `GetScreenWidth`, `GetScreenHeight` | Return primary-monitor dimensions. | Often wrong for multi-monitor robots; prefer virtual or monitor-specific bounds. |
| `GetVirtualScreenBounds` | Returns the all-monitor bounding box. | Box can include dead gaps and has no error signal; enumerate monitors for exact validation. |
| `IsPointOnScreen` | Tests the virtual bounding box. | Can return true in a gap between staggered monitors. Use `MonitorFromPoint`. |
| `ClampToScreenX`, `ClampToScreenY` | Clamp each axis to virtual bounds. | Combined point may remain in a monitor gap. Clamp to the nearest actual monitor rectangle. |

### Input blocking

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `BlockUserInput` | Blocks physical input while injected input continues. | Hazardous/deprecated, and not tracked for disposal. Use only briefly with `finally` and an operator recovery plan. |
| `UnblockUserInput` | Best-effort unblock. | Discards native result and only the blocking thread can unblock; expose status and track owner thread. |

### Background clicks

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `ClickWindow` | Posts to a window's outer-rectangle center. | Center may be non-client/meaningless; minimized geometry is unreliable and post success does not mean action handled. |
| `ClickWindowAtPoint` | Converts screen to client then posts. | Validate handle/point and DPI model; UIPI, WPF, Electron, and Chromium may ignore messages. |
| `ClickWindowAtClientPoint` | Posts down/up at client coordinates for five buttons. | Omits current modifier/button state, truncates coordinates to 16-bit `lParam`, and has no acknowledgment/outcome check. |
| `DoubleClickWindowAtClientPoint` | Posts a left DOWN/UP/DBLCLK/UP sequence. | No other buttons, no class-style validation, and post success is not processing success. |

### Window-relative targeting

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `GetWindowBounds` | Returns outer window rectangle. | Includes non-client/invisible resize border. Prefer client/extended-frame bounds, handle validation, and consistent DPI. |
| `ClientPointToScreen`, `ScreenPointToClient` | Convert points for a handle. | `0,0` is ambiguous on failure and handles can stale between conversion/action; always branch on `bool`. |
| `ClickAtClientPoint` | Converts then performs a real click. | Does not ensure visible, unobstructed, foreground, or unchanged target. Combine with a safety guard. |
| `ClickAtRelativePosition` | Clicks a client-area fraction. | `NaN` passes range checks; `1.0` targets exclusive right/bottom edge; reject non-finite/empty clients and map to `size - 1`. |
| `GetWindowAtPoint` | Returns the child/topmost window at a point. | Result may be an overlay/child; zero is normal. Document root-vs-child behavior. |
| `SafeClickAt` | Checks window/root before coordinate click. | Valuable guard, but zero expected handle can authorize empty space and target can change after check. Reject zero/recheck near injection. |

### DPI and physical coordinates

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `GetPhysicalCursorX`, `GetPhysicalCursorY` | Read physical cursor axes. | Check result because zero is valid; add a combined physical-position method for one snapshot. |
| `IsProcessDpiAware` | Tests whether context is not DPI-unaware. | `false` conflates failure/unaware and `true` hides system-aware vs per-monitor-v2. Return actual mode plus error. |

### Highlight and human-like movement

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `FlashCursorHighlight` | Flashes an XOR ring using the screen DC. | Blocks, coerces invalid values, ignores several GDI failures, and may artifact. Prefer a cancellable overlay window. |
| `MoveMouseBezier` | Moves on a randomized curve. | Random paths reduce repeatability and can cross unintended UI/gaps. Remove the “anti-detection” positioning; add bounded deterministic mode/cancellation. |
| `BezierClickAt`, `BezierDoubleClickAt` | Randomized movement then click/double-click. | Inherit target, DPI, timing, and repeatability risks; add a final target guard. |
| `BezierDragAndDrop` | Left-drags on a randomized curve. | **High:** movement failure can leave left down. Use `finally` and constrain/verify the entire path. |

### Verification and synchronization

| Method | What it does | Pega concerns / improvements |
|---|---|---|
| `GetPixelColor` | Reads one screen pixel as `COLORREF`. | Brittle with DPI, DWM/GPU, RDP, animation, color management, and occlusion. Prefer UIA or region matching for business logic. |
| `WaitForPixelColor` | Polls for exact color until timeout. | Timeout/failure both false, negative timeout is immediate, exact equality is fragile, and sleep blocks. Validate timing; add tolerance/cancellation. |
| `WaitForPixelChange` | Polls until one pixel differs. | Noise/animation can false-trigger; add stability duration and region/tolerance thresholds plus cancellation. |
| `IsBusyCursorActive` | Compares global cursor with standard busy handles. | Not target-scoped, misses custom cursors, and false also means failure unless `message` is inspected. |
| `WaitForIdleCursor` | Polls until global standard busy cursor clears. | Can be wrong because another app owns the cursor or the target uses another indicator. Scope to target and support cancellation. |

## Recommended repair order

1. Guarantee release cleanup for all button/key down paths.
2. Track and restore component-owned global state during cleanup.
3. Correct `ClickAndRestore`; validate zero handles, `NaN`, and stale targets.
4. Add cancellable/bounded waits and movements.
5. Test on supported Robot Studio/Runtime versions across DPI modes, integrity
   levels, RDP states, and monitor layouts.
