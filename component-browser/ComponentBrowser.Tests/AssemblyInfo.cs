using Xunit;

// AssemblyInspectorTests creates real MetadataLoadContext instances against real temp
// directories built from this repo's own shared src/bin build output. Running xunit's
// default parallel test execution alongside that caused real, reproducible interference (a
// test that passes every time in isolation started failing - components not found - only when
// run concurrently with the rest of this class) - confirmed for real while writing these
// tests, not a hypothetical. Disabling parallelization for this whole assembly is the safe,
// simple fix; this project is small enough that sequential execution costs nothing that matters.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
