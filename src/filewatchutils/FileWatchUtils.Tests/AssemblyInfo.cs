using Xunit;

// These tests depend on real time (background writers, polling waits, file-system watchers). xunit runs
// test classes in parallel by default, and one class blocking pool threads (the concurrent-claim tests)
// can starve another class's background task for hundreds of milliseconds, which is longer than the
// windows these tests measure. Run them one at a time so a timing test only ever competes with itself.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
