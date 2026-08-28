# Fire and Forget

## Start a long-running background tool without blocking the automation

````csharp
```csharp
int pid = cmd.StartFireAndForget("background-sync.exe");
// The automation continues immediately; background-sync.exe keeps running
// on its own. Use WindowUtils.FindWindowsByProcessId(pid) if you later need
// to find a window it opened.
```
````
