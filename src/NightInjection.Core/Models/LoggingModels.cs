using Microsoft.Extensions.Logging;

namespace NightInjection.Core.Models;

public sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionDetail = null);
