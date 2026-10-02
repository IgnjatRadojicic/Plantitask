using System.Diagnostics.CodeAnalysis;

namespace Plantitask.Core.Validation;

public static class TimeZoneRules
{
    /// <summary>
    /// Resolves a client supplied zone id. Only IANA ids pass because Postgres and the browser
    /// understand nothing else, even though .NET on Windows also resolves Windows ids.
    /// Store zone.Id rather than the input since lookup ignores case and Id is the canonical form.
    /// </summary>
    public static bool TryResolve(string? timeZoneId, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;

        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) || !found.HasIanaId)
            return false;

        zone = found;
        return true;
    }
}
