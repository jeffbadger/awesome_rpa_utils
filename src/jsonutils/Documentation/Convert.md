# Convert

## JSON to XML

```csharp
json.TryConvertJsonToXml("{\"status\":\"closed\",\"priority\":1}", "order", out string xml, out string message);
// xml == "<order><status>closed</status><priority>1</priority></order>"
```

`rootElementName` is always required explicitly — Newtonsoft's underlying
converter can sometimes infer a root from JSON shaped as a single
top-level property, but this component always requires the name for
predictability.

## XML to JSON

```csharp
json.TryConvertXmlToJson("<order><status>closed</status><priority>1</priority></order>", out string convertedJson, out message);
// convertedJson == "{\"order\":{\"status\":\"closed\",\"priority\":\"1\"}}"
```

`TryConvertXmlToJson` rejects any XML containing a DOCTYPE declaration
(DTD), returning `False` with a message — this component has no legitimate
need to support DTDs for JSON-conversion input, and parsing untrusted XML
with DTDs enabled is a known XXE/entity-expansion security risk.
