# Text Typing

Both overloads return `bool` (success) with an `out string message` explaining why
on failure — never throws, including for a null `text`.

## Type a string at the default speed

```csharp
keyboard.TypeText("user@example.com", out _);
```

## Type slowly, for a field that drops fast keystrokes

```csharp
keyboard.TypeText("slow-field-value", 40, out _);
```

## Type text containing an emoji (non-BMP character)

```csharp
keyboard.TypeText("Approved 👍", out _);
```

Non-BMP characters are sent as a surrogate pair in the canonical order — high
surrogate down, low surrogate down, low surrogate up, high surrogate up — so the
target composes a single character rather than two replacement glyphs.

## Checking why typing failed

```csharp
if (!keyboard.TypeText("user@example.com", out string message))
{
    Console.WriteLine($"Typing failed: {message}");
}
```
