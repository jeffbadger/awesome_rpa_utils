# Set

Runtime setters never create definitions. Use `SetValue` when incoming data is
text from OCR, an application control, or a configuration source; it parses the
text using the existing declaration. Use `SetDecimal`, `SetBoolean`, and the
other typed methods when the source already supplies a typed value.

`SetFromJsonObject` maps property names to bag names. `SetFromDataRow` maps
column names from one row. Both validate every update before publishing any of
them, and unknown names fail by default.
