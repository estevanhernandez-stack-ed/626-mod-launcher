using Xunit;

namespace ModManager.Tests.Manifest;

// Tests in this collection mutate the process-global EffectiveManifest static (SetRemote).
// DisableParallelization keeps them from running concurrently with any other collection —
// so the parity tests (KnownEnginesTests etc.) never observe a transient remote and stay
// byte-for-byte unmodified. Every test in here MUST reset EffectiveManifest.SetRemote(null).
[CollectionDefinition("ManifestState", DisableParallelization = true)]
public class ManifestStateCollection { }
