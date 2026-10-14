using NightInjection.Installation;
using NightInjection.UI;
using System.Buffers.Binary;

namespace NightInjection.Tests;

public sealed class SteamBridgeDeploymentTests
{
    private static byte[] RealPayload() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "fixtures", SteamBridgeDeployment.FileName));

    [Fact]
    public void OfficialCompilerPackageHasABootstrapBeforeItsStarHeader()
    {
        var bytes = RealPayload();
        Assert.False(bytes.AsSpan(0, 4).SequenceEqual("STAR"u8));
        var shimLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        Assert.True(bytes.AsSpan(4 + (int)shimLength, 4).SequenceEqual("STAR"u8));
        SteamBridgeDeployment.ValidatePayload(bytes);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("shim")]
    [InlineData("header")]
    [InlineData("section")]
    [InlineData("truncated")]
    public void CorruptCompilerPackagesAreRejectedBeforeInstallation(string corruption)
    {
        using var environment = new TestEnvironment();
        environment.CreateFile("Steam/millennium/lib/millennium.dll");
        environment.CreateFile("Steam/millennium/bin/millennium.luavm64.exe");
        var bytes = RealPayload();
        var offset = 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        switch (corruption)
        {
            case "length": BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.MaxValue); break;
            case "shim": bytes[10] ^= 1; break;
            case "header": bytes[offset] ^= 1; break;
            case "section": bytes[^1] ^= 1; break;
            case "truncated": bytes = bytes[..(offset + 15)]; break;
        }
        var payload = environment.CreateFile("payload.star");
        File.WriteAllBytes(payload, bytes);
        Assert.Throws<InvalidDataException>(() => SteamBridgeDeployment.Install(payload, environment.Steam, environment.Data));
        Assert.False(File.Exists(SteamBridgeDeployment.Target(environment.Steam)));
        Assert.False(File.Exists(Path.Combine(environment.Data, SteamBridgeDeployment.ReceiptName)));
    }
    [Fact]
    public void InstallUpgradeAndRemovePreserveOtherPluginsAndSettings()
    {
        using var environment = new TestEnvironment();
        environment.CreateFile("Steam/millennium/lib/millennium.dll");
        environment.CreateFile("Steam/millennium/bin/millennium.luavm64.exe");
        var payload = environment.CreateFile("payload.star");
        var original = RealPayload();
        File.WriteAllBytes(payload, original);
        var other = environment.CreateFile("Steam/millennium/plugins/other.star", "other");
        var settings = environment.CreateFile("Steam/millennium/settings.json", "keep");
        SteamBridgeDeployment.Install(payload, environment.Steam, environment.Data);
        Assert.True(File.Exists(SteamBridgeDeployment.Target(environment.Steam)));
        var updated = original.ToArray();
        var headerOffset = 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(updated);
        // A reserved table byte changes the package hash without corrupting section data.
        updated[headerOffset + 12 + 28] = 1;
        File.WriteAllBytes(payload, updated);
        SteamBridgeDeployment.Install(payload, environment.Steam, environment.Data);
        Assert.Equal(updated, File.ReadAllBytes(SteamBridgeDeployment.Target(environment.Steam)));
        Assert.True(SteamBridgeDeployment.Remove(environment.Data));
        Assert.False(File.Exists(SteamBridgeDeployment.Target(environment.Steam)));
        Assert.Equal("other", File.ReadAllText(other));
        Assert.Equal("keep", File.ReadAllText(settings));
    }

    [Fact]
    public void MissingDependencyAndForeignModifiedFilesArePreserved()
    {
        using var environment = new TestEnvironment();
        var payload = environment.CreateFile("payload.star");
        File.WriteAllBytes(payload, RealPayload());
        Assert.Throws<IOException>(() => SteamBridgeDeployment.Install(payload, environment.Steam, environment.Data));
        environment.CreateFile("Steam/millennium/lib/millennium.dll");
        environment.CreateFile("Steam/millennium/bin/millennium.luavm64.exe");
        SteamBridgeDeployment.Install(payload, environment.Steam, environment.Data);
        File.WriteAllText(SteamBridgeDeployment.Target(environment.Steam), "foreign update");
        Assert.Throws<IOException>(() => SteamBridgeDeployment.Install(payload, environment.Steam, environment.Data));
        Assert.False(SteamBridgeDeployment.Remove(environment.Data));
        Assert.Equal("foreign update", File.ReadAllText(SteamBridgeDeployment.Target(environment.Steam)));
    }

    [Fact]
    public void ActivationHandoffIsAtomicValidatedAndBounded()
    {
        using var environment = new TestEnvironment();
        var queue = Path.Combine(environment.Root, "queue");
        ActivationHandoff.Enqueue("night-injection://import?appId=730", queue);
        ActivationHandoff.Enqueue("night-injection://import?appId=evil", queue);
        File.WriteAllText(Path.Combine(queue, "large.pending"), new string('x', 3000));
        File.WriteAllText(Path.Combine(queue, "old.pending"), "night-injection://import?appId=220");
        File.SetLastWriteTimeUtc(Path.Combine(queue, "old.pending"), DateTime.UtcNow.AddHours(-1));
        var request = Assert.Single(ActivationHandoff.DequeueAll(queue));
        Assert.Equal("730", request.AppId);
        Assert.Empty(Directory.EnumerateFiles(queue));
    }
}
