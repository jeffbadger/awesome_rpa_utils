#!/usr/bin/env python3
"""Mechanically add the repository never-throws boundary to bool/out-string APIs."""

from pathlib import Path
import re


FILES = [
    "src/mouseutils/MouseUtils.cs",
    "src/keyboardutils/KeyboardUtils.cs",
    "src/windowutils/WindowUtils.cs",
    "src/dialogutils/DialogUtils.cs",
    "src/screencaptureutils/ScreenCaptureUtils.cs",
    "src/ocrutils/OcrUtils.cs",
    "src/uiautomationutils/UIAutomationUtils.cs",
    "src/commandlineutils/CommandLineUtils.cs",
    "src/serviceutils/ServiceUtils.cs",
    "src/eventutils/EventUtils.cs",
    "src/eventutils/WaitMethods.cs",
]


def matching(text: str, start: int, opening: str, closing: str) -> int:
    depth = 0
    state = "code"
    i = start
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ""
        if state == "code":
            if c == '"':
                state = "string"
            elif c == "'":
                state = "char"
            elif c == "/" and n == "/":
                state = "line"
                i += 1
            elif c == "/" and n == "*":
                state = "block"
                i += 1
            elif c == opening:
                depth += 1
            elif c == closing:
                depth -= 1
                if depth == 0:
                    return i
        elif state == "string":
            if c == "\\":
                i += 1
            elif c == '"':
                state = "code"
        elif state == "char":
            if c == "\\":
                i += 1
            elif c == "'":
                state = "code"
        elif state == "line":
            if c == "\n":
                state = "code"
        elif state == "block" and c == "*" and n == "/":
            state = "code"
            i += 1
        i += 1
    raise ValueError(f"No matching {closing} at {start}")


def transform(path: Path) -> int:
    text = path.read_text()
    matches = list(re.finditer(r"(?m)^(?P<indent>\s*)public\s+bool\s+(?P<name>\w+)\s*\(", text))
    edits = []
    count = 0
    for match in matches:
        open_paren = text.find("(", match.start())
        close_paren = matching(text, open_paren, "(", ")")
        parameters = text[open_paren + 1:close_paren]
        string_out = re.search(r"\bout\s+string\s+(\w+)", parameters)
        if not string_out:
            continue
        message_name = string_out.group(1)

        cursor = close_paren + 1
        while cursor < len(text) and text[cursor].isspace():
            cursor += 1
        if cursor >= len(text) or text[cursor] != "{":
            # Expression-bodied forwarding methods inherit the guarded callee.
            continue
        close_brace = matching(text, cursor, "{", "}")
        body = text[cursor + 1:close_brace]
        if "NeverThrowsGuard.IsRecoverable" in body:
            continue

        out_names = []
        for out_match in re.finditer(r"\bout\s+[\w.<>?,\[\]]+\s+(\w+)", parameters):
            name = out_match.group(1)
            if name not in out_names:
                out_names.append(name)

        indent = match.group("indent")
        inner = indent + "    "
        deeper = indent + "        "
        initializers = "".join(f"\n{inner}{name} = default;" for name in out_names)
        prefix = initializers + f"\n{inner}try\n{inner}{{"
        suffix = (
            f"\n{inner}}}\n"
            f"{inner}catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))\n"
            f"{inner}{{\n"
            f"{deeper}{message_name} = NeverThrowsGuard.Failure(\"{match.group('name')}\", ex);\n"
            f"{deeper}return false;\n"
            f"{inner}}}\n{indent}"
        )
        edits.append((cursor + 1, prefix))
        edits.append((close_brace, suffix))
        count += 1

    for offset, insertion in sorted(edits, reverse=True):
        text = text[:offset] + insertion + text[offset:]
    path.write_text(text)
    return count


root = Path(__file__).resolve().parents[1]
total = 0
for relative in FILES:
    changed = transform(root / relative)
    print(f"{relative}: guarded {changed} methods")
    total += changed
print(f"total: guarded {total} methods")
