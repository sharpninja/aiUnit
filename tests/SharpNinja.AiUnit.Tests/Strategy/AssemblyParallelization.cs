using Xunit;

// Disable cross-collection parallel execution for the whole test assembly.
// Several test classes (strategy resolver + xUnit attribute integration)
// mutate process-wide AIUNIT_* environment variables; running them in
// parallel produces flaky results. Tests within a single collection still
// run sequentially per xUnit default, so this attribute only affects
// inter-collection scheduling.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
