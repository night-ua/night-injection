using NightInjection.Core.Results;

namespace NightInjection.Core.Models;

public enum SteamImportState
{
    Idle, Connecting, CheckingConfiguration, PreparingPackage, Downloading,
    VerifyingPackage, ReadyToApply, ApplyingChanges, Completed, Failed, Cancelled, Unsupported
}

public sealed record SteamBridgeSettings(bool AutomaticApply = false, string ColorMode = "native",
    string CustomColor = "#FF6A00", int Version = 2);

public sealed record SteamImportPreview(string Name, string Destination, long Bytes, bool Overwrite);

public sealed record SteamImportSnapshot(Guid TaskId, string AppId, string GameName, SteamImportState State,
    string Message, long BytesReceived = 0, long? TotalBytes = null, ImportErrorCode? ErrorCode = null,
    string Verification = "Not verified", IReadOnlyList<SteamImportPreview>? Preview = null,
    int FileCount = 0, int OverwriteCount = 0, bool AutomaticRequested = false, long Revision = 0, int PreviewOffset = 0);

public sealed record SteamBridgeRequest(string Op, string? AppId = null, Guid? TaskId = null,
    string? GameName = null, bool Retry = false, bool Confirmed = false, SteamBridgeSettings? Settings = null, int PreviewOffset = 0);

public sealed record SteamBridgeReply(bool Ok, SteamImportSnapshot? Task = null,
    SteamBridgeSettings? Settings = null, string? Error = null, string? Message = null);
