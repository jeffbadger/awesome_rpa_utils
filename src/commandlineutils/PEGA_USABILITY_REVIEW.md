# CommandLineUtils Pega API Usability Review

## Summary

The common captured-command methods expose several types that are difficult to
construct or consume in Pega: `CommandResult`, `IDictionary<string,string>`,
`Encoding`, and `string[]`. `RunElevated` is the only completely scalar method.
Basic fire-and-forget use is scalar when its optional environment dictionary is
omitted.

## Method review

| Method | Rating | Assessment |
|---|---|---|
| `Run` | Significant proxy friction | Required inputs are scalar, so a basic process can be launched, but useful output is wrapped in `CommandResult`. Custom environment variables and output encoding require objects with no local producer. Add a flat scalar-result method plus string/enum configuration adapters. |
| `RunShellCommand` | Adapter needed | In addition to the `CommandResult`, dictionary, and encoding problems, its `allowedPrograms` guardrail requires `string[]`. Pega users may be pushed toward leaving this null and running an unrestricted shell command. Add a delimited/JSON allowlist input and emphasize that it remains a guardrail, not a sandbox. |
| `RunElevated` | Direct ports; unattended limitation | Every port is scalar and results are already flattened. UAC consent normally appears on the secure desktop and cannot be driven by ordinary desktop automation, so this is unsuitable for unattended execution unless elevation is pre-authorized/configured outside the robot. |
| `StartFireAndForget` | Direct common path; adapter needed for environment | File, arguments, directory, process ID, and message are scalar when `environmentVariables` is omitted. There is no Pega-friendly way to supply custom environment values. A simple overload would also avoid exposing an unusable optional dictionary port. |

## Recommended scalar surfaces

1. Add a captured-run method returning `exitCode`, `standardOutput`,
   `standardError`, `timedOut`, and `outputTruncated` as individual outputs.
2. Accept output encoding through a small enum, encoding name, or code-page
   integer instead of `System.Text.Encoding`.
3. Accept environment overrides as JSON or newline-delimited `NAME=VALUE` text,
   with validation and clear escaping rules.
4. Accept the shell allowlist as JSON or a carefully parsed delimiter-separated
   string. Do not require Pega to construct `string[]`.
5. Retain the current object-based methods for .NET callers, but expose clearly
   named Pega-oriented methods so overload selection is unambiguous.

## Additional concerns

- A `true` method return only means the process ran; the automation must separately
  evaluate timeout and exit code. Flattened outputs should retain that distinction.
- `RunShellCommand` executes a single command string through `cmd.exe`; dynamic or
  untrusted content remains command-injection risk even with a program allowlist.
- Arguments are supplied as one string, so quoting and escaping remain the caller's
  responsibility. A `string[]` argument API would be safer for .NET but would worsen
  Pega usability; scalar named arguments or a documented quoting helper would be a
  better Pega compromise.

## Verdict

`RunElevated` has a Pega-friendly signature but an unattended UAC limitation.
`Run` is proxy-dependent, and `RunShellCommand` has multiple inputs with no natural
producer. Flattened result outputs and string/enum adapters should be treated as
high-priority changes.
