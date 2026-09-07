using NightInjection.Core.Validation;

namespace NightInjection.UI;

internal static class ActivationHandoff
{
    private static string QueueRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "night-injection",
        "activation-queue");

    public static void Enqueue(string activationUri)
    {
        if (!WebsiteImportActivation.TryParse(activationUri, out _))
        {
            return;
        }

        Directory.CreateDirectory(QueueRoot);
        var path = Path.Combine(QueueRoot, $"{Guid.NewGuid():N}.pending");
        File.WriteAllText(path, activationUri);
    }

    public static IReadOnlyList<WebsiteImportRequest> DequeueAll()
    {
        if (!Directory.Exists(QueueRoot))
        {
            return [];
        }

        var requests = new List<WebsiteImportRequest>();
        foreach (var path in Directory.EnumerateFiles(QueueRoot, "*.pending")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
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
