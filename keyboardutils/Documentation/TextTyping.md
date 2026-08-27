# Text Typing

## Type a string at the default speed

```csharp
keyboard.TypeText("user@example.com");
```

## Type slowly, for a field that drops fast keystrokes

```csharp
keyboard.TypeText("slow-field-value", 40);
```

## Type text containing an emoji (non-BMP character)

```csharp
keyboard.TypeText("Approved 👍");
```
