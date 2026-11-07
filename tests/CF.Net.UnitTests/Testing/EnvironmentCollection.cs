using Xunit;

namespace CFNet.UnitTests.Testing;

/// <summary>
/// Tests that mutate process-wide environment variables must not run alongside anything else.
/// </summary>
[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection
{
}
