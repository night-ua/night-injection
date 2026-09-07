using System.Globalization;

namespace NightInjection.Core.Validation;

public static class AppIdValidator
{
    public static bool IsValid(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        var value = appId.Trim();
        return value.All(static character => character is >= '0' and <= '9')
            && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0;
    }

    public static string Normalize(string? appId)
    {
        if (!IsValid(appId))
        {
            throw new ArgumentException("AppID must be a positive ASCII numeric value.", nameof(appId));
        }

        return appId!.Trim();
    }
}
