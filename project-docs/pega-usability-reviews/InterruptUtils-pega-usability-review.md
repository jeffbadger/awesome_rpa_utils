# InterruptUtils Pega usability review

This review was completed before implementation. `InterruptUtils` exists because an
automation's steps run one at a time on a single thread and cannot be interrupted, so
nothing in a step can handle a popup that appears during a long wait. The component's
job is to do that on its own threads, which makes the review's questions different
from a normal component's: what can Robot Studio set up ahead of time, and how does a
later step learn what happened.

## Findings applied

- **All rules are set up with plain method calls.** Each `Add...Rule` method takes
  scalars (strings, a Boolean, a repository-owned enum), so every port can be a
  constant. There is no rule object, collection, or handle to construct. One rule kind
  per method, with unique names, keeps the
  [Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md)
  satisfied and each method's purpose visible on the designer surface.
- **Rules cannot mean "everything".** A rule needs at least one of title, message or
  process, so a mistyped or blank constant cannot turn into "click whatever dialog
  appears".
- **Results are readable without an event.** Robot Studio's support for component
  events is inferred rather than confirmed (see the
  [FileWatchUtils review](FileWatchUtils-pega-usability-review.md)), and these events
  arrive on a worker thread. So every outcome is also available as a scalar or JSON from
  a later step: `GetDismissalCount`, `GetTotalDismissals`, `HasUnresolvedPopup`,
  `GetLastEventJson`, `GetLogJson`.
- **Events are isolated.** A subscriber that throws cannot stop other subscribers or the
  watch; event arguments are repository-owned classes with read-only scalar properties.
  The args class is named `InterruptPopupEventArgs` because `PopupEventArgs` collides
  with a `System.Windows.Forms` type.
- **Lifecycle is explicit and forgiving.** `Start` returns immediately; `Stop` succeeds
  when not running; disposal while running is safe; `Start` after disposal is refused with
  a message. Rules, counts and the log survive `Stop`.
- **A step that drives a dialog itself can opt out** with `Pause`/`Resume` or
  `SetRuleEnabled`, and the automation's own process's popups are never touched.

## Decisions

1. **Decision: standalone component, not a composition of WinEventUtils and DialogUtils.**
   The repository forbids project references between components. `InterruptUtils`
   carries its own small window-event hook and click code, duplicated the way
   `NeverThrowsGuard` is. The cost is some duplicated Win32 code; the benefit is one DLL
   with no ordering or version coupling.
2. **Decision: a periodic scan backs up the window events.** The hook cannot see the Robot
   host's own process, misses applications that do not raise standard events, and cannot
   see a popup that was already open at `Start`. The scan covers all three. It is on by
   default (1 s) and can be turned off.
3. **Decision: bounded, not cancellable.** Like the other components, it does not try to
   interrupt a blocked step (there is no mechanism); it works beside it. Retries per popup
   and dismissals per rule per minute are capped, so a misbehaving popup cannot loop.
4. **Decision: no attempt at WinUI/UWP or custom-drawn popups.** They have no native
   buttons; a click rule reports that clearly and points at `KeyboardUtils` or
   `UIAutomationUtils`. A close rule is offered for button-less windows.
5. **Decision: `Pause` defers rather than discards.** A popup that appears while paused and
   is still open at `Resume` is then handled, so a genuine interruption during the paused
   step is not lost.

## Accepted caveats

Window events are not delivered on a locked screen or the secure desktop, so it suits an
attended session or an unattended one with an active desktop. Popups are matched by
substring on title and message; a popup that draws its message itself can only be matched
on title or process. Events arrive on a worker thread. Clicking a button such as Yes or No
is a decision about the automation's data, so a rule should exist only for a popup whose
answer has been decided.
