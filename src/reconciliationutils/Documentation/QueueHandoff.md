# Queue hand-off

A common use: reconcile, then turn each exception into a work item for a person or a later step. The component does
not write queues itself; it hands you scalars, and you add the item with the tool you already use (for example
`LocalQueueUtils.AddJson`).

```csharp
using System.Text.Json;

// Builds the JSON payload for the exception most recently read. Values are copied as-is; the
// component never puts them in messages, so decide here what a queue is allowed to hold.
static string ExceptionPayload(string resultId, string kind, string keyJson, int leftRow, int rightRow, string reason)
{
    using var stream = new System.IO.MemoryStream();
    using (var w = new Utf8JsonWriter(stream))
    {
        w.WriteStartObject();
        w.WriteString("resultId", resultId);
        w.WriteString("kind", kind);
        if (keyJson == null) w.WriteNull("key");                       // an invalid row has no usable key
        else { w.WritePropertyName("key"); w.WriteRawValue(keyJson); } // keyJson is already a JSON array of key parts
        w.WriteNumber("leftRow", leftRow);        // -1 when absent or ambiguous
        w.WriteNumber("rightRow", rightRow);
        w.WriteString("reason", reason);
        w.WriteEndObject();
    }
    return System.Text.Encoding.UTF8.GetString(stream.ToArray());
}
```

```text
ReconcileJson                                   -> exceptionCount
while TryReadNextException.hasItem:
    payload = ExceptionPayload(resultId, kind, keyJson, leftRowIndex, rightRowIndex, reason)
    LocalQueueUtils.AddJson(queuePath, payload, out itemId, out duplicate, out message)
```

`AddJson` also has optional `businessKey`, `priority`, `delaySeconds` and `maximumAttempts` arguments. Passing the
exception's key as `businessKey` makes a re-run idempotent: an active item with the same key is returned as
`duplicate` instead of being added twice. Check the `AddJson` result and `message` in the loop as you would any call.

Notes:

- Result IDs are local to one run. A queue item that must survive a re-run should carry the **key** (`keyJson`), not
  only the ID, and a worker should re-check the record by key.
- Route by `kind`: `OnlyLeft` and `OnlyRight` need a lookup or creation; `Different` needs a correction decision;
  `DuplicateKey` needs a human (call `GetResultJson` and include its members); `InvalidRecord` and
  `InvalidComparison` are data-quality problems in the source.
- To carry field detail, read the inner cursor and add its values (`ruleName`, `leftValueJson`, `rightValueJson`) to
  the payload, or store `GetResultJson` output. Queue payloads then contain your data: apply the same care as for
  the source rows.
- `keyJson` is null for an `InvalidRecord` (it has no usable key); use `leftRow`/`rightRow` to find it in the source.
- Check `exceptionCount` first: 0 means there is nothing to hand off.
