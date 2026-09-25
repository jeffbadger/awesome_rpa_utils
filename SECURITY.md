# Security Policy

## Supported versions

Security fixes are made against the **latest release** only (see the
[Releases](https://github.com/jeffbadger/awesome_rpa_utils/releases) page). If you
are on an older release, please upgrade to the latest one before reporting, and
check that the problem still reproduces.

## Reporting a vulnerability

**Please do not open a public issue, pull request or discussion for a security
problem.** Report it privately instead:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability** (GitHub private vulnerability reporting).
3. Describe the problem.

Useful things to include:

- the component and version (for example `ArchiveAutomation` 0.3.26) and the
  Windows and .NET versions you ran it on;
- what you expected and what happened, with the smallest steps or input that
  reproduce it (a crafted archive, a file path, a JSON document and so on);
- the impact as you understand it: what an attacker could read, write, run or
  bypass, and what they would need in order to do it.

This is a volunteer-maintained project. I will read every report and respond as
soon as I reasonably can, keep you informed while I investigate, and credit you in
the fix and release notes if you would like that. I can't promise a fixed
response time or a fix date.

## What is in scope

The components in `src/` run inside an automation on a Windows desktop and act
with that automation's own permissions. Reports about the following are welcome:

- **Bypassing a safeguard a component advertises**, for example zip-slip or
  zip-bomb protection in `ArchiveUtils`, path validation in `LocalQueueUtils` or
  `StateMachineUtils` persistence, or the size and depth limits in the JSON
  components.
- **Untrusted input causing harm beyond the caller's intent**, for example a
  crafted file, archive, JSON document or saved state that leads to writing
  outside the intended folder, running code, reading data it should not, or
  exhausting memory or disk.
- **Data exposure**, such as a component writing a secret to a log, a saved file
  or an event where the documentation says it will not.
- The build and release scripts and the GitHub workflows in this repository.

## What is out of scope

- Behavior that is documented and inherent to a component's purpose. For
  example `CommandLineUtils` runs the commands you give it, and the input
  components send the keystrokes and clicks you ask for. Passing untrusted text
  into such a method is the automation author's responsibility.
- Problems that require an attacker to already control the machine or the
  automation's account.
- Vulnerabilities in third-party dependencies with no effect on this project
  (please report those upstream).

## Handling secrets

Several components persist state to disk in plain text (for example the
`StateMachineUtils` context and `LocalQueueUtils` payloads). Do not put
passwords, tokens or personal data in them; use Pega Robot Studio's credential
management instead. If you find a place where a secret is exposed despite that
guidance, please report it as above.
