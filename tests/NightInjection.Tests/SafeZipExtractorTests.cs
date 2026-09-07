using System.IO.Compression;
using NightInjection.Infrastructure.Filesystem;

namespace NightInjection.Tests;

public sealed class SafeZipExtractorTests
{
    [Fact]
    public async Task ExtractsSupportedNestedContentInsideDestination()
    {
        using var environment = new TestEnvironment();
        var archive = CreateArchive(environment, ("nested/220.lua", "addappid(220)"), ("220.manifest", "manifest"));
        var destination = Path.Combine(environment.Temp, "extract");

        var files = await new SafeZipExtractor().ExtractAsync(archive, destination);

        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.StartsWith(Path.GetFullPath(destination), file, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("addappid(220)", File.ReadAllText(Path.Combine(destination, "nested", "220.lua")));
    }

    [Theory]
    [InlineData("../outside.lua")]
    [InlineData("folder/../../outside.lua")]
    [InlineData("C:/outside.lua")]
    public async Task RejectsTraversalAndRootedEntries(string entry)
    {
        using var environment = new TestEnvironment();
        var archive = CreateArchive(environment, (entry, "bad"));
        var destination = Path.Combine(environment.Temp, "extract");

        await Assert.ThrowsAsync<InvalidDataException>(() => new SafeZipExtractor().ExtractAsync(archive, destination));
        Assert.False(File.Exists(Path.Combine(environment.Temp, "outside.lua")));
    }

    [Fact]
    public async Task RejectsDuplicateOutputPathsCaseInsensitively()
    {
        using var environment = new TestEnvironment();
        var archive = CreateArchive(environment, ("a.lua", "one"), ("A.lua", "two"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SafeZipExtractor().ExtractAsync(archive, Path.Combine(environment.Temp, "extract")));
    }

    private static string CreateArchive(TestEnvironment environment, params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(environment.Root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var item = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(item.Open());
            writer.Write(content);
        }

        return path;
    }
}
