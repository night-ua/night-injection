namespace NightInjection.Core.Validation;

public sealed record WebsiteImportRequest(string AppId, Uri Origin);

public static class WebsiteImportActivation
{
    public const string Scheme = "night-injection";
    public static readonly Uri DefaultOrigin = new("https://www.darkdevil.space/");

    public static bool TryParse(string? value, out WebsiteImportRequest request)
    {
        request = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim().Trim('"'), UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("import", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.IsDefaultPort)
        {
            return false;
        }

        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.None);
        const string appIdPrefix = "appId=";
        if (parameters.Length is < 1 or > 2
            || !parameters[0].StartsWith(appIdPrefix, StringComparison.Ordinal)
            || parameters[0].Length == appIdPrefix.Length)
        {
            return false;
        }

        var candidate = parameters[0][appIdPrefix.Length..];
        if (!AppIdValidator.IsValid(candidate) || !uint.TryParse(candidate, out _))
        {
            return false;
        }

        var origin = DefaultOrigin;
        if (parameters.Length == 2)
        {
            const string originPrefix = "origin=";
            if (!parameters[1].StartsWith(originPrefix, StringComparison.Ordinal)
                || parameters[1].Length == originPrefix.Length)
            {
                return false;
            }

            string decodedOrigin;
            try
            {
                decodedOrigin = Uri.UnescapeDataString(parameters[1][originPrefix.Length..]);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!TryNormalizeOrigin(decodedOrigin, out origin))
            {
                return false;
            }
        }

        request = new WebsiteImportRequest(candidate, origin);
        return true;
    }

    private static bool TryNormalizeOrigin(string value, out Uri origin)
    {
        origin = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || candidate.AbsolutePath != "/"
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        var production = candidate.Scheme == Uri.UriSchemeHttps
            && candidate.IsDefaultPort
            && (candidate.Host.Equals("darkdevil.space", StringComparison.OrdinalIgnoreCase)
                || candidate.Host.Equals("www.darkdevil.space", StringComparison.OrdinalIgnoreCase));
        if (production)
        {
            origin = DefaultOrigin;
            return true;
        }

        var local = candidate.Scheme == Uri.UriSchemeHttp
            && candidate.Port is > 0 and <= 65535
            && (candidate.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || candidate.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
        if (!local)
        {
            return false;
        }

        origin = new Uri($"http://{candidate.Host}:{candidate.Port}/");
        return true;
    }
}
