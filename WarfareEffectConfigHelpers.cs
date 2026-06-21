using System;
using System.Linq;

namespace WarfareTweaks;

internal static class WarfareEffectConfigHelpers
{
    internal static bool IsWarfareEffectConfig(EffectBehaviorConfig effectConfig)
    {
        string type = effectConfig.Type?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(type) ||
               string.Equals(type, "warfare", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "chainLightning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "chainLightningMistlands", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasPrefabAssignment(EffectBehaviorConfig effectConfig, string prefabName)
    {
        if (effectConfig.Prefabs == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        return effectConfig.Prefabs.Keys.Any(configuredPrefabName =>
            string.Equals(configuredPrefabName?.Trim(), prefabName, StringComparison.OrdinalIgnoreCase));
    }
}
