namespace NightInjection.Core.Results;

public record OperationResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> AffectedPaths,
    IReadOnlyList<string> Warnings)
{
    public static OperationResult Success(
        string message,
        IReadOnlyList<string>? affectedPaths = null,
        IReadOnlyList<string>? warnings = null) =>
        new(true, message, affectedPaths ?? [], warnings ?? []);

    public static OperationResult Failure(
        string message,
        IReadOnlyList<string>? affectedPaths = null,
        IReadOnlyList<string>? warnings = null) =>
        new(false, message, affectedPaths ?? [], warnings ?? []);
}

public sealed record ValidationResult(bool IsValid, string Message)
{
    public static ValidationResult Valid(string message = "Valid") => new(true, message);
    public static ValidationResult Invalid(string message) => new(false, message);
}

public sealed record WebsiteImportResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> FilePaths,
    string? TemporaryDirectory)
{
    public static WebsiteImportResult Success(
        IReadOnlyList<string> filePaths,
        string temporaryDirectory,
        string message) =>
        new(true, message, filePaths, temporaryDirectory);

    public static WebsiteImportResult Failure(string message) =>
        new(false, message, [], null);
}
