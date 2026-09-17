# Validation

Call `ValidateRequiredValuesPresent` before a submission or handoff. It returns a
Boolean readiness result plus a JSON list of missing names. Required strings
are missing when null or empty; other types are missing when null.

`GetSnapshotJson` provides names, types, flags, and current values for diagnosis.
Entries marked `sensitive` always appear as `***` in this snapshot.
