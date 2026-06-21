using System;
using System.Collections.Generic;

namespace WarfareTweaks;

internal static class WarfareTweaksWarningLog
{
    private static readonly HashSet<string> Reported = new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryMarkReported(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && Reported.Add(key);
    }
}
