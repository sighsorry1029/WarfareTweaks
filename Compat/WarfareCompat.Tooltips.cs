using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace WarfareTweaks;

internal static partial class WarfareCompat
{
    private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, ItemPrefabNameCacheEntry> ItemPrefabNamesBySharedData = new();
    private static readonly Dictionary<string, List<ConfiguredWarfareEffectLookup>> ConfiguredTooltipEffectsByPrefabName = new(StringComparer.OrdinalIgnoreCase);

    internal static void AppendMissingConfiguredEffectTooltips(ItemDrop.ItemData item, ref string tooltip)
    {
        if (item?.m_shared == null ||
            string.IsNullOrWhiteSpace(tooltip) ||
            !TryResolveItemPrefabName(item, out string prefabName))
        {
            return;
        }

        if (!ConfiguredTooltipEffectsByPrefabName.TryGetValue(prefabName, out List<ConfiguredWarfareEffectLookup>? configuredEffects) ||
            configuredEffects.Count == 0)
        {
            return;
        }

        HashSet<string> nativeTooltipEffectIds = GetNativeTooltipEffectIds(item, prefabName);
        List<string> fallbackBlocks = new();
        foreach (ConfiguredWarfareEffectLookup configuredEffect in configuredEffects)
        {
            string? block = null;
            WarfareBuiltInEffectRegistration registration = configuredEffect.Registration;
            if (!nativeTooltipEffectIds.Contains(registration.Id) &&
                ShouldAppendFallbackTooltip(registration, tooltip))
            {
                block = BuildConfiguredFallbackTooltip(
                    registration,
                    configuredEffect.EffectConfig,
                    prefabName,
                    configuredEffect.PrefabOverride);
            }

            if (!string.IsNullOrWhiteSpace(block) &&
                !fallbackBlocks.Any(existing => string.Equals(existing, block, StringComparison.OrdinalIgnoreCase)))
            {
                fallbackBlocks.Add(block!);
            }
        }

        if (fallbackBlocks.Count == 0)
        {
            return;
        }

        tooltip = tooltip.TrimEnd() + "\n\n" + string.Join("\n", fallbackBlocks);
    }

    private static HashSet<string> GetNativeTooltipEffectIds(ItemDrop.ItemData item, string prefabName)
    {
        HashSet<string> effectIds = new(StringComparer.OrdinalIgnoreCase);
        if (item?.m_shared == null)
        {
            return effectIds;
        }

        AddNativeTooltipEffectIds(item.m_shared.m_equipStatusEffect, effectIds);
        if (!string.IsNullOrWhiteSpace(prefabName) &&
            AppliedAttackStatusEffectOverrides.TryGetValue(prefabName, out WarfareAttackStatusEffectOverrideState? overrideState))
        {
            AddNativeTooltipEffectIds(overrideState.OriginalAttackStatusEffect, effectIds);
        }

        return effectIds;
    }

    private static void AddNativeTooltipEffectIds(StatusEffect? statusEffect, HashSet<string> effectIds)
    {
        if (statusEffect == null)
        {
            return;
        }

        AddNativeTooltipEffectIds(((UnityEngine.Object)statusEffect).name, effectIds);
        AddNativeTooltipEffectIds(statusEffect.m_name, effectIds);
        AddNativeTooltipEffectIds(statusEffect.m_tooltip, effectIds);
    }

