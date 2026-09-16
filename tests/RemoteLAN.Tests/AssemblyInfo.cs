using Xunit;

// Disable parallel test execution across test classes to prevent DXGI Desktop Duplication resource contention
[assembly: CollectionBehavior(DisableTestParallelization = true)]
