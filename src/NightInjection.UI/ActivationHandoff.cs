using NightInjection.Core.Validation;

namespace NightInjection.UI;

internal static class ActivationHandoff
{
    private static string QueueRoot => Path.Combine(
        Environment.GetEnvironmentVariable("NIGHT_INJECTION_DATA_ROOT") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "night-injection"),
        "activation-queue");

    public static void Enqueue(string activationUri, string? directory = null)
    {
        if (!WebsiteImportActivation.TryParse(activationUri, out _))
        {
            return;
        }

        var root = directory ?? QueueRoot;
        Directory.CreateDirectory(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) return;
        var path = Path.Combine(root, $"{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, activationUri);
        File.Move(path, Path.ChangeExtension(path, ".pending"));
    }

    public static IReadOnlyList<WebsiteImportRequest> DequeueAll(string? directory = null)
    {
        var root = directory ?? QueueRoot;
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            return [];
        }

        var requests = new List<WebsiteImportRequest>();
        foreach (var path in Directory.EnumerateFiles(root, "*.pending")
                     .OrderBy(File.GetLastWriteTimeUtc).Take(64))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2048 || info.LastWriteTimeUtc < DateTime.UtcNow.AddMinutes(-5)
                    || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var value = File.ReadAllText(path);
                if (value.Length <= 2048 && WebsiteImportActivation.TryParse(value, out var request))
                {
                    requests.Add(request);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            finally
            {
                TryDelete(path);
            }
        }

        return requests;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
