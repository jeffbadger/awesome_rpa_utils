# CommandLineUtils Pega API Usability Review

## Summary

The common captured-command methods previously exposed several types that are
difficult to construct or consume in Pega: `CommandResult`,
`IDictionary<string,string>`, `Encoding`, and `string[]`. `RunElevated` was
already the only completely scalar method. Each proxy-dependent method now
also has a distinctly-named, fully-scalar counterpart.

## Method review

| Method | Rating | Assessment |
|---|---|---|
| `Run` | Proxy-dependent; scalar counterpart added | Required inputs are scalar, so a basic process can be launched, but the original's output is wrapped in `CommandResult` and its configuration needs `IDictionary`/`Encoding`. `RunFlat` now provides a fully scalar counterpart (flattened result outputs, `NAME=VALUE` environment text, an encoding name string); the original remains for .NET callers. |
| `RunShellCommand` | Adapter needed; scalar counterpart added | In addition to the `CommandResult`, dictionary, and encoding problems, its `allowedPrograms` guardrail required `string[]`. `RunShellCommandFlat` now provides a fully scalar counterpart, including a comma-separated allowlist; the guardrail-not-sandbox caveat is unchanged and documented on both. |
| `RunElevated` | Direct ports; unattended limitation | Every port is scalar and results are already flattened. UAC consent normally appears on the secure desktop and cannot be driven by ordinary desktop automation, so this is unsuitable for unattended execution unless elevation is pre-authorized/configured outside the robot. No change needed. |
| `StartFireAndForget` | Direct common path; scalar counterpart added | File, arguments, directory, process ID, and message are scalar when `environmentVariables` is omitted. `StartFireAndForgetWithEnvironment` now provides a `NAME=VALUE`-text counterpart for the case where it isn't. |

## Recommended scalar surfaces

1. **Done.** `RunFlat`/`RunShellCommandFlat` return `exitCode`,
   `standardOutput`, `standardError`, `timedOut`, and `outputTruncated` as
   individual outputs.
2. **Done.** Output encoding is accepted as an encoding name string
   (`outputEncodingName`, resolved via `Encoding.GetEncoding(string)`, e.g.
   `"utf-8"`) instead of `System.Text.Encoding`. An unrecognized name returns
   `false` with a message before anything runs.
3. **Done.** Environment overrides are accepted as newline-delimited
   `NAME=VALUE` text (`environmentVariablesText`). A line missing `=` or with
   an empty name returns `false` with a message before anything runs.
4. **Done.** The shell allowlist is accepted as a comma-separated string
   (`allowedProgramsCsv`) on `RunShellCommandFlat`. An empty/null CSV
   preserves `RunShellCommand`'s "no guardrail" meaning (not "nothing is
   allowed").
5. **Done.** The new methods (`RunFlat`, `RunShellCommandFlat`,
   `StartFireAndForgetWithEnvironment`) are distinctly named, not overloads
   of `Run`/`RunShellCommand`/`StartFireAndForget` - a Pega designer's method
   picker never has to disambiguate two same-named methods by parameter list
   alone. The original object-based methods are retained unchanged for .NET
   callers.

## Additional concerns

- **Addressed.** A `true` method return only means the process ran; the
  automation must separately evaluate timeout and exit code. The flattened
  outputs keep `timedOut` as a separate output from `exitCode`, preserving
  that distinction rather than collapsing it into a sentinel exit code.
- `RunShellCommand`/`RunShellCommandFlat` execute a single command string
  through `cmd.exe`; dynamic or untrusted content remains a command-injection
  risk even with a program allowlist. Unchanged - already documented on both
  methods' remarks and in the README.
- Arguments are still supplied as one string, so quoting and escaping remain
  the caller's responsibility. The suggested quoting helper / scalar named
  arguments were left out of scope for this pass (not a numbered
  recommendation; the user asked to proceed with the 5 numbered items only).

## Verdict

`RunElevated` has a Pega-friendly signature but an unattended UAC limitation,
unchanged. `Run`/`RunShellCommand`/`StartFireAndForget` were proxy-dependent
for their captured-result/environment/encoding/allowlist ports; each now has
a distinctly-named, fully-scalar counterpart (`RunFlat`,
`RunShellCommandFlat`, `StartFireAndForgetWithEnvironment`) implementing all
five recommended changes, while the original object-based methods remain
available for .NET callers.
