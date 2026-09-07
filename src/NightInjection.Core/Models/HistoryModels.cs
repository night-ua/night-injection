namespace NightInjection.Core.Models;

public sealed record HistoryRecord(
    long Id,
    DateTimeOffset Timestamp,
    string Operation,
    string? AppId,
    string? SourceFile,
    string Result,
    string? Details,
    string? Repository,
    bool DryRun);

public sealed record HistoryQuery(
    string? Search = null,
    string? Operation = null,
    string? Result = null,
    int Limit = 500);

public sealed record NewHistoryRecord(
    DateTimeOffset Timestamp,
    string Operation,
    string? AppId,
    string? SourceFile,
    string Result,
    string? Details,
    string? Repository,
    bool DryRun = false);
