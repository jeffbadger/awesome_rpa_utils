# Reset

Initialize definitions once, process one transaction, then call `ResetValues`
before the next transaction. Mutable entries return to their declared defaults,
read-only entries are preserved, and write-once entries become assignable again.
The schema remains sealed and ready.
