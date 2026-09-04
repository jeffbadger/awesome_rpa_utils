# Push

Use the scalar methods for one item or pass a whole source to an atomic bulk
method:

```csharp
stack.PushText(windowTitle, out string message);
stack.PushJsonArray(responseJson, out int pushed, out message);
stack.PushFileReferences(downloadFolder, "*.pdf", false, out pushed, out message);
```

Array elements, lines, and sorted file paths are pushed in source order. The
last one is therefore the first popped. A malformed source, inaccessible path,
missing file, or insufficient capacity leaves the stack unchanged. File
references do not transfer ownership of the files.
