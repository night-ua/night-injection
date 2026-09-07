namespace NightInjection.Core.Models;

public enum InjectionFileType
{
    Lua,
    Manifest
}

public enum InjectionSourceKind
{
    LocalFiles,
    AppIdBundle
}

public sealed record InjectionPlanEntry(
    string SourcePath,
    string SourceDisplayName,
    string DestinationPath,
    InjectionFileType FileType,
    long Size,
    bool WillOverwrite,
    string ValidationMessage = "Ready");

public sealed record InjectionPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public InjectionSourceKind SourceKind { get; init; }
    public string? AppId { get; init; }
    public string? Repository { get; init; }
    public string SteamPath { get; init; } = string.Empty;
    public string? TemporaryDirectory { get; init; }
    public IReadOnlyList<InjectionPlanEntry> Entries { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool IsValid => Errors.Count == 0 && Entries.Count > 0;
    public int OverwriteCount => Entries.Count(static entry => entry.WillOverwrite);
    public long TotalBytes => Entries.Sum(static entry => entry.Size);
}
