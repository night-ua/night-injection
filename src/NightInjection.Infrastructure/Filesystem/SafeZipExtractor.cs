using System.IO.Compression;

namespace NightInjection.Infrastructure.Filesystem;

public sealed class SafeZipExtractor
{
    public const int MaximumEntries = 4_000;
    public const long MaximumEntryBytes = 128L * 1024 * 1024;
    public const long MaximumExpandedBytes = 512L * 1024 * 1024;
    public const double MaximumCompressionRatio = 250;

    public async Task<IReadOnlyList<string>> ExtractAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken = default)
    {
        var destinationRoot = Path.GetFullPath(destination);
        var targets = new Dictionary<ZipArchiveEntry, string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException($"ZIP contains more than {MaximumEntries} entries.");
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntry(entry);
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (entry.Length > MaximumEntryBytes || expandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("ZIP expanded size exceeds the safe limit.");
            }

            if (entry.CompressedLength > 0
                && entry.Length / (double)entry.CompressedLength > MaximumCompressionRatio)
            {
                throw new InvalidDataException($"ZIP entry has an unsafe compression ratio: {entry.FullName}");
            }

            var target = ResolveTarget(destinationRoot, entry.FullName);
            if (!seen.Add(target))
            {
                throw new InvalidDataException($"ZIP contains duplicate output path: {entry.FullName}");
            }

            targets.Add(entry, target);
        }

        Directory.CreateDirectory(destinationRoot);
        var written = new List<string>(targets.Count);
        foreach (var pair in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(pair.Value)!);
            await using var source = pair.Key.Open();
            await using var output = new FileStream(
                pair.Value,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            written.Add(pair.Value);
        }

        return written;
    }

    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        if (Path.IsPathRooted(name)
            || name.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static part => part == "..")
            || name.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsafe ZIP entry: {entry.FullName}");
        }

        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if (unixFileType == 0xA000 || windowsAttributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"ZIP links are not allowed: {entry.FullName}");
        }
    }

    private static string ResolveTarget(string destinationRoot, string entryName)
    {
        var normalized = entryName.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(destinationRoot, normalized));
        var prefix = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe ZIP entry: {entryName}");
        }

        return target;
    }
}
