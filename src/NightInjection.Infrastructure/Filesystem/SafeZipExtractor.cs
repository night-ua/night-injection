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
        RejectLinks(destinationRoot);
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
            RejectLinks(target);
            if (!seen.Add(target))
            {
                throw new InvalidDataException($"ZIP contains duplicate output path: {entry.FullName}");
            }

            targets.Add(entry, target);
        }

        Directory.CreateDirectory(destinationRoot);
        var written = new List<string>(targets.Count);
        long actualTotal = 0;
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
            var buffer = new byte[81_920];
            long actualEntry = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                actualEntry = checked(actualEntry + read);
                actualTotal = checked(actualTotal + read);
                if (actualEntry > pair.Key.Length || actualEntry > MaximumEntryBytes || actualTotal > MaximumExpandedBytes)
                    throw new InvalidDataException("ZIP stream exceeds its declared safe size.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            if (actualEntry != pair.Key.Length) throw new InvalidDataException("ZIP stream size does not match its directory entry.");
            written.Add(pair.Value);
        }

        return written;
    }

    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(name)
            || name.Length > 240 || name.Any(static c => c < 32)
            || segments.Any(static part => part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(['<', '>', '"', '|', '?', '*', ':']) >= 0
                || System.Text.RegularExpressions.Regex.IsMatch(part,
                    @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
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

    private static void RejectLinks(string target)
    {
        for (var path = target; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
        {
            if ((Directory.Exists(path) || File.Exists(path))
                && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("ZIP extraction paths may not contain links or junctions.");
        }
    }
}
