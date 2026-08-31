# Signature Uniqueness Standard

## Purpose

The utilities in this repository are consumed from Pega Robot Studio's
designer surface, which resolves a method by name and its list of
Pega-visible (non-`out`) input parameters. It does not read `out` parameter
types or count the way the C# compiler does. Two public overloads that share
a name and an identical ordered list of non-`out` parameter types are fully
legal C# — the compiler disambiguates by `out` shape — but they render as
indistinguishable entries in Robot Studio's method picker. A designer cannot
reliably tell which one they placed, and the wrong one silently satisfies the
call.

This standard applies independently to every utility project, the same way
the [Never-Throws Standard](never-throws-standard.md) does. It governs how
new overloads must be named so this ambiguity cannot recur.

## Rule

For any two public overloads of the same method name:

- Compare only the **non-`out`, non-`ref`** parameters, in order, by type.
- If that ordered type list is identical between the two overloads, they are
  a **violation** regardless of how their `out`/`ref` parameters differ (by
  type, by count, or by presence).
- Arity differences in the non-`out` parameters (e.g. one overload takes an
  extra `int pollIntervalMs` the other doesn't) are **not** a violation —
  Pega can distinguish argument count.
- A different non-`out` parameter *type* at the same position (e.g. `int
  colorRef` vs. `Color color`) is **not** a violation — Pega can distinguish
  parameter type for inputs, just not for outputs.

This is a **naming** rule, not a design rule: an overload pair with this
shape is not inherently wrong (returning both a `Rectangle` and its scalar
components is a reasonable API), it just cannot share one name.

## Naming convention

When two overloads collide under this rule, rename the overload that is
*less directly usable from a Pega flow* — never the newer, more
disambiguated one. The one that stays on the plain (un-suffixed) name should
be whichever a Pega automation would reach for by default.

Pick the suffix from what actually distinguishes the pair:

| Situation | Suffix | Example |
|---|---|---|
| The renamed overload is missing a disambiguating Boolean output the other has (`timedOut`, `querySucceeded`, `wasAlready*`, `comparisonCompleted`) | `Simple` | `WaitForTextToAppearSimple` |
| The renamed overload returns a framework/object type via a single `out`, where the other returns the same data as scalar outputs | `As<Type>` | `GetBoundingRectangleAsRectangle`, `GetPositionAsPoint`, `TryGetStatusAsServiceControllerStatus` |

Do not invent a third pattern without a reason — consistency across the
suite lets a reader predict a renamed method's new name on sight.

### Which overload keeps the plain name

- The overload with the extra disambiguating `out bool` (a `querySucceeded`,
  `timedOut`, `wasAlready*`, or `comparisonCompleted` parameter) keeps the
  plain name; its sibling without that output becomes `...Simple`.
- The overload returning scalar/primitive outputs keeps the plain name; the
  sibling returning a single framework/object type (`Rectangle`, `Point`,
  `ServiceControllerStatus`, `EventData`, …) becomes `...As<Type>`.

### Exceptions

A component may use a different, already-established convention instead,
provided it is applied consistently within that project and documented in
its own README. `CommandLineUtils` is the one such exception in this
repository: its newer, more Pega-usable overloads carry a `Flat` suffix
(`RunFlat`, `RunShellCommandFlat`) while the older overloads keep the plain
name — the reverse of the convention above. Do not "fix" that project to
match this standard without an explicit decision to do so; it is a
deliberate, documented departure, not an oversight.

## Renaming checklist

Renaming a method to resolve a collision touches more than its declaration.
For each rename:

1. Rename the method declaration, its XML `<summary>`/`<param>`/`<returns>`
   doc, and its `[Description]` attribute.
2. Update every internal call site that invokes the renamed method,
   including from unrelated methods elsewhere in the same file (a helper
   like `HighlightElement` or `GetChildrenSummaryJson` calling a renamed
   geometry getter is easy to miss with a narrow search).
3. Update every `<see cref="...">` doc reference to the old signature,
   including references inside *other* methods' doc comments and inside
   shared private helpers (e.g. `StartAndWait`/`StopAndWait` in
   `ServiceUtils`).
4. Update the failure-message string passed to
   `NeverThrowsGuard.Failure("<OperationName>", ex)` inside the renamed
   method's catch block. Leave it unchanged on the sibling overload that
   kept its name.
5. Update the test project: every call site using the old name, and the
   test method names themselves where they name the method under test
   (`MethodName_Scenario_Expectation`).
6. Update the component's `README.md` method table and any prose in
   "Notes & Caveats" that names the method — including a note that merely
   mentions the method without a full signature.
7. Update every worked example in the component's `Documentation/*.md`
   pages that calls the renamed overload. Leave calls to the sibling
   overload (the one that kept its name) unchanged.
8. Check other components' documentation for cross-references — a
   producer/consumer example in one component's docs can call a method
   defined in another component (e.g. `WindowUtils`' README referencing
   `MouseUtils.GetWindowBounds`). Confirm whether the referenced overload
   was the one renamed before editing.
9. Append a "## Addendum: naming ambiguity fix" section to the component's
   Pega usability review doc in
   [`project-docs/pega-usability-reviews/`](../pega-usability-reviews/),
   listing each old-name → new-name pair and the reason.
10. Rebuild and run the project's test suite before treating the rename as
    complete. A clean build after step 2-4 does not guarantee steps 5-8
    were caught — grep for the bare old method name (not just calls with
    parentheses) across the component's `.cs`, `README.md`, and
    `Documentation/*.md` files as a final sweep.

## Detecting violations

Extract every public method signature (`grep -n "public .* MethodName"` per
component, or a broader sweep across all components) and group by method
name. For each group with more than one overload, compare the ordered list
of non-`out`/non-`ref` parameter types only — ignore parameter names,
default values, and `out`/`ref` parameters entirely. Two overloads whose
remaining type lists match exactly are a violation.

Run this check whenever a new overload is added to an existing method name,
not just as a one-off sweep — the violations fixed across PRs #49-#55 were
all introduced by earlier, well-intentioned additive overloads (adding a
`querySucceeded` or `timedOut` output, or a scalar-coordinate overload)
that nobody checked against this rule at the time.

## Completion criteria

A component conforms when:

- No two public overloads of the same method name share an identical
  ordered list of non-`out` parameter types.
- Every rename follows the naming convention above (or the component's
  documented, deliberate exception).
- The renaming checklist has been applied in full — no stale references to
  the old name remain in source, tests, or docs.
- The component's Pega usability review doc records the rename and its
  rationale.
