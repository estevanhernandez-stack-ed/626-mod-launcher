using Xunit;

namespace ModManager.Tests.Mcp;

// Tests in this collection point the process-global ModManager.Mcp.McpConfig.DataRoot static at a
// throwaway registry. Two classes doing that in parallel race on the same static: one test's setup can
// overwrite another's DataRoot mid-run, so a lookup for a game the first test just registered comes back
// "no game with id ..." — not because the write failed, but because a different test's registry was live
// at read time. Every test class that assigns McpConfig.DataRoot (directly, or via a helper like
// FromSoftFixture.SeedRegistry) MUST carry [Collection("McpDataRoot")], so xUnit runs them sequentially
// against each other instead of concurrently.
[CollectionDefinition("McpDataRoot")]
public class McpDataRootCollection { }
