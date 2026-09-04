# LocalQueueUtils examples

- [Setup and lifetime](Setup.md)
- [Adding work](AddingWork.md)
- [Processing and recovery](Processing.md)

The intended boundary is simple: Robot Manager owns the parent business work;
LocalQueueUtils owns a variable number of local child items discovered while
that work is processed.
