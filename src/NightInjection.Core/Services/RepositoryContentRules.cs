namespace NightInjection.Core.Services;

public enum RepositoryRule
{
    ProjectLightning,
    SpinAddAppIdOnly,
    RemoveManifestId,
    Unsupported
}

public static class RepositoryContentRules
{
    public const string SignatureLine = "-- Made with love by LightningFast⚡💜";

    public static RepositoryRule Resolve(string repository)
    {
        if (repository.Contains("ProjectLightningManifests", StringComparison.Ordinal))
        {
            return RepositoryRule.ProjectLightning;
        }

        if (repository.Contains("SPIN0ZAi", StringComparison.Ordinal))
        {
            return RepositoryRule.SpinAddAppIdOnly;
        }

        return repository.Contains("dvahana2424-web", StringComparison.Ordinal)
            || repository.Contains("sojorepo", StringComparison.Ordinal)
            || repository.Contains("SteamAutoCracks", StringComparison.Ordinal)
                ? RepositoryRule.RemoveManifestId
                : RepositoryRule.Unsupported;
    }

    public static bool AllowsManifest(RepositoryRule rule) => rule == RepositoryRule.ProjectLightning;

    public static string TransformLua(string source, RepositoryRule rule)
    {
        if (rule == RepositoryRule.ProjectLightning)
        {
            return source;
        }

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        IEnumerable<string> kept = rule switch
        {
            RepositoryRule.SpinAddAppIdOnly =>
                lines.Where(static line => line.TrimStart().StartsWith("addappid(", StringComparison.Ordinal)),
            RepositoryRule.RemoveManifestId =>
                lines.Where(static line => !line.Contains("setManifestid", StringComparison.Ordinal)),
            _ => []
        };

        return string.Join('\n', kept.Append(string.Empty).Append(SignatureLine));
    }
}
