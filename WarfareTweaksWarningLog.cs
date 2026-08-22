using System;
using System.Collections.Generic;

namespace WarfareTweaks;

internal static class WarfareTweaksWarningLog
{
    private static readonly HashSet<string> Reported = new(StringComparer.OrdinalIgnoreCase);

    internal static void LogOnce(string key, string message)
    {
        if (!string.IsNullOrWhiteSpace(key) && Reported.Add(key))
        {
            WarfareTweaksPlugin.ModLogger.LogWarning(message);
        }
    }
}