    private static void AddNativeTooltipEffectIds(string value, HashSet<string> effectIds)
    {
        string key = NormalizeStatusEffectKey(value);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (key.StartsWith("SE_AdrenalineLightning", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("adrenaline");
            effectIds.Add("haste");
            return;
        }

        if (key.StartsWith("SE_AdrenalineShred", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("adrenaline");
            effectIds.Add("smasher");
            return;
        }

        if (key.StartsWith("SE_Adrenaline", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("adrenaline");
            return;
        }

        if (key.StartsWith("SE_Bash", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("bash");
            return;
        }

        if (key.StartsWith("SE_Bleeding", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("bleeding");
            return;
        }

        if (key.StartsWith("SE_Crusher", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("SE_Smasher", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("smasher");
            return;
        }

        if (key.StartsWith("SE_Decapitator4", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("decapitator4");
            return;
        }

        if (key.StartsWith("SE_Decapitator5", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("decapitator5");
            return;
        }

        if (key.StartsWith("SE_Executioner", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("executioner");
            return;
        }

        if (key.StartsWith("SE_Haste", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("haste");
            return;
        }

        if (key.StartsWith("SE_Hunger", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("hackAndSlash");
            effectIds.Add("bleedingSecondary");
            return;
        }

        if (key.StartsWith("SE_Impale", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("impale");
            return;
        }

        if (key.StartsWith("SE_Pinned", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("pinning");
            return;
        }

        if (key.StartsWith("SE_Piercer", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("juggernaut");
            effectIds.Add("pierceGreatbowFireAndIce");
            effectIds.Add("piercingGreatbowMistlands");
            effectIds.Add("piercingGreatbowModer");
            effectIds.Add("piercingGreatbowPlains");
            return;
        }

        if (key.StartsWith("SE_RipPiercer", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("juggernaut");
            effectIds.Add("impale");
            return;
        }

        if (key.StartsWith("SE_RipShred", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("decapitator4");
            effectIds.Add("bleedingSecondary");
            return;
        }

        if (key.StartsWith("SE_Rip", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("bleedingSecondary");
            return;
        }

        if (key.StartsWith("SE_Vampirism", StringComparison.OrdinalIgnoreCase))
        {
            effectIds.Add("vampirism");
        }
    }

    private static string NormalizeStatusEffectKey(string value)
    {
        string key = StripRichTextTags(value ?? "").Trim();
        if (key.StartsWith("$", StringComparison.Ordinal))
        {
            key = key.Substring(1).Trim();
        }

        int whitespaceIndex = key.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        if (whitespaceIndex > 0)
        {
            key = key.Substring(0, whitespaceIndex);
        }

        key = key.Replace("_description_TW", "_TW", StringComparison.OrdinalIgnoreCase);
        return key;
    }

    private static bool TryResolveItemPrefabName(ItemDrop.ItemData item, out string prefabName)
    {
        prefabName = "";
        if (item?.m_dropPrefab != null)
        {
            prefabName = NormalizePrefabName(item.m_dropPrefab.name);
            CacheItemPrefabName(item.m_shared, prefabName);
            return !string.IsNullOrWhiteSpace(prefabName);
        }

        if (ObjectDB.instance?.m_items == null || item?.m_shared == null)
        {
            return false;
        }

        if (ItemPrefabNamesBySharedData.TryGetValue(item.m_shared, out ItemPrefabNameCacheEntry cachedEntry))
        {
            prefabName = cachedEntry.PrefabName;
            return !string.IsNullOrWhiteSpace(prefabName);
        }

        foreach (GameObject prefab in ObjectDB.instance.m_items)
        {
            if (prefab == null)
            {
                continue;
            }

            ItemDrop? prefabItem = prefab.GetComponent<ItemDrop>();
            if (prefabItem?.m_itemData?.m_shared == item.m_shared)
            {
                prefabName = NormalizePrefabName(prefab.name);
                CacheItemPrefabName(item.m_shared, prefabName);
                return !string.IsNullOrWhiteSpace(prefabName);
            }
        }

        foreach (GameObject prefab in ObjectDB.instance.m_items)
        {
            if (prefab == null)
            {
                continue;
            }

            ItemDrop? prefabItem = prefab.GetComponent<ItemDrop>();
            if (prefabItem?.m_itemData?.m_shared == null ||
                !string.Equals(prefabItem.m_itemData.m_shared.m_name, item.m_shared.m_name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prefabName = NormalizePrefabName(prefab.name);
            CacheItemPrefabName(item.m_shared, prefabName);
            return !string.IsNullOrWhiteSpace(prefabName);
        }

        return false;
    }

    private static void CacheItemPrefabName(ItemDrop.ItemData.SharedData? sharedData, string prefabName)
    {
        if (sharedData == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return;
        }

        ItemPrefabNamesBySharedData.Remove(sharedData);
        ItemPrefabNamesBySharedData.Add(sharedData, new ItemPrefabNameCacheEntry(prefabName));
    }

    private static string NormalizePrefabName(string prefabName)
    {
        return (prefabName ?? "")
            .Replace("(Clone)", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool ShouldAppendFallbackTooltip(WarfareBuiltInEffectRegistration registration, string tooltip)
    {
        return !TooltipContainsAny(tooltip, GetFallbackTooltipNeedles(registration.Id));
    }

    private static bool TooltipContainsAny(string tooltip, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(tooltip) || needles.Length == 0)
        {
            return false;
        }

        string[] lines = tooltip
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (string line in lines)
        {
            string normalizedLine = StripRichTextTags(line).Trim();
            if (string.IsNullOrWhiteSpace(normalizedLine))
            {
                continue;
            }

            foreach (string needle in needles)
            {
                string normalizedNeedle = StripRichTextTags(needle).Trim();
                if (string.IsNullOrWhiteSpace(normalizedNeedle))
                {
                    continue;
                }

                if (normalizedLine.Equals(normalizedNeedle, StringComparison.OrdinalIgnoreCase) ||
                    normalizedLine.StartsWith(normalizedNeedle + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string StripRichTextTags(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
        {
            return text ?? "";
        }

        char[] buffer = new char[text.Length];
        int count = 0;
        bool inTag = false;
        foreach (char c in text)
        {
            if (c == '<')
            {
                inTag = true;
                continue;
            }

            if (inTag)
            {
                if (c == '>')
                {
                    inTag = false;
                }

                continue;
            }

            buffer[count++] = c;
        }

        return new string(buffer, 0, count);
    }

    private static string BuildConfiguredFallbackTooltip(
        WarfareBuiltInEffectRegistration registration,
        EffectBehaviorConfig effectConfig,
        string prefabName,
        EffectBehaviorOverrideConfig? prefabOverride)
    {
        string effectId = registration.Id;
        string label = GetFallbackTooltipLabel(effectId);

        if (string.Equals(effectId, "adrenaline", StringComparison.OrdinalIgnoreCase))
        {
            return TryResolveAdrenalineRestorePercent(effectConfig, prefabOverride, out float percent)
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipAdrenalineValue,
                    "Restores {0}% of max stamina on the 5th hit against the same target.",
                    FormatNumber(percent)))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Localize(
                    WarfareTweaksLocalization.TooltipAdrenaline,
                    "Restores stamina on the 5th hit against the same target."));
        }

        if (string.Equals(effectId, "haste", StringComparison.OrdinalIgnoreCase))
        {
            float multiplier = ResolveConfiguredHasteMoveSpeedMultiplier(effectConfig, prefabOverride, 1.4f);
            return BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                WarfareTweaksLocalization.TooltipHaste,
                "Increases movement speed on the 6th hit against the same target. Multiplier: {0}x.",
                FormatNumber(multiplier)));
        }

        if (string.Equals(effectId, "vampirism", StringComparison.OrdinalIgnoreCase))
        {
            int? value = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
            return value.HasValue
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipVampirismValue,
                    "Restores {0}% of max health on the 5th hit against the same target.",
                    FormatNumber(value.Value)))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Localize(
                    WarfareTweaksLocalization.TooltipVampirism,
                    "Restores health on repeated hits against the same target."));
        }

        if (IsSourceDamageDotEffect(effectId))
        {
            float? damageFactor = ResolveConfiguredDamageFactor(effectConfig, prefabOverride);
            string damageType = GetSourceDamageDotType(effectId);
            return damageFactor.HasValue
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipDotValue,
                    "Applies {0} damage over time based on the triggering hit. Damage factor: {1}x.",
                    damageType,
                    FormatNumber(damageFactor.Value)))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipDot,
                    "Applies {0} damage over time based on the triggering hit.",
                    damageType));
        }

        if (string.Equals(effectId, "bash", StringComparison.OrdinalIgnoreCase))
        {
            int? value = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
            return value.HasValue
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipBashValue,
                    "Can stagger enemies on non-secondary hits. Chance: {0}%.",
                    FormatNumber(value.Value)))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Localize(
                    WarfareTweaksLocalization.TooltipBash,
                    "Can stagger enemies on non-secondary hits."));
        }

        if (string.Equals(effectId, "executioner", StringComparison.OrdinalIgnoreCase))
        {
            int? value = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
            return value.HasValue
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipExecutionerValue,
                    "Deals bonus damage to targets at or below 25% health. Bonus: {0}%.",
                    FormatNumber(value.Value)))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Localize(
                    WarfareTweaksLocalization.TooltipExecutioner,
                    "Deals bonus damage to low-health targets."));
        }

        if (TryBuildResistanceWeaknessTooltip(effectId, label, out string resistanceTooltip))
        {
            return resistanceTooltip;
        }

        if (IsFixedExtraDamageEffect(effectId))
        {
            int? value = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
            string damageType = GetFixedExtraDamageType(effectId);
            return value.HasValue
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipFixedExtraDamageValue,
                    "Deals {0} extra {1} damage after repeated hits against the same target.",
                    FormatNumber(value.Value),
                    damageType))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipFixedExtraDamage,
                    "Deals extra {0} damage after repeated hits against the same target.",
                    damageType));
        }

        if (string.Equals(effectId, "pinning", StringComparison.OrdinalIgnoreCase))
        {
            int? value = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
            return value.HasValue
                ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                    WarfareTweaksLocalization.TooltipPinningValue,
                    "Slows targets hit for {0}s.",
                    FormatNumber(value.Value)))
                : BuildTooltipLine(label, WarfareTweaksLocalization.Localize(
                    WarfareTweaksLocalization.TooltipPinning,
                    "Slows targets hit for a brief moment."));
        }

        int? fallbackValue = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
        return fallbackValue.HasValue
            ? BuildTooltipLine(label, WarfareTweaksLocalization.Format(
                WarfareTweaksLocalization.TooltipFallbackValue,
                "Warfare effect value: {0}.",
                FormatNumber(fallbackValue.Value)))
            : BuildTooltipLine(label, WarfareTweaksLocalization.Localize(
                WarfareTweaksLocalization.TooltipFallback,
                "Warfare effect assigned by WarfareTweaks."));
    }

    private static string BuildTooltipLine(string label, string body)
    {
        return $"<color=orange>{label}</color>: {body}";
    }

    private static string[] GetFallbackTooltipNeedles(string effectId)
    {
        string label = GetFallbackTooltipLabel(effectId);
        return effectId switch
        {
            "adrenaline" => new[] { label, "$SE_Adrenaline_TW", "SE_Adrenaline_TW" },
            "pinning" => new[] { label, "Pinned", "Pinning" },
            "juggernaut" => new[] { label, "Juggernaut" },
            "hackAndSlash" => new[] { label, "HacknSlash" },
            "smasher" => new[] { label, "Crusher", "$SE_Crusher_TW", "SE_Crusher_TW" },
            "smashAndBash" => new[] { label, "SmashnBash" },
            _ => new[] { label }
        };
    }

    private static string GetFallbackTooltipLabel(string effectId)
    {
        return effectId switch
        {
            "adrenaline" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectAdrenaline, "Adrenaline"),
            "bash" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectBash, "Bash"),
            "bleeding" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectBleeding, "Bleeding"),
            "bleedingSecondary" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectBleedingSecondary, "Bleeding Secondary"),
            "burningSecondary" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectBurningSecondary, "Burning Secondary"),
            "decapitator4" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectDecapitator, "Decapitator"),
            "decapitator5" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectDecapitator, "Decapitator"),
            "executioner" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectExecutioner, "Executioner"),
            "hackAndSlash" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectHackAndSlash, "Hack and Slash"),
            "haste" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectHaste, "Haste"),
            "impale" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectImpale, "Impale"),
            "juggernaut" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectPiercer, "Piercer"),
            "lightningBurst" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectLightningBurst, "Lightning Burst"),
            "pinning" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectPinning, "Pinning"),
            "pierceGreatbowFireAndIce" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectPiercingGreatbow, "Piercing Greatbow"),
            "piercingGreatbowMistlands" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectPiercingGreatbow, "Piercing Greatbow"),
            "piercingGreatbowModer" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectPiercingGreatbow, "Piercing Greatbow"),
            "piercingGreatbowPlains" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectPiercingGreatbow, "Piercing Greatbow"),
            "smasher" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectSmasher, "Smasher"),
            "smashAndBash" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectSmashAndBash, "Smash and Bash"),
            "bludgeoner" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectBludgeoner, "Bludgeoner"),
            "vampirism" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectVampirism, "Vampirism"),
            _ => SplitCamelCase(effectId)
        };
    }

    private static bool IsSourceDamageDotEffect(string effectId)
    {
        return effectId is "bleeding" or "bleedingSecondary" or "burningSecondary" or "impale";
    }

    private static string GetSourceDamageDotType(string effectId)
    {
        return effectId switch
        {
            "burningSecondary" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageFire, "fire"),
            _ => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamagePierce, "pierce")
        };
    }

    private static float? ResolveConfiguredDamageFactor(EffectBehaviorConfig effectConfig, EffectBehaviorOverrideConfig? prefabOverride)
    {
        return prefabOverride?.DamageFactor ?? effectConfig.DamageFactor;
    }

    private static bool TryBuildResistanceWeaknessTooltip(string effectId, string label, out string tooltip)
    {
        tooltip = "";
        (string damageType, int hits, float window)? details = effectId switch
        {
            "decapitator4" => ("slash", 4, 6f),
            "decapitator5" => ("slash", 5, 6f),
            "hackAndSlash" => ("slash", 6, 2.5f),
            "smasher" => ("blunt", 6, 6f),
            "smashAndBash" => ("blunt", 6, 1f),
            "bludgeoner" => ("blunt", 6, 3f),
            "juggernaut" => ("pierce", 4, 6f),
            _ => null
        };

        if (!details.HasValue)
        {
            return false;
        }

        tooltip = BuildTooltipLine(label, WarfareTweaksLocalization.Format(
            WarfareTweaksLocalization.TooltipResistanceWeakness,
            "Makes {0} damage count as weak on the {1}th hit against the same target within {2}s.",
            LocalizeDamageType(details.Value.damageType),
            details.Value.hits,
            FormatNumber(details.Value.window)));
        return true;
    }

    private static bool IsFixedExtraDamageEffect(string effectId)
    {
        return effectId is "lightningBurst" or
            "pierceGreatbowFireAndIce" or
            "piercingGreatbowMistlands" or
            "piercingGreatbowModer" or
            "piercingGreatbowPlains";
    }

    private static string GetFixedExtraDamageType(string effectId)
    {
        return effectId switch
        {
            "lightningBurst" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageLightning, "lightning"),
            "piercingGreatbowModer" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageFrost, "frost"),
            _ => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamagePierce, "pierce")
        };
    }

    private static string LocalizeDamageType(string damageType)
    {
        return damageType switch
        {
            "blunt" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageBlunt, "blunt"),
            "fire" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageFire, "fire"),
            "frost" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageFrost, "frost"),
            "lightning" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageLightning, "lightning"),
            "pierce" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamagePierce, "pierce"),
            "slash" => WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.DamageSlash, "slash"),
            _ => damageType
        };
    }

    private static string SplitCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WarfareTweaksLocalization.Localize(WarfareTweaksLocalization.EffectFallback, "Warfare Effect");
        }

        string result = "";
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(value[i - 1]))
            {
                result += " ";
            }

            result += i == 0 ? char.ToUpperInvariant(c) : c;
        }

        return result;
    }

    private static float ResolveConfiguredHasteMoveSpeedMultiplier(
        EffectBehaviorConfig effectConfig,
        EffectBehaviorOverrideConfig? prefabOverride,
        float defaultValue)
    {
        if (prefabOverride?.MoveSpeedMultiplier.HasValue == true)
        {
            return Mathf.Max(0f, prefabOverride.MoveSpeedMultiplier.Value);
        }

        if (!Mathf.Approximately(effectConfig.MoveSpeedMultiplier, 1f))
        {
            return Mathf.Max(0f, effectConfig.MoveSpeedMultiplier);
        }

        return defaultValue;
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }


    private static void AddConfiguredTooltipEffect(string prefabName, ConfiguredWarfareEffectLookup lookup)
    {
        if (!ConfiguredTooltipEffectsByPrefabName.TryGetValue(prefabName, out List<ConfiguredWarfareEffectLookup>? effects))
        {
            effects = new List<ConfiguredWarfareEffectLookup>();
            ConfiguredTooltipEffectsByPrefabName[prefabName] = effects;
        }

        int existingIndex = effects.FindIndex(effect =>
            string.Equals(effect.Registration.Id, lookup.Registration.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            effects[existingIndex] = lookup;
            return;
        }

        effects.Add(lookup);
    }


    private sealed class ItemPrefabNameCacheEntry
    {
        public ItemPrefabNameCacheEntry(string prefabName)
        {
            PrefabName = prefabName;
        }

        public string PrefabName { get; }
    }

}
