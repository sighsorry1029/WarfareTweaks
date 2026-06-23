using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WarfareTweaks;

internal static class WarfareCompat
{
    internal const string WarfareGuid = "Therzie.Warfare";
    private const string WarfareStatusEffectsNamespace = "Warfare_StatusEffects.StatusEffects";
    private const string WarfareFireAndIceStatusEffectsNamespace = "WarfareFireAndIce_StatusEffects.StatusEffects";
    private const string WarfareUtilsTypeName = WarfareStatusEffectsNamespace + ".WarfareUtils";

    private static readonly Dictionary<string, HashSet<string>> SuppressedEffectsByPrefabName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> ManagedEffectsByPrefabName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ManagedChainLightningPrefabs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<MethodBase, string> PatchedPrefixMethods = new();
    private static readonly Dictionary<MethodBase, string> PatchedAddToItemMethods = new();
    private static readonly Dictionary<string, WarfareAppliedAssignment> AppliedConfiguredAssignments = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WarfareAttackSpawnOverrideState> AppliedAttackSpawnOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WarfareAoeOverrideState> AppliedAoeOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WarfareAttackStatusEffectOverrideState> AppliedAttackStatusEffectOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<StatusEffect, WarfareBleedTuningState> BleedTuningsByStatus = new();
    private static readonly ConditionalWeakTable<StatusEffect, WarfareHasteTuningState> HasteTuningsByStatus = new();
    private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, ItemPrefabNameCacheEntry> ItemPrefabNamesBySharedData = new();
    private static readonly List<WarfareDotSourceDamageContext> ActiveDotSourceDamageContexts = new();
    private static readonly Dictionary<ObjectDB, ObjectDbItemNameCache> ObjectDbItemNameCaches = new();
    private static readonly Dictionary<string, ConfiguredWarfareEffectLookup> ConfiguredEffectsByPrefabAndEffectId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type> LoadedTypesByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, WarfareTargetAccessors> TargetAccessorsByEffectType = new();
    private static readonly Dictionary<Type, WarfareTargetSetAccessors> TargetSetAccessorsByType = new();
    private static readonly int WarfareHasteStackingHash = "Warfare_Haste_Stacking".GetStableHashCode();
    private static readonly int WarfareFireAndIceHasteStackingHash = "WarfareFireAndIce_Haste_Stacking".GetStableHashCode();
    private const float WarfareSourceDamageDotDuration = 10f;
    private const float WarfareSourceDamageDotTickInterval = 1f;
    private static float? _pendingHasteMoveSpeedMultiplier;
    private static bool _allowWarfareAddToItem;
    private static bool _hooksInstalled;
    private static readonly string[] ChainLightningMistlandsDefaultPrefabs =
    {
        "GreatbowDvergr_TW",
        "BastardDvergr_TW",
        "AxeDvergr_TW",
        "BattleaxeDvergr_TW",
        "BattlehammerDvergr_TW",
        "ClaymoreDvergr_TW",
        "FistDvergr_TW",
        "LanceDvergr_TW",
        "MaceDvergr_TW",
        "ThrowAxeDvergr_TW",
        "WarpikeDvergr_TW"
    };

    // Built-in effect defaults drive config suppression and restoration decisions.
    private static readonly WarfareBuiltInEffectRegistration[] BuiltInRegistrations =
    {
        RegisterWarfareAndFireAndIce(
            "adrenaline",
            "Adrenaline",
            new[] { "KnifeViper_TW:10", "FistBlackmetal_TW:14", "FistDvergr_TW:16" },
            new[] { "DualHammerRageHatred_TW:20", "DualKnifeNjord_TW:20", "DualKnifeSurtr_TW:20", "KnifeNjord_TW:20", "KnifeSurtr_TW:20" }),
        RegisterWarfareAndFireAndIce(
            "bash",
            "Bash",
            new[] { "SledgeBonemass_TW:3", "SledgeSilver_TW:2", "SledgeBlackmetal_TW:3", "SledgeDemolisher_TW:4", "SledgeFlametal_TW:5" },
            new[] { "SledgeNjord_TW:6", "SledgeSurtr_TW:6" }),
        new("bleeding", "Bleeding", new[] { "FistSilver_TW:1" }),
        RegisterWarfareAndFireAndIce(
            "bleedingSecondary",
            "BleedingSecondaryAttack",
            new[] { "FistQueen_TW:2", "FistChitin_TW:1" },
            new[] { "DualSwordSkadi_TW:1", "DualSpearSvigaFrekk_TW:1" },
            "bleeding_secondary"),
        RegisterWarfareAndFireAndIce(
            "decapitator4",
            "Decapitator4",
            new[] { "BastardDvergr_TW:100", "BastardFlametal_TW:100", "BattleaxeDvergr_TW:100", "BattleaxeFlametal_TW:100", "DualSwordScimitar_TW:100" },
            new[] { "BastardSurtr_TW:100", "BastardNjord_TW:100", "BattleaxeSurtr_TW:100", "BattleaxeNjord_TW:100", "DualSwordSkadi_TW:100" }),
        RegisterWarfareAndFireAndIce(
            "decapitator5",
            "Decapitator5",
            new[] { "FistFlametal_TW:100", "ThrowAxeDvergr_TW:100" },
            new[] { "ThrowAxeSurtr_TW:100", "ThrowAxeNjord_TW:100", "DualAxeKrom_TW:100" }),
        RegisterWarfareAndFireAndIce(
            "executioner",
            "Executioner",
            new[] { "ClaymoreDvergr_TW:15" },
            new[] { "ClaymoreNjord_TW:20", "ClaymoreSurtr_TW:20", "BattleaxeDragon_TW:25", "VolcanicBlade_TW:25", "GlacierBlade_TW:25", "LightningBlade_TW:25" }),
        new("hackAndSlash", "HacknSlash", new[] { "FistQueen_TW:100" }, "hacknslash", "hacknSlash"),
        new("haste", "Haste", new[] { "KnifeViper_TW", "FistBlackmetal_TW" }),
        RegisterWarfareAndFireAndIce(
            "impale",
            "Impale",
            new[] { "LanceBlackmetal_TW:1", "LanceDvergr_TW:2" },
            new[] { "LanceNjord_TW:1", "LanceSurtr_TW:1", "ClaymoreJotunn_TW:1" }),
        RegisterWarfareAndFireAndIce(
            "juggernaut",
            "Juggernaut",
            new[] { "WarpikeBone_TW:100", "WarpikeElder_TW:100", "WarpikeChitin_TW:100", "WarpikeObsidian_TW:100", "WarpikeBlackmetal_TW:100", "WarpikeDvergr_TW:100", "WarpikeFlametal_TW:100" },
            new[] { "WarpikeNjord_TW:100", "WarpikeSurtr_TW:100", "DualSpearSvigaFrekk_TW:100" }),
        new("pinning", "Pinned", new[] { "GreatbowModer_TW:4" }, "pinned"),
        new("piercingGreatbowMistlands", "PiercingGreatbowMistlands", new[] { "GreatbowDvergr_TW:100" }, "piercing_greatbow_mistlands"),
        new("piercingGreatbowModer", "PiercingGreatbowModer", new[] { "GreatbowModer_TW:100" }, "piercing_greatbow_moder"),
        new("piercingGreatbowPlains", "PiercingGreatbowPlains", new[] { "GreatbowBlackmetal_TW:100" }, "piercing_greatbow_plains"),
        RegisterWarfareAndFireAndIce(
            "smasher",
            "Smasher",
            new[] { "BattlehammerDvergr_TW:100", "BattlehammerElder_TW:100" },
            new[] { "BattlehammerNjord_TW:100", "BattlehammerSurtr_TW:100" }),
        new("vampirism", "Vampirism", new[] { "ScytheVampiric_TW:6" }),
        new(
            "bludgeoner",
            new[]
            {
                new WarfareEffectTypeSpec(WarfareFireAndIceStatusEffectsNamespace, "Bludgeoner", new[] { "DualHammerRageHatred_TW:100" })
            }),
        new(
            "burningSecondary",
            new[]
            {
                new WarfareEffectTypeSpec(WarfareFireAndIceStatusEffectsNamespace, "BurningSecondaryAttack", new[] { "FistSurtr_TW:1" })
            },
            "burning_secondary"),
        new(
            "lightningBurst",
            new[]
            {
                new WarfareEffectTypeSpec(WarfareFireAndIceStatusEffectsNamespace, "LightningBurst", new[] { "DualHammerStormstrike_TW:1", "FistFenrir_TW:1" })
            }),
        new(
            "pierceGreatbowFireAndIce",
            new[]
            {
                new WarfareEffectTypeSpec(WarfareFireAndIceStatusEffectsNamespace, "PierceGreatbow", new[] { "GreatbowNjord_TW:100", "GreatbowSurtr_TW:100" })
            },
            "pierceGreatbow", "piercingGreatbowFireAndIce"),
        new(
            "smashAndBash",
            new[]
            {
                new WarfareEffectTypeSpec(WarfareFireAndIceStatusEffectsNamespace, "SmashnBash", new[] { "FistNjord_TW:100" })
            },
            "smashnBash")
    };

    private static readonly Dictionary<string, WarfareBuiltInEffectRegistration> RegistrationsByEffectId = BuildRegistrationLookup();

    private static WarfareBuiltInEffectRegistration RegisterWarfareAndFireAndIce(
        string id,
        string patchTypeName,
        string[] warfarePrefabSpecs,
        string[] fireAndIcePrefabSpecs,
        params string[] aliases)
    {
        return new WarfareBuiltInEffectRegistration(
            id,
            new[]
            {
                new WarfareEffectTypeSpec(WarfareStatusEffectsNamespace, patchTypeName, warfarePrefabSpecs),
                new WarfareEffectTypeSpec(WarfareFireAndIceStatusEffectsNamespace, patchTypeName, fireAndIcePrefabSpecs)
            },
            aliases);
    }

    private static Dictionary<string, WarfareBuiltInEffectRegistration> BuildRegistrationLookup()
    {
        Dictionary<string, WarfareBuiltInEffectRegistration> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (WarfareBuiltInEffectRegistration registration in BuiltInRegistrations)
        {
            foreach (string effectId in registration.EffectIds)
            {
                string key = effectId?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                {
                    lookup.Add(key, registration);
                }
            }
        }

        return lookup;
    }

    internal static void TryInstallHooks()
    {
        if (_hooksInstalled)
        {
            return;
        }

        Harmony harmony = new("sighsorry.WarfareTweaks");
        int patchedCount = TryInstallAttackWeaponContextHook(harmony);
        patchedCount += TryInstallBuiltInDirectHitGateHooks(harmony);
        patchedCount += TryInstallAddToItemGateHooks(harmony);
        patchedCount += TryInstallBleedingTuningHooks(harmony);
        patchedCount += TryInstallFixedDamageTuningHooks(harmony);
        patchedCount += TryInstallHasteTuningHooks(harmony);
        patchedCount += TryInstallAdrenalineTuningHooks(harmony);

        if (patchedCount > 0)
        {
            _hooksInstalled = true;
            WarfareTweaksPlugin.ModLogger.LogInfo($"Installed {patchedCount} Warfare built-in effect hook(s).");
        }
    }

    internal static void RebuildBuiltInEffects(IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        SuppressedEffectsByPrefabName.Clear();
        ManagedEffectsByPrefabName.Clear();
        ManagedChainLightningPrefabs.Clear();
        RebuildConfiguredEffectLookup(effectConfigs);

        foreach (WarfareBuiltInEffectRegistration registration in BuiltInRegistrations)
        {
            foreach (string prefabName in registration.PrefabNames)
            {
                AddManagedEffect(prefabName, registration.Id);
            }

            if (!TryFindConfiguredEffect(effectConfigs, registration, out EffectBehaviorConfig? effectConfig) ||
                effectConfig == null)
            {
                AddSuppressedDefaultAssignments(registration);
                continue;
            }

            foreach (string prefabName in GetConfiguredPrefabNames(effectConfig))
            {
                AddManagedEffect(prefabName, registration.Id);
            }

            AddSuppressedMissingDefaultAssignments(registration, effectConfig);
        }

        foreach (string prefabName in ChainLightningMistlandsDefaultPrefabs)
        {
            ManagedChainLightningPrefabs.Add(prefabName);
        }

        if (TryFindConfiguredChainLightningEffect(effectConfigs, out _, out EffectBehaviorConfig? chainLightningConfig) &&
            chainLightningConfig != null)
        {
            foreach (string prefabName in GetConfiguredPrefabNames(chainLightningConfig))
            {
                ManagedChainLightningPrefabs.Add(prefabName);
            }
        }
    }

    // YAML/ObjectDB application mutates Warfare target lists and native item status effects.
    internal static void ApplyConfiguredEffects(ObjectDB objectDb, IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        List<(string EffectId, EffectBehaviorConfig Config)> warfareEffects = effectConfigs
            .Where(entry => entry.Value != null)
            .Select(entry => (entry.Key.Trim(), entry.Value))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Item1))
            .ToList();
        ResetAppliedConfiguredAssignments();
        ResetAppliedPrefabOverrides();

        if (!IsWarfareLoaded())
        {
            if (warfareEffects.Count > 0 &&
                WarfareTweaksWarningLog.TryMarkReported("warfare_effects_missing_warfare"))
            {
                WarfareTweaksPlugin.ModLogger.LogWarning(
                    $"Skipping {WarfareTweaksPlugin.WarfareYamlFileName} warfare effect assignments: Warfare is not installed.");
            }

            return;
        }

        EnsureWarfareGreatbowStatusEffects(objectDb);

        int removedBuiltInCount = RemoveBuiltInTargetAssignments(effectConfigs);
        if (removedBuiltInCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Removed {removedBuiltInCount} Warfare built-in effect target assignment(s) before applying {WarfareTweaksPlugin.WarfareYamlFileName}.");
        }

        int removedAttackStatusEffectCount = SuppressNativeAttackStatusEffects(objectDb, effectConfigs);
        if (removedAttackStatusEffectCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Removed {removedAttackStatusEffectCount} Warfare native attack status effect assignment(s) before applying {WarfareTweaksPlugin.WarfareYamlFileName}.");
        }

        int appliedCount = 0;
        if (!TryFindConfiguredChainLightningEffect(effectConfigs, out _, out _))
        {
            SuppressDefaultChainLightningAssignments(objectDb, ref appliedCount);
        }

        if (warfareEffects.Count == 0)
        {
            if (appliedCount > 0)
            {
                WarfareTweaksPlugin.ModLogger.LogInfo($"Applied {appliedCount} configured Warfare effect assignment(s).");
            }

            return;
        }

        foreach ((string effectId, EffectBehaviorConfig effectConfig) in warfareEffects)
        {
            if (TryApplyChainLightningConfig(objectDb, effectId, effectConfig, out int chainLightningCount))
            {
                appliedCount += chainLightningCount;
                continue;
            }

            if (!TryFindRegistration(effectId, out WarfareBuiltInEffectRegistration? registration))
            {
                if (WarfareTweaksWarningLog.TryMarkReported($"warfare_effect_unknown_{effectId}"))
                {
                    WarfareTweaksPlugin.ModLogger.LogWarning(
                        $"Skipping warfare effect '{effectId}': no matching Warfare built-in effect is known.");
                }

                continue;
            }

            WarfareBuiltInEffectRegistration resolvedRegistration = registration!;
            if (!TryResolveAnyLoadedEffectTypeName(resolvedRegistration, out string anyEffectTypeName))
            {
                if (ShouldSilentlySkipMissingOptionalEffect(resolvedRegistration, effectConfig))
                {
                    continue;
                }

                if (WarfareTweaksWarningLog.TryMarkReported($"warfare_effect_type_missing_{resolvedRegistration.Id}"))
                {
                    WarfareTweaksPlugin.ModLogger.LogWarning(
                        $"Skipping warfare effect '{resolvedRegistration.Id}': none of its known Warfare type(s) were found.");
                }

                continue;
            }

            foreach ((string configuredPrefabName, EffectBehaviorOverrideConfig? prefabOverride) in effectConfig.Prefabs ?? new Dictionary<string, EffectBehaviorOverrideConfig>())
            {
                string prefabName = configuredPrefabName?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(prefabName))
                {
                    continue;
                }

                if (!ContainsObjectDbItem(objectDb, prefabName))
                {
                    if (ShouldSilentlySkipMissingOptionalDefaultPrefab(resolvedRegistration, prefabName))
                    {
                        continue;
                    }

                    if (WarfareTweaksWarningLog.TryMarkReported($"warfare_effect_prefab_missing_{resolvedRegistration.Id}_{prefabName}"))
                    {
                        WarfareTweaksPlugin.ModLogger.LogWarning(
                            $"Skipping warfare effect '{resolvedRegistration.Id}' on '{prefabName}': prefab was not found in ObjectDB.");
                    }

                    continue;
                }

                string effectTypeName = ResolveEffectTypeNameForPrefab(resolvedRegistration, prefabName) ?? anyEffectTypeName;
                Type? effectType = FindLoadedType(effectTypeName);
                if (effectType == null)
                {
                    if (WarfareTweaksWarningLog.TryMarkReported($"warfare_effect_type_missing_{resolvedRegistration.Id}_{prefabName}"))
                    {
                        WarfareTweaksPlugin.ModLogger.LogWarning(
                            $"Skipping warfare effect '{resolvedRegistration.Id}' on '{prefabName}': Warfare type '{effectTypeName}' was not found.");
                    }

                    continue;
                }

                int? value = ResolveConfiguredWarfareValue(resolvedRegistration, effectConfig, prefabOverride, prefabName);

                if (!TryApplyTargetAssignment(effectType, prefabName, value, out WarfareAppliedAssignment? assignment, out string reason))
                {
                    if (WarfareTweaksWarningLog.TryMarkReported($"warfare_effect_add_failed_{resolvedRegistration.Id}_{prefabName}"))
                    {
                        WarfareTweaksPlugin.ModLogger.LogWarning(
                            $"Skipping warfare effect '{resolvedRegistration.Id}' on '{prefabName}': {reason}");
                    }

                    continue;
                }

                AppliedConfiguredAssignments[$"{resolvedRegistration.Id}:{prefabName}"] = assignment!;
                appliedCount++;
            }
        }

        if (appliedCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo($"Applied {appliedCount} configured Warfare effect assignment(s).");
        }
    }

    internal static bool IsWarfareEffectId(string effectId)
    {
        return TryFindRegistration(effectId, out _);
    }

    internal static bool ShouldSuppressBuiltIn(string weaponPrefabName, string effectId)
    {
        return SuppressedEffectsByPrefabName.TryGetValue(weaponPrefabName, out HashSet<string>? suppressedEffects) &&
               suppressedEffects.Contains(effectId);
    }

    internal static void AppendMissingConfiguredEffectTooltips(ItemDrop.ItemData item, ref string tooltip)
    {
        if (item?.m_shared == null ||
            string.IsNullOrWhiteSpace(tooltip) ||
            !TryResolveItemPrefabName(item, out string prefabName))
        {
            return;
        }

        List<string> fallbackBlocks = new();
        foreach ((string configuredEffectId, EffectBehaviorConfig effectConfig) in WarfareTweaksPlugin.CurrentEffects)
        {
            string effectId = configuredEffectId?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(effectId) || effectConfig == null)
            {
                continue;
            }

            if (!WarfareEffectConfigHelpers.HasPrefabAssignment(effectConfig, prefabName))
            {
                continue;
            }

            string? block = null;
            if (TryFindRegistration(effectId, out WarfareBuiltInEffectRegistration? registration) &&
                registration != null &&
                ShouldAppendFallbackTooltip(registration.Id, tooltip))
            {
                block = BuildConfiguredFallbackTooltip(registration, effectConfig, prefabName);
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

    private static bool ShouldAppendFallbackTooltip(string effectId, string tooltip)
    {
        string canonicalEffectId = (effectId ?? "").Trim();
        if (string.Equals(canonicalEffectId, "haste", StringComparison.OrdinalIgnoreCase))
        {
            return !TooltipContainsAny(tooltip, "Haste");
        }

        if (string.Equals(canonicalEffectId, "pinning", StringComparison.OrdinalIgnoreCase))
        {
            return !TooltipContainsAny(tooltip, "Pinning", "Pinned");
        }

        return false;
    }

    private static bool TooltipContainsAny(string tooltip, params string[] needles)
    {
        return needles.Any(needle =>
            !string.IsNullOrWhiteSpace(needle) &&
            tooltip.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string BuildConfiguredFallbackTooltip(
        WarfareBuiltInEffectRegistration registration,
        EffectBehaviorConfig effectConfig,
        string prefabName)
    {
        EffectBehaviorOverrideConfig? prefabOverride = TryGetPrefabOverride(effectConfig, prefabName);
        if (string.Equals(registration.Id, "haste", StringComparison.OrdinalIgnoreCase))
        {
            float multiplier = ResolveConfiguredHasteMoveSpeedMultiplier(effectConfig, prefabOverride, 1.4f);
            return $"<color=orange>Haste</color>: Increases your movement speed on 6th hit. Multiplier: {FormatNumber(multiplier)}x.";
        }

        int? value = ResolveConfiguredWarfareValue(registration, effectConfig, prefabOverride, prefabName);
        return value.HasValue
            ? $"<color=orange>Pinning</color>: Slows targets hit for {FormatNumber(value.Value)}s."
            : "<color=orange>Pinning</color>: Slows targets hit for a brief moment.";
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

    private static bool TryFindConfiguredEffect(
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs,
        WarfareBuiltInEffectRegistration registration,
        out EffectBehaviorConfig? effectConfig)
    {
        effectConfig = null;
        foreach ((string configuredEffectId, EffectBehaviorConfig configuredEffect) in effectConfigs)
        {
            if (configuredEffect == null ||
                !registration.EffectIds.Any(effectId => string.Equals(effectId, configuredEffectId?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            effectConfig = configuredEffect;
            return true;
        }

        return false;
    }

    private static void RebuildConfiguredEffectLookup(IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        ConfiguredEffectsByPrefabAndEffectId.Clear();
        foreach ((string effectId, EffectBehaviorConfig effectConfig) in effectConfigs)
        {
            string configuredEffectId = effectId?.Trim() ?? "";
            if (effectConfig == null ||
                !TryFindRegistration(configuredEffectId, out WarfareBuiltInEffectRegistration? registration) ||
                registration == null)
            {
                continue;
            }

            foreach (string prefabName in GetConfiguredPrefabNames(effectConfig))
            {
                ConfiguredEffectsByPrefabAndEffectId[ConfiguredEffectLookupKey(prefabName, registration.Id)] =
                    new ConfiguredWarfareEffectLookup(
                        registration,
                        effectConfig,
                        TryGetPrefabOverride(effectConfig, prefabName));
            }
        }
    }

    private static bool TryGetConfiguredEffectForCurrentWeapon(
        string canonicalEffectId,
        out string prefabName,
        out ConfiguredWarfareEffectLookup? lookup)
    {
        lookup = null;
        if (!TryGetCurrentAttackWeaponPrefabName(out prefabName))
        {
            return false;
        }

        return TryGetConfiguredEffect(prefabName, canonicalEffectId, out lookup);
    }

    private static bool TryGetConfiguredEffect(
        string prefabName,
        string canonicalEffectId,
        out ConfiguredWarfareEffectLookup? lookup)
    {
        lookup = null;
        if (string.IsNullOrWhiteSpace(prefabName) || string.IsNullOrWhiteSpace(canonicalEffectId))
        {
            return false;
        }

        return ConfiguredEffectsByPrefabAndEffectId.TryGetValue(
            ConfiguredEffectLookupKey(prefabName, canonicalEffectId),
            out lookup);
    }

    private static string ConfiguredEffectLookupKey(string prefabName, string canonicalEffectId)
    {
        return $"{prefabName.Trim()}\n{canonicalEffectId.Trim()}";
    }

    private static IEnumerable<string> GetConfiguredPrefabNames(EffectBehaviorConfig effectConfig)
    {
        if (effectConfig.Prefabs == null)
        {
            yield break;
        }

        foreach (string configuredPrefabName in effectConfig.Prefabs.Keys)
        {
            string prefabName = configuredPrefabName?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                yield return prefabName;
            }
        }
    }

    private static bool TryFindConfiguredChainLightningEffect(
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs,
        out string effectId,
        out EffectBehaviorConfig? effectConfig)
    {
        effectId = "";
        effectConfig = null;
        foreach ((string configuredEffectId, EffectBehaviorConfig configuredEffect) in effectConfigs)
        {
            string trimmedEffectId = configuredEffectId?.Trim() ?? "";
            if (configuredEffect == null ||
                !IsChainLightningConfig(trimmedEffectId, configuredEffect))
            {
                continue;
            }

            effectId = trimmedEffectId;
            effectConfig = configuredEffect;
            return true;
        }

        return false;
    }

    private static void AddSuppressedDefaultAssignments(WarfareBuiltInEffectRegistration registration)
    {
        foreach (string prefabName in registration.PrefabNames)
        {
            AddSuppressedEffect(prefabName, registration.Id);
        }
    }

    private static void AddSuppressedMissingDefaultAssignments(
        WarfareBuiltInEffectRegistration registration,
        EffectBehaviorConfig effectConfig)
    {
        foreach (string prefabName in registration.PrefabNames)
        {
            if (WarfareEffectConfigHelpers.HasPrefabAssignment(effectConfig, prefabName))
            {
                continue;
            }

            AddSuppressedEffect(prefabName, registration.Id);
        }
    }

    private static void AddManagedEffect(string prefabName, string effectId)
    {
        if (!ManagedEffectsByPrefabName.TryGetValue(prefabName, out HashSet<string>? effects))
        {
            effects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ManagedEffectsByPrefabName[prefabName] = effects;
        }

        effects.Add(effectId);
    }

    private static void AddSuppressedEffect(string prefabName, string effectId)
    {
        if (!SuppressedEffectsByPrefabName.TryGetValue(prefabName, out HashSet<string>? effects))
        {
            effects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SuppressedEffectsByPrefabName[prefabName] = effects;
        }

        effects.Add(effectId);
    }

    private static bool TryResolveSuppressedBuiltInIds(string effectId, out string[] builtInIds)
    {
        switch ((effectId ?? "").Trim().ToLowerInvariant())
        {
            case "adrenaline":
                builtInIds = new[] { "adrenaline" };
                return true;
            case "bash":
                builtInIds = new[] { "bash" };
                return true;
            case "bleeding":
                builtInIds = new[] { "bleeding", "bleedingSecondary", "impale" };
                return true;
            case "decapitator":
                builtInIds = new[] { "decapitator4", "decapitator5", "hackAndSlash" };
                return true;
            case "executioner":
                builtInIds = new[] { "executioner" };
                return true;
            case "haste":
                builtInIds = new[] { "haste" };
                return true;
            case "juggernaut":
                builtInIds = new[] { "juggernaut" };
                return true;
            case "piercing":
                builtInIds = new[] { "piercingGreatbowMistlands", "piercingGreatbowModer", "piercingGreatbowPlains" };
                return true;
            case "smasher":
                builtInIds = new[] { "smasher" };
                return true;
            case "vampirism":
                builtInIds = new[] { "vampirism" };
                return true;
            default:
                builtInIds = Array.Empty<string>();
                return false;
        }
    }

    private static bool SuppressBuiltInPrefix(MethodBase __originalMethod)
    {
        return __originalMethod == null ||
               !PatchedPrefixMethods.TryGetValue(__originalMethod, out string? effectId) ||
               !WeaponEffectManager.ShouldSuppressWarfareBuiltIn(effectId);
    }

    // Harmony hook installers are grouped before the resolver methods they patch into.
    private static int TryInstallAttackWeaponContextHook(Harmony harmony)
    {
        Type? targetType = FindLoadedType(WarfareUtilsTypeName);
        MethodInfo? targetMethod = targetType != null
            ? AccessTools.DeclaredMethod(targetType, "TryGetAttackWeapon")
            : null;
        if (targetMethod == null)
        {
            return 0;
        }

        harmony.Patch(
            targetMethod,
            prefix: new HarmonyMethod(typeof(WarfareCompat), nameof(WarfareTryGetAttackWeaponPrefix)));
        return 1;
    }

    private static bool WarfareTryGetAttackWeaponPrefix(ref string prefab, ref bool __result)
    {
        if (!DirectWeaponHitContextSystem.TryGetCurrentProjectileWeaponPrefabName(out string weaponPrefabName))
        {
            return true;
        }

        prefab = weaponPrefabName;
        __result = true;
        return false;
    }

    private static int TryInstallBuiltInDirectHitGateHooks(Harmony harmony)
    {
        int patchedCount = 0;
        HashSet<string> patchedTypeNames = new(StringComparer.Ordinal);
        foreach (WarfareBuiltInEffectRegistration registration in BuiltInRegistrations)
        {
            foreach (WarfareEffectTypeSpec spec in registration.EffectTypeSpecs)
            {
                string patchTypeName = spec.CharacterDamagePatchTypeName;
                if (!patchedTypeNames.Add(patchTypeName))
                {
                    continue;
                }

                Type? targetType = FindLoadedType(patchTypeName);
                if (targetType == null)
                {
                    continue;
                }

                MethodInfo? targetMethod = AccessTools.DeclaredMethod(targetType, "Prefix");
                if (targetMethod == null)
                {
                    continue;
                }

                harmony.Patch(
                    targetMethod,
                    prefix: new HarmonyMethod(typeof(WarfareCompat), nameof(WarfareBuiltInDirectHitGatePrefix)));
                PatchedPrefixMethods[targetMethod] = registration.Id;
                patchedCount++;
            }
        }

        return patchedCount;
    }

    private static bool WarfareBuiltInDirectHitGatePrefix(MethodBase __originalMethod)
    {
        bool shouldRun = DirectWeaponHitContextSystem.ShouldCountWeaponEffectHit &&
                         SuppressBuiltInPrefix(__originalMethod);
        if (shouldRun &&
            __originalMethod != null &&
            PatchedPrefixMethods.TryGetValue(__originalMethod, out string? effectId) &&
            string.Equals(effectId, "haste", StringComparison.OrdinalIgnoreCase))
        {
            _pendingHasteMoveSpeedMultiplier = ResolveConfiguredHasteMoveSpeedMultiplier(1.4f);
        }

        return shouldRun;
    }

    private static int TryInstallAddToItemGateHooks(Harmony harmony)
    {
        int patchedCount = 0;
        HashSet<string> patchedTypeNames = new(StringComparer.Ordinal);
        foreach (WarfareBuiltInEffectRegistration registration in BuiltInRegistrations)
        {
            foreach (WarfareEffectTypeSpec spec in registration.EffectTypeSpecs)
            {
                if (!patchedTypeNames.Add(spec.EffectTypeName))
                {
                    continue;
                }

                Type? targetType = FindLoadedType(spec.EffectTypeName);
                if (targetType == null)
                {
                    continue;
                }

                MethodInfo? targetMethod = AccessTools.DeclaredMethod(targetType, "AddToItem");
                if (targetMethod == null)
                {
                    continue;
                }

                harmony.Patch(
                    targetMethod,
                    prefix: new HarmonyMethod(typeof(WarfareCompat), nameof(WarfareAddToItemGatePrefix)));
                PatchedAddToItemMethods[targetMethod] = registration.Id;
                patchedCount++;
            }
        }

        return patchedCount;
    }

    private static bool WarfareAddToItemGatePrefix(MethodBase __originalMethod)
    {
        return _allowWarfareAddToItem ||
               __originalMethod == null ||
               !PatchedAddToItemMethods.ContainsKey(__originalMethod);
    }

    private static int TryInstallBleedingTuningHooks(Harmony harmony)
    {
        int patchedCount = 0;
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Bleeding+SE_Warfare_Bleeding",
            "OnEnable",
            postfixName: nameof(WarfareBleedingStackStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Bleeding+SE_Warfare_Bleeding",
            "SetLevel",
            prefixName: nameof(WarfareBleedingStackStatusSetLevelPrefix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Bleeding+SE_Warfare_Bleeding_Stacking",
            "OnEnable",
            postfixName: nameof(WarfareBleedingDotStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Bleeding+SE_Warfare_Bleeding_Stacking",
            "SetLevel",
            postfixName: nameof(WarfareBleedingDotStatusSetLevelPostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Bleeding+SE_Warfare_Bleeding_Stacking",
            "UpdateStatusEffect",
            transpilerName: nameof(WarfareBleedingDotStatusUpdateTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.BleedingSecondaryAttack+SE_Warfare_BleedingSecondaryAttack",
            "OnEnable",
            postfixName: nameof(WarfareBleedingSecondaryStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.BleedingSecondaryAttack+SE_Warfare_BleedingSecondaryAttack",
            "SetLevel",
            postfixName: nameof(WarfareBleedingSecondaryStatusSetLevelPostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.BleedingSecondaryAttack+SE_Warfare_BleedingSecondaryAttack",
            "UpdateStatusEffect",
            transpilerName: nameof(WarfareBleedingSecondaryStatusUpdateTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Impale+SE_Warfare_Impale",
            "OnEnable",
            postfixName: nameof(WarfareImpaleStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Impale+SE_Warfare_Impale",
            "SetLevel",
            postfixName: nameof(WarfareImpaleStatusSetLevelPostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Impale+SE_Warfare_Impale",
            "UpdateStatusEffect",
            transpilerName: nameof(WarfareImpaleStatusUpdateTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.BurningSecondaryAttack+SE_Warfare_BurningSecondaryAttack",
            "OnEnable",
            postfixName: nameof(WarfareBurningSecondaryStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.BurningSecondaryAttack+SE_Warfare_BurningSecondaryAttack",
            "SetLevel",
            postfixName: nameof(WarfareBurningSecondaryStatusSetLevelPostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.BurningSecondaryAttack+SE_Warfare_BurningSecondaryAttack",
            "UpdateStatusEffect",
            transpilerName: nameof(WarfareBurningSecondaryStatusUpdateTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.BurningSecondaryAttack+SE_WarfareFireAndIce_BurningSecondaryAttack",
            "OnEnable",
            postfixName: nameof(WarfareBurningSecondaryStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.BurningSecondaryAttack+SE_WarfareFireAndIce_BurningSecondaryAttack",
            "SetLevel",
            postfixName: nameof(WarfareBurningSecondaryStatusSetLevelPostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.BurningSecondaryAttack+SE_WarfareFireAndIce_BurningSecondaryAttack",
            "UpdateStatusEffect",
            transpilerName: nameof(WarfareFireAndIceBurningSecondaryStatusUpdateTranspiler));
        return patchedCount;
    }

    private static int TryInstallFixedDamageTuningHooks(Harmony harmony)
    {
        int patchedCount = 0;
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.PiercingGreatbowMistlands+Warfare_PiercingGreatbowMistlands",
            "SetLevel",
            transpilerName: nameof(WarfarePiercingGreatbowMistlandsSetLevelTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.PiercingGreatbowModer+Warfare_PiercingGreatbowModer",
            "SetLevel",
            transpilerName: nameof(WarfarePiercingGreatbowModerSetLevelTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.PiercingGreatbowPlains+Warfare_PiercingGreatbowPlains",
            "SetLevel",
            transpilerName: nameof(WarfarePiercingGreatbowPlainsSetLevelTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.PierceGreatbow+SE_WarfareFireAndIce_Pierced",
            "SetLevel",
            transpilerName: nameof(WarfareFireAndIcePierceGreatbowSetLevelTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.LightningBurst+SE_WarfareFireAndIce_LightningBurst",
            "SetLevel",
            transpilerName: nameof(WarfareFireAndIceLightningBurstSetLevelTranspiler));
        return patchedCount;
    }

    private static int TryInstallHasteTuningHooks(Harmony harmony)
    {
        int patchedCount = 0;
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Haste+SE_Warfare_Haste_Stacking",
            "OnEnable",
            postfixName: nameof(WarfareHasteStackingStatusOnEnablePostfix));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.Haste+SE_WarfareFireAndIce_Haste_Stacking",
            "OnEnable",
            postfixName: nameof(WarfareHasteStackingStatusOnEnablePostfix));
        return patchedCount;
    }

    private static int TryInstallAdrenalineTuningHooks(Harmony harmony)
    {
        int patchedCount = 0;
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareStatusEffectsNamespace}.Adrenaline+SE_Warfare_Adrenaline",
            "SetLevel",
            transpilerName: nameof(WarfareAdrenalineSetLevelTranspiler));
        patchedCount += TryPatchWarfareMethod(
            harmony,
            $"{WarfareFireAndIceStatusEffectsNamespace}.Adrenaline+SE_WarfareFireAndIce_Adrenaline",
            "SetLevel",
            transpilerName: nameof(WarfareAdrenalineSetLevelTranspiler));
        return patchedCount;
    }

    private static int TryPatchWarfareMethod(
        Harmony harmony,
        string typeName,
        string methodName,
        string? prefixName = null,
        string? postfixName = null,
        string? transpilerName = null)
    {
        Type? targetType = FindLoadedType(typeName);
        if (targetType == null)
        {
            return 0;
        }

        MethodInfo? targetMethod = AccessTools.DeclaredMethod(targetType, methodName);
        if (targetMethod == null)
        {
            return 0;
        }

        HarmonyMethod? prefix = !string.IsNullOrWhiteSpace(prefixName)
            ? new HarmonyMethod(typeof(WarfareCompat), prefixName)
            : null;
        HarmonyMethod? postfix = !string.IsNullOrWhiteSpace(postfixName)
            ? new HarmonyMethod(typeof(WarfareCompat), postfixName)
            : null;
        HarmonyMethod? transpiler = !string.IsNullOrWhiteSpace(transpilerName)
            ? new HarmonyMethod(typeof(WarfareCompat), transpilerName)
            : null;
        harmony.Patch(targetMethod, prefix, postfix, transpiler);
        return 1;
    }

    private static void WarfareBleedingStackStatusOnEnablePostfix(object __instance)
    {
        if (__instance is not StatusEffect status ||
            !TryResolveWarfareBleedTuning("bleeding", out WarfareBleedTuning? tuning) ||
            tuning == null ||
            !tuning.StackWindow.HasValue)
        {
            return;
        }

        status.m_ttl = Mathf.Max(0.01f, tuning.StackWindow.Value);
    }

    private static bool WarfareBleedingStackStatusSetLevelPrefix(object __instance, int itemLevel)
    {
        if (__instance is not StatusEffect status ||
            !TryResolveWarfareBleedTuning("bleeding", out WarfareBleedTuning? tuning) ||
            tuning == null ||
            !tuning.StacksRequired.HasValue)
        {
            return true;
        }

        if (!TryGetIntField(__instance, "stack", out int stack))
        {
            return true;
        }

        stack++;
        TrySetIntField(__instance, "stack", stack);
        if (stack < Mathf.Max(1, tuning.StacksRequired.Value))
        {
            return false;
        }

        Character? character = status.m_character;
        if (character == null)
        {
            return true;
        }

        character.m_seman.AddStatusEffect("Warfare_Bleeding_Stacking".GetStableHashCode(), resetTime: true, itemLevel, skillLevel: 0f);
        character.m_seman.RemoveStatusEffect(status, quiet: true);
        return false;
    }

    private static void WarfareBleedingDotStatusOnEnablePostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "bleeding");
    }

    private static void WarfareBleedingDotStatusSetLevelPostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "bleeding");
    }

    private static void WarfareBleedingSecondaryStatusOnEnablePostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "bleedingSecondary");
    }

    private static void WarfareBleedingSecondaryStatusSetLevelPostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "bleedingSecondary");
    }

    private static void WarfareImpaleStatusOnEnablePostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "impale");
    }

    private static void WarfareImpaleStatusSetLevelPostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "impale");
    }

    private static void WarfareBurningSecondaryStatusOnEnablePostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "burningSecondary");
    }

    private static void WarfareBurningSecondaryStatusSetLevelPostfix(object __instance)
    {
        ApplyWarfareBleedStatusTuning(__instance, "burningSecondary");
    }

    private static void WarfareHasteStackingStatusOnEnablePostfix(object __instance)
    {
        if (__instance is not StatusEffect status)
        {
            return;
        }

        float multiplier = _pendingHasteMoveSpeedMultiplier ?? ResolveConfiguredHasteMoveSpeedMultiplier(1.4f);
        _pendingHasteMoveSpeedMultiplier = null;
        HasteTuningsByStatus.Remove(status);
        HasteTuningsByStatus.Add(status, new WarfareHasteTuningState(multiplier));
    }

    private static IEnumerable<CodeInstruction> WarfareBleedingDotStatusUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareBleedUpdateConstants(instructions, tickIntervalDefault: 1f, damageCoefficientDefault: 0.0025f);
    }

    private static IEnumerable<CodeInstruction> WarfareBleedingSecondaryStatusUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareBleedUpdateConstants(instructions, tickIntervalDefault: 1f, damageCoefficientDefault: 0.00125f);
    }

    private static IEnumerable<CodeInstruction> WarfareImpaleStatusUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareBleedUpdateConstants(instructions, tickIntervalDefault: 1f, damageCoefficientDefault: 0.0025f);
    }

    private static IEnumerable<CodeInstruction> WarfareBurningSecondaryStatusUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareBleedUpdateConstants(instructions, tickIntervalDefault: 1f, damageCoefficientDefault: 0.00125f);
    }

    private static IEnumerable<CodeInstruction> WarfareFireAndIceBurningSecondaryStatusUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareFlatDotDamageConstant(instructions, damageDefault: 20);
    }

    private static IEnumerable<CodeInstruction> WarfarePiercingGreatbowMistlandsSetLevelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareFixedDamageConstant(instructions, damageDefault: 60, effectId: "piercingGreatbowMistlands");
    }

    private static IEnumerable<CodeInstruction> WarfarePiercingGreatbowModerSetLevelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareFixedDamageConstant(instructions, damageDefault: 40, effectId: "piercingGreatbowModer");
    }

    private static IEnumerable<CodeInstruction> WarfarePiercingGreatbowPlainsSetLevelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareFixedDamageConstant(instructions, damageDefault: 40, effectId: "piercingGreatbowPlains");
    }

    private static IEnumerable<CodeInstruction> WarfareFireAndIcePierceGreatbowSetLevelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareFixedDamageConstant(instructions, damageDefault: 100, effectId: "pierceGreatbowFireAndIce");
    }

    private static IEnumerable<CodeInstruction> WarfareFireAndIceLightningBurstSetLevelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceWarfareFixedDamageConstant(instructions, damageDefault: 55, effectId: "lightningBurst");
    }

    private static IEnumerable<CodeInstruction> WarfareAdrenalineSetLevelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo divisorResolver = AccessTools.DeclaredMethod(typeof(WarfareCompat), nameof(ResolveWarfareAdrenalineDivisor));
        foreach (CodeInstruction instruction in instructions)
        {
            if (IsLdcR4(instruction, 350f))
            {
                foreach (CodeInstruction replacement in BuildAdrenalineDivisorResolverInstructions(instruction, divisorResolver))
                {
                    yield return replacement;
                }

                continue;
            }

            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceWarfareBleedUpdateConstants(
        IEnumerable<CodeInstruction> instructions,
        float tickIntervalDefault,
        float damageCoefficientDefault)
    {
        MethodInfo tickIntervalResolver = AccessTools.DeclaredMethod(typeof(WarfareCompat), nameof(ResolveWarfareBleedTickInterval));
        MethodInfo damageCoefficientResolver = AccessTools.DeclaredMethod(typeof(WarfareCompat), nameof(ResolveWarfareBleedDamageCoefficient));
        foreach (CodeInstruction instruction in instructions)
        {
            if (IsLdcR4(instruction, tickIntervalDefault))
            {
                foreach (CodeInstruction replacement in BuildFloatResolverInstructions(instruction, tickIntervalDefault, tickIntervalResolver))
                {
                    yield return replacement;
                }

                continue;
            }

            if (IsLdcR4(instruction, damageCoefficientDefault))
            {
                foreach (CodeInstruction replacement in BuildFloatResolverInstructions(instruction, damageCoefficientDefault, damageCoefficientResolver))
                {
                    yield return replacement;
                }

                continue;
            }

            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceWarfareFixedDamageConstant(
        IEnumerable<CodeInstruction> instructions,
        int damageDefault,
        string effectId)
    {
        MethodInfo damageResolver = AccessTools.DeclaredMethod(typeof(WarfareCompat), nameof(ResolveWarfareFixedDamage));
        bool replaced = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!replaced && IsLdcI4(instruction, damageDefault))
            {
                foreach (CodeInstruction replacement in BuildFixedDamageResolverInstructions(instruction, damageDefault, effectId, damageResolver))
                {
                    yield return replacement;
                }

                replaced = true;
                continue;
            }

            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceWarfareFlatDotDamageConstant(
        IEnumerable<CodeInstruction> instructions,
        int damageDefault)
    {
        MethodInfo damageResolver = AccessTools.DeclaredMethod(typeof(WarfareCompat), nameof(ResolveWarfareFlatDotTickDamage));
        bool replaced = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!replaced && IsLdcI4(instruction, damageDefault))
            {
                CodeInstruction loadInstance = new(OpCodes.Ldarg_0)
                {
                    labels = instruction.labels,
                    blocks = instruction.blocks
                };
                yield return loadInstance;
                yield return new CodeInstruction(OpCodes.Ldc_I4, damageDefault);
                yield return new CodeInstruction(OpCodes.Call, damageResolver);
                replaced = true;
                continue;
            }

            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> BuildFloatResolverInstructions(
        CodeInstruction originalInstruction,
        float defaultValue,
        MethodInfo resolver)
    {
        CodeInstruction loadInstance = new(OpCodes.Ldarg_0)
        {
            labels = originalInstruction.labels,
            blocks = originalInstruction.blocks
        };
        yield return loadInstance;
        yield return new CodeInstruction(OpCodes.Ldc_R4, defaultValue);
        yield return new CodeInstruction(OpCodes.Call, resolver);
    }

    private static IEnumerable<CodeInstruction> BuildFixedDamageResolverInstructions(
        CodeInstruction originalInstruction,
        int defaultValue,
        string effectId,
        MethodInfo resolver)
    {
        CodeInstruction loadInstance = new(OpCodes.Ldarg_0)
        {
            labels = originalInstruction.labels,
            blocks = originalInstruction.blocks
        };
        yield return loadInstance;
        yield return new CodeInstruction(OpCodes.Ldarg_1);
        yield return new CodeInstruction(OpCodes.Ldc_I4, defaultValue);
        yield return new CodeInstruction(OpCodes.Ldstr, effectId);
        yield return new CodeInstruction(OpCodes.Call, resolver);
    }

    private static IEnumerable<CodeInstruction> BuildAdrenalineDivisorResolverInstructions(
        CodeInstruction originalInstruction,
        MethodInfo resolver)
    {
        CodeInstruction loadInstance = new(OpCodes.Ldarg_0)
        {
            labels = originalInstruction.labels,
            blocks = originalInstruction.blocks
        };
        yield return loadInstance;
        yield return new CodeInstruction(OpCodes.Ldarg_1);
        yield return new CodeInstruction(OpCodes.Ldc_R4, 350f);
        yield return new CodeInstruction(OpCodes.Call, resolver);
    }

    private static bool IsLdcR4(CodeInstruction instruction, float value)
    {
        return instruction.opcode == OpCodes.Ldc_R4 &&
               instruction.operand is float operand &&
               Math.Abs(operand - value) < 0.000001f;
    }

    private static bool IsLdcI4(CodeInstruction instruction, int value)
    {
        if (instruction.opcode == OpCodes.Ldc_I4)
        {
            return instruction.operand is int operand && operand == value;
        }

        if (instruction.opcode == OpCodes.Ldc_I4_S)
        {
            return Convert.ToInt32(instruction.operand) == value;
        }

        if (value >= 0 && value <= 8)
        {
            OpCode expected = value switch
            {
                0 => OpCodes.Ldc_I4_0,
                1 => OpCodes.Ldc_I4_1,
                2 => OpCodes.Ldc_I4_2,
                3 => OpCodes.Ldc_I4_3,
                4 => OpCodes.Ldc_I4_4,
                5 => OpCodes.Ldc_I4_5,
                6 => OpCodes.Ldc_I4_6,
                7 => OpCodes.Ldc_I4_7,
                8 => OpCodes.Ldc_I4_8,
                _ => instruction.opcode
            };
            return instruction.opcode == expected;
        }

        return false;
    }

    // Runtime resolvers are invoked from transpiled Warfare status effect code.
    private static float ResolveWarfareBleedTickInterval(object statusObject, float vanillaValue)
    {
        if (statusObject is not StatusEffect status ||
            !TryGetStoredWarfareBleedTuning(status, out WarfareBleedTuningState? tuning) ||
            tuning == null)
        {
            return vanillaValue;
        }

        if (tuning.UseSourceDamageDot)
        {
            return WarfareSourceDamageDotTickInterval;
        }

        return tuning.TickInterval.HasValue
            ? Mathf.Max(0.01f, tuning.TickInterval.Value)
            : vanillaValue;
    }

    private static float ResolveWarfareBleedDamageCoefficient(object statusObject, float vanillaValue)
    {
        if (statusObject is not StatusEffect status ||
            !TryGetStoredWarfareBleedTuning(status, out WarfareBleedTuningState? tuning) ||
            tuning == null)
        {
            return vanillaValue;
        }

        if (tuning.UseSourceDamageDot)
        {
            return ResolveSourceDamageDotCoefficient(status, tuning);
        }

        return tuning.DamageFactor.HasValue
            ? vanillaValue * Mathf.Max(0f, tuning.DamageFactor.Value)
            : vanillaValue;
    }

    private static int ResolveWarfareFlatDotTickDamage(object statusObject, int vanillaValue)
    {
        if (statusObject is not StatusEffect status ||
            !TryGetStoredWarfareBleedTuning(status, out WarfareBleedTuningState? tuning) ||
            tuning == null ||
            !tuning.UseSourceDamageDot)
        {
            return vanillaValue;
        }

        return Mathf.RoundToInt(ResolveSourceDamageDotTickDamage(tuning));
    }

    private static int ResolveWarfareFixedDamage(object statusObject, int itemLevel, int vanillaValue, string effectId)
    {
        if (!TryResolveConfiguredWarfareValueForCurrentWeapon(effectId, out int configuredValue))
        {
            return vanillaValue;
        }

        return Mathf.Max(0, configuredValue);
    }

    private static float ResolveWarfareHasteMoveSpeedMultiplier(object statusObject, float vanillaValue)
    {
        if (statusObject is StatusEffect status &&
            HasteTuningsByStatus.TryGetValue(status, out WarfareHasteTuningState? tuning))
        {
            return Mathf.Max(0f, tuning.MoveSpeedMultiplier);
        }

        return vanillaValue;
    }

    internal static void ApplyWarfareHasteSpeedModifier(SEMan seMan, ref float speed)
    {
        if (seMan == null)
        {
            return;
        }

        if (TryGetActiveWarfareHasteStatus(seMan, out StatusEffect? status) &&
            status != null)
        {
            speed *= ResolveWarfareHasteMoveSpeedMultiplier(status, 1.4f);
        }
    }

    private static bool TryGetActiveWarfareHasteStatus(SEMan seMan, out StatusEffect? status)
    {
        status = seMan.GetStatusEffect(WarfareHasteStackingHash);
        if (status != null)
        {
            return true;
        }

        status = seMan.GetStatusEffect(WarfareFireAndIceHasteStackingHash);
        return status != null;
    }

    private static float ResolveWarfareAdrenalineDivisor(object statusObject, int itemLevel, float vanillaValue)
    {
        if (!TryResolveConfiguredAdrenalineRestorePercentForCurrentWeapon(out float percent))
        {
            return vanillaValue;
        }

        percent = Mathf.Max(0f, percent);
        if (percent <= 0f)
        {
            return float.PositiveInfinity;
        }

        int effectiveItemLevel = Mathf.Max(1, itemLevel);
        return effectiveItemLevel * 100f / percent;
    }

    private static void ApplyWarfareBleedStatusTuning(object statusObject, string effectId)
    {
        if (statusObject is not StatusEffect status ||
            !TryResolveWarfareBleedTuning(effectId, out WarfareBleedTuning? tuning) ||
            tuning == null)
        {
            return;
        }

        AttachSourceDamageToWarfareBleedTuning(status, tuning);
        StoreWarfareBleedTuning(status, tuning);
        if (tuning.DamageFactor.HasValue)
        {
            status.m_ttl = WarfareSourceDamageDotDuration;
        }
        else if (tuning.Duration.HasValue)
        {
            status.m_ttl = Mathf.Max(0.01f, tuning.Duration.Value);
        }
    }

    private static float ResolveSourceDamageDotCoefficient(StatusEffect status, WarfareBleedTuningState tuning)
    {
        float targetMaxHealth = status.m_character != null ? Mathf.Max(0f, status.m_character.GetMaxHealth()) : 0f;
        int nativeValue = Mathf.Max(1, tuning.NativeValue ?? 1);
        float damageFactor = Mathf.Max(0f, tuning.DamageFactor ?? 0f);
        float sourceDamage = Mathf.Max(0f, tuning.SourceDamage ?? 0f);
        if (targetMaxHealth <= 0f || sourceDamage <= 0f || damageFactor <= 0f)
        {
            return 0f;
        }

        return ResolveSourceDamageDotTickDamage(tuning) / (targetMaxHealth * nativeValue);
    }

    private static float ResolveSourceDamageDotTickDamage(WarfareBleedTuningState tuning)
    {
        float damageFactor = Mathf.Max(0f, tuning.DamageFactor ?? 0f);
        float sourceDamage = Mathf.Max(0f, tuning.SourceDamage ?? 0f);
        if (sourceDamage <= 0f || damageFactor <= 0f)
        {
            return 0f;
        }

        int tickCount = Mathf.Max(1, Mathf.CeilToInt(WarfareSourceDamageDotDuration / WarfareSourceDamageDotTickInterval));
        return sourceDamage * damageFactor / tickCount;
    }

    private static void AttachSourceDamageToWarfareBleedTuning(StatusEffect status, WarfareBleedTuning tuning)
    {
        if (!tuning.DamageFactor.HasValue || status.m_character == null)
        {
            return;
        }

        if (TryGetActiveDotSourceDamage(status.m_character, tuning.PrefabName, out float sourceDamage) ||
            TryGetCurrentAttackRawDamage(tuning.PrefabName, out sourceDamage))
        {
            tuning.SourceDamage = sourceDamage;
        }
    }

    private static bool TryResolveWarfareBleedTuning(string canonicalEffectId, out WarfareBleedTuning? tuning)
    {
        tuning = null;
        if (!TryGetConfiguredEffectForCurrentWeapon(
                canonicalEffectId,
                out string prefabName,
                out ConfiguredWarfareEffectLookup? lookup) ||
            lookup == null)
        {
            return false;
        }

        EffectBehaviorConfig effectConfig = lookup.EffectConfig;
        EffectBehaviorOverrideConfig? prefabOverride = lookup.PrefabOverride;
        tuning = new WarfareBleedTuning
        {
            PrefabName = prefabName,
            StacksRequired = prefabOverride?.StacksRequired ?? effectConfig.StacksRequired,
            StackWindow = prefabOverride?.StackWindow ?? (effectConfig.StackWindow > 0f ? effectConfig.StackWindow : null),
            Duration = prefabOverride?.Duration ?? effectConfig.Duration,
            TickInterval = prefabOverride?.TickInterval ?? effectConfig.TickInterval,
            DamageFactor = prefabOverride?.DamageFactor ?? effectConfig.DamageFactor,
            NativeValue = ResolveConfiguredWarfareValue(lookup.Registration, effectConfig, prefabOverride, prefabName)
        };
        return tuning.HasAnyValue;
    }

    private static bool TryResolveConfiguredWarfareValueForCurrentWeapon(string canonicalEffectId, out int value)
    {
        value = 0;
        if (!TryGetConfiguredEffectForCurrentWeapon(
                canonicalEffectId,
                out string prefabName,
                out ConfiguredWarfareEffectLookup? lookup) ||
            lookup == null)
        {
            return false;
        }

        int? configuredValue = ResolveConfiguredWarfareValue(
            lookup.Registration,
            lookup.EffectConfig,
            lookup.PrefabOverride,
            prefabName);
        if (!configuredValue.HasValue)
        {
            return false;
        }

        value = configuredValue.Value;
        return true;
    }

    private static bool TryResolveConfiguredAdrenalineRestorePercentForCurrentWeapon(out float percent)
    {
        percent = 0f;
        if (!TryGetConfiguredEffectForCurrentWeapon(
                "adrenaline",
                out _,
                out ConfiguredWarfareEffectLookup? lookup) ||
            lookup == null)
        {
            return false;
        }

        return TryResolveAdrenalineRestorePercent(lookup.EffectConfig, lookup.PrefabOverride, out percent);
    }

    private static bool TryResolveAdrenalineRestorePercent(
        EffectBehaviorConfig effectConfig,
        EffectBehaviorOverrideConfig? prefabOverride,
        out float percent)
    {
        percent = 0f;
        if (prefabOverride?.StaminaRestore?.Value.HasValue == true)
        {
            percent = prefabOverride.StaminaRestore.Value.Value;
            return true;
        }

        if (effectConfig.StaminaRestore.Value > 0f)
        {
            percent = effectConfig.StaminaRestore.Value;
            return true;
        }

        return false;
    }

    private static int ConvertAdrenalineRestorePercentToNativeValue(float percent)
    {
        if (percent <= 0f)
        {
            return 0;
        }

        return Mathf.Max(1, Mathf.RoundToInt(percent * 3.5f));
    }

    private static float ResolveConfiguredHasteMoveSpeedMultiplier(float defaultValue)
    {
        if (!TryGetConfiguredEffectForCurrentWeapon(
                "haste",
                out _,
                out ConfiguredWarfareEffectLookup? lookup) ||
            lookup == null)
        {
            return defaultValue;
        }

        EffectBehaviorConfig effectConfig = lookup.EffectConfig;
        EffectBehaviorOverrideConfig? prefabOverride = lookup.PrefabOverride;
        if (prefabOverride?.MoveSpeedMultiplier.HasValue == true)
        {
            return Mathf.Max(0f, prefabOverride.MoveSpeedMultiplier.Value);
        }

        return !Mathf.Approximately(effectConfig.MoveSpeedMultiplier, 1f)
            ? Mathf.Max(0f, effectConfig.MoveSpeedMultiplier)
            : defaultValue;
    }

    private static bool TryGetCurrentAttackWeaponPrefabName(out string prefabName)
    {
        if (TryGetProjectileHitWeaponPrefabName(out prefabName))
        {
            return true;
        }

        prefabName = "";
        if (Player.m_localPlayer == null)
        {
            return false;
        }

        Attack? currentAttack = ((Humanoid)Player.m_localPlayer).m_currentAttack;
        if (currentAttack?.m_weapon?.m_dropPrefab == null)
        {
            return false;
        }

        prefabName = currentAttack.m_weapon.m_dropPrefab.name;
        return !string.IsNullOrWhiteSpace(prefabName);
    }

    private static bool TryGetProjectileHitWeaponPrefabName(out string prefabName)
    {
        prefabName = "";
        if (!WarfareTweaksRuntimeFacade.TryGetProjectileHitAttackContext(
                out string contextWeaponPrefabName,
                out bool secondaryAttack,
                out _,
                out bool disableCurrentAttackFallback) ||
            secondaryAttack ||
            disableCurrentAttackFallback ||
            string.IsNullOrWhiteSpace(contextWeaponPrefabName))
        {
            return false;
        }

        prefabName = contextWeaponPrefabName;
        return true;
    }

    internal static WarfareDotSourceDamageScope BeginWarfareDotSourceDamage(Character target, HitData hit)
    {
        if (target == null ||
            hit == null ||
            hit.GetAttacker() != Player.m_localPlayer ||
            !DirectWeaponHitContextSystem.IsDirectWeaponHitActive ||
            WeaponEffectManager.IsApplyingGeneratedEffectDamage ||
            !TryGetCurrentAttackWeaponPrefabName(out string prefabName))
        {
            return default;
        }

        float sourceDamage = hit.m_damage.GetTotalDamage();
        if (sourceDamage <= 0f)
        {
            return default;
        }

        WarfareDotSourceDamageContext context = new(target, prefabName, sourceDamage, target.GetHealth());
        ActiveDotSourceDamageContexts.Add(context);
        return new WarfareDotSourceDamageScope(context);
    }

    internal static void EndWarfareDotSourceDamage(WarfareDotSourceDamageScope scope)
    {
        if (scope.Context is not WarfareDotSourceDamageContext context)
        {
            return;
        }

        float actualDamage = Mathf.Max(0f, context.HealthBefore - context.Target.GetHealth());
        context.ApplyActualDamage(actualDamage);

        ActiveDotSourceDamageContexts.Remove(context);
    }

    private static bool TryGetActiveDotSourceDamage(Character target, string prefabName, out float sourceDamage)
    {
        sourceDamage = 0f;
        for (int i = ActiveDotSourceDamageContexts.Count - 1; i >= 0; i--)
        {
            WarfareDotSourceDamageContext context = ActiveDotSourceDamageContexts[i];
            if (context.Target == target &&
                string.Equals(context.WeaponPrefabName, prefabName, StringComparison.OrdinalIgnoreCase))
            {
                sourceDamage = context.SourceDamage;
                return sourceDamage > 0f;
            }
        }

        return false;
    }

    private static bool TryGetCurrentAttackRawDamage(string prefabName, out float sourceDamage)
    {
        sourceDamage = 0f;
        if (Player.m_localPlayer == null)
        {
            return false;
        }

        Attack? currentAttack = ((Humanoid)Player.m_localPlayer).m_currentAttack;
        ItemDrop.ItemData? weapon = currentAttack?.m_weapon;
        if (weapon?.m_dropPrefab == null ||
            !string.Equals(weapon.m_dropPrefab.name, prefabName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        HitData.DamageTypes damage = weapon.GetDamage();
        if (!Mathf.Approximately(currentAttack!.m_damageMultiplier, 1f))
        {
            damage.Modify(currentAttack.m_damageMultiplier);
        }

        sourceDamage = damage.GetTotalDamage();
        return sourceDamage > 0f;
    }

    private static void StoreWarfareBleedTuning(StatusEffect status, WarfareBleedTuning tuning)
    {
        BleedTuningsByStatus.Remove(status);
        WarfareBleedTuningState state = new(tuning);
        BleedTuningsByStatus.Add(status, state);
        RegisterWarfareBleedStatusWithActiveSourceContext(status, tuning.PrefabName, state);
    }

    private static bool TryGetStoredWarfareBleedTuning(StatusEffect status, out WarfareBleedTuningState? tuning)
    {
        return BleedTuningsByStatus.TryGetValue(status, out tuning);
    }

    private static void RegisterWarfareBleedStatusWithActiveSourceContext(
        StatusEffect status,
        string prefabName,
        WarfareBleedTuningState state)
    {
        if (status.m_character == null || !state.UseSourceDamageDot)
        {
            return;
        }

        for (int i = ActiveDotSourceDamageContexts.Count - 1; i >= 0; i--)
        {
            WarfareDotSourceDamageContext context = ActiveDotSourceDamageContexts[i];
            if (context.Target == status.m_character &&
                string.Equals(context.WeaponPrefabName, prefabName, StringComparison.OrdinalIgnoreCase))
            {
                context.RegisterState(state);
                return;
            }
        }
    }

    private static bool TryGetIntField(object instance, string fieldName, out int value)
    {
        value = 0;
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return false;
        }

        object? fieldValue = field.GetValue(instance);
        if (fieldValue == null)
        {
            return false;
        }

        value = Convert.ToInt32(fieldValue);
        return true;
    }

    private static bool TrySetIntField(object instance, string fieldName, int value)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return false;
        }

        field.SetValue(instance, value);
        return true;
    }

    private static bool TryFindRegistration(string effectId, out WarfareBuiltInEffectRegistration? registration)
    {
        registration = null;
        string key = effectId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return RegistrationsByEffectId.TryGetValue(key, out registration);
    }

    private static Type? FindLoadedType(string fullTypeName)
    {
        string typeName = fullTypeName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        if (LoadedTypesByName.TryGetValue(typeName, out Type? cachedType))
        {
            return cachedType;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName, throwOnError: false);
            if (type != null)
            {
                LoadedTypesByName[typeName] = type;
                return type;
            }
        }

        return null;
    }

    private static bool IsWarfareLoaded()
    {
        return FindLoadedType("Warfare.WarfarePlugin") != null ||
               FindLoadedType("WarfareFireAndIce.WarfareFireAndIcePlugin") != null ||
               BuiltInRegistrations.Any(registration => registration.EffectTypeNames.Any(typeName => FindLoadedType(typeName) != null));
    }

    private static bool TryResolveAnyLoadedEffectTypeName(WarfareBuiltInEffectRegistration registration, out string effectTypeName)
    {
        foreach (string candidate in registration.EffectTypeNames)
        {
            if (FindLoadedType(candidate) != null)
            {
                effectTypeName = candidate;
                return true;
            }
        }

        effectTypeName = "";
        return false;
    }

    private static string? ResolveEffectTypeNameForPrefab(WarfareBuiltInEffectRegistration registration, string prefabName)
    {
        string? defaultEffectTypeName = registration.GetDefaultEffectTypeName(prefabName);
        if (!string.IsNullOrWhiteSpace(defaultEffectTypeName))
        {
            return defaultEffectTypeName;
        }

        return registration.EffectTypeNames.FirstOrDefault(typeName => FindLoadedType(typeName) != null);
    }

    private static bool ShouldSilentlySkipMissingOptionalEffect(
        WarfareBuiltInEffectRegistration registration,
        EffectBehaviorConfig effectConfig)
    {
        List<string> configuredPrefabNames = GetConfiguredPrefabNames(effectConfig)
            .Where(prefabName => !string.IsNullOrWhiteSpace(prefabName))
            .ToList();
        return configuredPrefabNames.Count > 0 &&
               configuredPrefabNames.All(prefabName => ShouldSilentlySkipMissingOptionalDefaultPrefab(registration, prefabName));
    }

    private static bool ShouldSilentlySkipMissingOptionalDefaultPrefab(
        WarfareBuiltInEffectRegistration registration,
        string prefabName)
    {
        string? defaultEffectTypeName = registration.GetDefaultEffectTypeName(prefabName);
        return defaultEffectTypeName != null &&
               !string.IsNullOrWhiteSpace(defaultEffectTypeName) &&
               FindLoadedType(defaultEffectTypeName) == null;
    }

    private static void EnsureWarfareGreatbowStatusEffects(ObjectDB objectDb)
    {
        EnsureWarfareStatusEffect(
            objectDb,
            "Warfare_StatusEffects.StatusEffects.PiercingGreatbowMistlands+Warfare_PiercingGreatbowMistlands",
            "Warfare_PiercingGreatbowMistlands");
        EnsureWarfareStatusEffect(
            objectDb,
            "Warfare_StatusEffects.StatusEffects.PiercingGreatbowModer+Warfare_PiercingGreatbowModer",
            "Warfare_PiercingGreatbowModer");
        EnsureWarfareStatusEffect(
            objectDb,
            "Warfare_StatusEffects.StatusEffects.PiercingGreatbowPlains+Warfare_PiercingGreatbowPlains",
            "Warfare_PiercingGreatbowPlains");
    }

    private static void EnsureWarfareStatusEffect(ObjectDB objectDb, string typeName, string statusName)
    {
        if (objectDb?.m_StatusEffects == null ||
            objectDb.m_StatusEffects.Exists(statusEffect => statusEffect != null && ((UnityEngine.Object)statusEffect).name == statusName))
        {
            return;
        }

        Type? statusType = FindLoadedType(typeName);
        if (statusType == null || !typeof(StatusEffect).IsAssignableFrom(statusType))
        {
            return;
        }

        StatusEffect? status = ScriptableObject.CreateInstance(statusType) as StatusEffect;
        if (status == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(((UnityEngine.Object)status).name))
        {
            ((UnityEngine.Object)status).name = statusName;
        }

        objectDb.m_StatusEffects.Add(status);
        WarfareTweaksPlugin.ModLogger.LogInfo($"Registered missing Warfare status effect '{statusName}'.");
    }

    private static bool ContainsObjectDbItem(ObjectDB objectDb, string prefabName)
    {
        if (objectDb?.m_items == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        ObjectDbItemNameCache cache = GetObjectDbItemNameCache(objectDb);
        return cache.ItemNames.Contains(prefabName);
    }

    private static ObjectDbItemNameCache GetObjectDbItemNameCache(ObjectDB objectDb)
    {
        int itemCount = objectDb.m_items?.Count ?? 0;
        if (ObjectDbItemNameCaches.TryGetValue(objectDb, out ObjectDbItemNameCache? cache) &&
            cache.ItemCount == itemCount)
        {
            return cache;
        }

        HashSet<string> itemNames = new(StringComparer.OrdinalIgnoreCase);
        if (objectDb.m_items != null)
        {
            foreach (GameObject item in objectDb.m_items)
            {
                if (item != null)
                {
                    itemNames.Add(item.name);
                }
            }
        }

        cache = new ObjectDbItemNameCache(itemCount, itemNames);
        ObjectDbItemNameCaches[objectDb] = cache;
        return cache;
    }

    private static bool TryApplyChainLightningConfig(
        ObjectDB objectDb,
        string effectId,
        EffectBehaviorConfig effectConfig,
        out int appliedCount)
    {
        appliedCount = 0;
        if (!IsChainLightningConfig(effectId, effectConfig))
        {
            return false;
        }

        string chainPrefabName = ResolveChainLightningPrefabName(effectId, effectConfig);
        GameObject? chainPrefab = FindWarfarePrefab(objectDb, chainPrefabName);
        if (chainPrefab == null)
        {
            if (ZNetScene.instance == null)
            {
                return true;
            }

            if (WarfareTweaksWarningLog.TryMarkReported($"warfare_chain_lightning_prefab_missing_{chainPrefabName}"))
            {
                WarfareTweaksPlugin.ModLogger.LogWarning(
                    $"Skipping warfare effect '{effectId}': prefab '{chainPrefabName}' was not found.");
            }

            return true;
        }

        if (TryTuneChainLightningAoe(chainPrefabName, chainPrefab, effectConfig))
        {
            appliedCount++;
        }

        SuppressMissingDefaultChainLightningAssignments(objectDb, chainPrefabName, effectConfig, ref appliedCount);

        if (effectConfig.Prefabs == null)
        {
            return true;
        }

        foreach ((string configuredPrefabName, EffectBehaviorOverrideConfig? prefabOverride) in effectConfig.Prefabs)
        {
            string prefabName = configuredPrefabName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                continue;
            }

            ItemDrop.ItemData.SharedData? sharedData = FindItemSharedData(objectDb, prefabName);
            if (sharedData == null)
            {
                if (WarfareTweaksWarningLog.TryMarkReported($"warfare_chain_lightning_weapon_missing_{prefabName}"))
                {
                    WarfareTweaksPlugin.ModLogger.LogWarning(
                        $"Skipping warfare chain lightning on '{prefabName}': prefab was not found in ObjectDB.");
                }

                continue;
            }

            Attack attack = sharedData.m_attack;
            if (attack == null)
            {
                if (WarfareTweaksWarningLog.TryMarkReported($"warfare_chain_lightning_attack_missing_{prefabName}"))
                {
                    WarfareTweaksPlugin.ModLogger.LogWarning(
                        $"Skipping warfare chain lightning on '{prefabName}': primary attack is missing.");
                }

                continue;
            }

            StoreAttackSpawnOverride(prefabName, attack);
            float chance = Mathf.Clamp01(Mathf.Max(0f, prefabOverride?.ProcChance ?? effectConfig.ProcChance) * 0.01f);
            if (chance <= 0f)
            {
                attack.m_spawnOnHit = null;
                attack.m_spawnOnHitChance = 0f;
            }
            else
            {
                attack.m_spawnOnHit = chainPrefab;
                attack.m_spawnOnHitChance = chance;
            }

            appliedCount++;
        }

        return true;
    }

    private static void SuppressMissingDefaultChainLightningAssignments(
        ObjectDB objectDb,
        string chainPrefabName,
        EffectBehaviorConfig effectConfig,
        ref int appliedCount)
    {
        if (!string.Equals(chainPrefabName, "ChainLightningMistlands_TW", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string prefabName in ChainLightningMistlandsDefaultPrefabs)
        {
            if (WarfareEffectConfigHelpers.HasPrefabAssignment(effectConfig, prefabName))
            {
                continue;
            }

            ItemDrop.ItemData.SharedData? sharedData = FindItemSharedData(objectDb, prefabName);
            Attack? attack = sharedData?.m_attack;
            if (attack == null)
            {
                continue;
            }

            StoreAttackSpawnOverride(prefabName, attack);
            attack.m_spawnOnHit = null;
            attack.m_spawnOnHitChance = 0f;
            appliedCount++;
        }
    }

    private static void SuppressDefaultChainLightningAssignments(ObjectDB objectDb, ref int appliedCount)
    {
        SuppressMissingDefaultChainLightningAssignments(
            objectDb,
            "ChainLightningMistlands_TW",
            new EffectBehaviorConfig(),
            ref appliedCount);
    }

    private static bool IsChainLightningConfig(string effectId, EffectBehaviorConfig effectConfig)
    {
        string type = effectConfig.Type?.Trim() ?? "";
        return effectId.Contains("ChainLightning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "chainLightning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "chainLightningMistlands", StringComparison.OrdinalIgnoreCase) ||
               effectConfig.LightningDamage.HasValue ||
               effectConfig.Radius.HasValue ||
               effectConfig.Ttl.HasValue ||
               effectConfig.HitInterval.HasValue;
    }

    private static string ResolveChainLightningPrefabName(string effectId, EffectBehaviorConfig effectConfig)
    {
        if (!string.IsNullOrWhiteSpace(effectConfig.Prefab))
        {
            return effectConfig.Prefab.Trim();
        }

        if (effectId.Contains("ChainLightning", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effectId, "chainLightning", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effectId, "chainLightningMistlands", StringComparison.OrdinalIgnoreCase))
        {
            return effectId.Trim();
        }

        return "ChainLightningMistlands_TW";
    }

    private static bool TryTuneChainLightningAoe(string prefabName, GameObject chainPrefab, EffectBehaviorConfig effectConfig)
    {
        Aoe? aoe = chainPrefab.GetComponent<Aoe>() ?? chainPrefab.GetComponentInChildren<Aoe>(true);
        if (aoe == null)
        {
            if (WarfareTweaksWarningLog.TryMarkReported($"warfare_chain_lightning_aoe_missing_{prefabName}"))
            {
                WarfareTweaksPlugin.ModLogger.LogWarning(
                    $"Skipping warfare chain lightning tuning on '{prefabName}': Aoe component was not found.");
            }

            return false;
        }

        StoreAoeOverride(prefabName, aoe);
        if (effectConfig.LightningDamage.HasValue)
        {
            aoe.m_damage.m_lightning = Mathf.Max(0f, effectConfig.LightningDamage.Value);
        }

        if (effectConfig.Radius.HasValue)
        {
            aoe.m_radius = Mathf.Max(0f, effectConfig.Radius.Value);
        }

        if (effectConfig.Ttl.HasValue)
        {
            aoe.m_ttl = Mathf.Max(0.01f, effectConfig.Ttl.Value);
        }

        if (effectConfig.HitInterval.HasValue)
        {
            aoe.m_hitInterval = Mathf.Max(0f, effectConfig.HitInterval.Value);
        }

        return effectConfig.LightningDamage.HasValue ||
               effectConfig.Radius.HasValue ||
               effectConfig.Ttl.HasValue ||
               effectConfig.HitInterval.HasValue;
    }

    private static GameObject? FindWarfarePrefab(ObjectDB objectDb, string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return null;
        }

        GameObject? scenePrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
        if (scenePrefab != null)
        {
            return scenePrefab;
        }

        if (objectDb?.m_items != null)
        {
            foreach (GameObject item in objectDb.m_items)
            {
                if (item != null && string.Equals(item.name, prefabName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static ItemDrop.ItemData.SharedData? FindItemSharedData(ObjectDB objectDb, string prefabName)
    {
        if (objectDb?.m_items == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return null;
        }

        foreach (GameObject item in objectDb.m_items)
        {
            if (item == null || !string.Equals(item.name, prefabName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return item.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
        }

        return null;
    }

    private static EffectBehaviorOverrideConfig? TryGetPrefabOverride(EffectBehaviorConfig effectConfig, string prefabName)
    {
        if (effectConfig.Prefabs == null)
        {
            return null;
        }

        foreach ((string configuredPrefabName, EffectBehaviorOverrideConfig? prefabOverride) in effectConfig.Prefabs)
        {
            if (string.Equals(configuredPrefabName?.Trim(), prefabName, StringComparison.OrdinalIgnoreCase))
            {
                return prefabOverride ?? new EffectBehaviorOverrideConfig();
            }
        }

        return null;
    }

    private static int? ResolveConfiguredWarfareValue(
        WarfareBuiltInEffectRegistration registration,
        EffectBehaviorConfig effectConfig,
        EffectBehaviorOverrideConfig? prefabOverride,
        string prefabName)
    {
        if (prefabOverride?.Value.HasValue == true)
        {
            return prefabOverride.Value.Value;
        }

        if (effectConfig.Value.HasValue)
        {
            return effectConfig.Value.Value;
        }

        if (string.Equals(registration.Id, "adrenaline", StringComparison.OrdinalIgnoreCase) &&
            TryResolveAdrenalineRestorePercent(effectConfig, prefabOverride, out float adrenalinePercent))
        {
            return ConvertAdrenalineRestorePercentToNativeValue(adrenalinePercent);
        }

        return registration.GetDefaultValue(prefabName) ?? GetImplicitWarfareValue(registration.Id);
    }

    private static int? GetImplicitWarfareValue(string effectId)
    {
        return effectId switch
        {
            "bleeding" or
            "bleedingSecondary" or
            "impale" or
            "burningSecondary" => 1,
            "lightningBurst" => 55,
            "decapitator4" or
            "decapitator5" or
            "bludgeoner" or
            "hackAndSlash" or
            "juggernaut" or
            "smashAndBash" or
            "smasher" => 100,
            "pierceGreatbowFireAndIce" => 100,
            "piercingGreatbowMistlands" => 60,
            "piercingGreatbowModer" or
            "piercingGreatbowPlains" => 40,
            _ => null
        };
    }

    private static void ResetAppliedConfiguredAssignments()
    {
        if (AppliedConfiguredAssignments.Count == 0)
        {
            return;
        }

        foreach (WarfareAppliedAssignment assignment in AppliedConfiguredAssignments.Values)
        {
            Type? effectType = FindLoadedType(assignment.EffectTypeName);
            if (effectType == null)
            {
                continue;
            }

            TryRestoreTargetAssignment(effectType, assignment, out _);
        }

        AppliedConfiguredAssignments.Clear();
    }

    private static void ResetAppliedPrefabOverrides()
    {
        foreach (WarfareAttackSpawnOverrideState state in AppliedAttackSpawnOverrides.Values)
        {
            if (state.Attack == null)
            {
                continue;
            }

            state.Attack.m_spawnOnHit = state.OriginalSpawnOnHit;
            state.Attack.m_spawnOnHitChance = state.OriginalSpawnOnHitChance;
        }

        AppliedAttackSpawnOverrides.Clear();

        foreach (WarfareAoeOverrideState state in AppliedAoeOverrides.Values)
        {
            if (state.Aoe == null)
            {
                continue;
            }

            state.Aoe.m_damage = state.OriginalDamage;
            state.Aoe.m_radius = state.OriginalRadius;
            state.Aoe.m_ttl = state.OriginalTtl;
            state.Aoe.m_hitInterval = state.OriginalHitInterval;
        }

        AppliedAoeOverrides.Clear();

        foreach (WarfareAttackStatusEffectOverrideState state in AppliedAttackStatusEffectOverrides.Values)
        {
            if (state.ItemDrop?.m_itemData?.m_shared == null)
            {
                continue;
            }

            state.ItemDrop.m_itemData.m_shared.m_attackStatusEffect = state.OriginalAttackStatusEffect;
        }

        AppliedAttackStatusEffectOverrides.Clear();
    }

    private static void StoreAttackSpawnOverride(string prefabName, Attack attack)
    {
        if (AppliedAttackSpawnOverrides.ContainsKey(prefabName))
        {
            return;
        }

        AppliedAttackSpawnOverrides[prefabName] = new WarfareAttackSpawnOverrideState(
            attack,
            attack.m_spawnOnHit,
            attack.m_spawnOnHitChance);
    }

    private static void StoreAoeOverride(string prefabName, Aoe aoe)
    {
        if (AppliedAoeOverrides.ContainsKey(prefabName))
        {
            return;
        }

        AppliedAoeOverrides[prefabName] = new WarfareAoeOverrideState(
            aoe,
            aoe.m_damage,
            aoe.m_radius,
            aoe.m_ttl,
            aoe.m_hitInterval);
    }

    private static int RemoveBuiltInTargetAssignments(IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        int removedCount = 0;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (WarfareBuiltInEffectRegistration registration in BuiltInRegistrations)
        {
            foreach (WarfareEffectTypeSpec spec in registration.EffectTypeSpecs)
            {
                Type? effectType = FindLoadedType(spec.EffectTypeName);
                if (effectType == null)
                {
                    continue;
                }

                foreach (string prefabName in registration.PrefabNames)
                {
                    if (RemoveTargetAssignmentOnce(effectType, prefabName, seen))
                    {
                        removedCount++;
                    }
                }

                foreach ((string effectId, EffectBehaviorConfig effectConfig) in effectConfigs)
                {
                    if (effectConfig == null ||
                        !TryFindRegistration(effectId, out WarfareBuiltInEffectRegistration? configuredRegistration) ||
                        !string.Equals(configuredRegistration!.Id, registration.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string prefabName in GetConfiguredPrefabNames(effectConfig))
                    {
                        if (RemoveTargetAssignmentOnce(effectType, prefabName, seen))
                        {
                            removedCount++;
                        }
                    }
                }
            }
        }

        return removedCount;
    }

    private static bool RemoveTargetAssignmentOnce(Type effectType, string prefabName, HashSet<string> seen)
    {
        string normalizedPrefabName = prefabName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedPrefabName) ||
            !seen.Add($"{effectType.FullName}:{normalizedPrefabName}"))
        {
            return false;
        }

        return TryRemoveTargetAssignment(effectType, normalizedPrefabName, out bool removed, out _) && removed;
    }

    private static bool TryRemoveTargetAssignment(Type effectType, string prefabName, out bool removed, out string reason)
    {
        removed = false;
        reason = "";
        if (!TryGetTargets(effectType, out object? targets, out reason))
        {
            return false;
        }

        if (targets is IDictionary dictionary)
        {
            if (dictionary.Contains(prefabName))
            {
                dictionary.Remove(prefabName);
                removed = true;
            }

            return true;
        }

        if (!TryInvokeSetTargetContains(targets!, prefabName, out bool contains, out reason))
        {
            return false;
        }

        if (!contains)
        {
            return true;
        }

        if (!TryInvokeSetTargetRemove(targets!, prefabName, out reason))
        {
            return false;
        }

        removed = true;
        return true;
    }

    private static int SuppressNativeAttackStatusEffects(
        ObjectDB objectDb,
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        if (objectDb == null)
        {
            return 0;
        }

        int removedCount = 0;
        HashSet<string> prefabNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (WarfareBuiltInEffectRegistration registration in BuiltInRegistrations)
        {
            foreach (string prefabName in registration.PrefabNames)
            {
                prefabNames.Add(prefabName);
            }

            if (!TryFindConfiguredEffect(effectConfigs, registration, out EffectBehaviorConfig? effectConfig) ||
                effectConfig == null)
            {
                continue;
            }

            foreach (string prefabName in GetConfiguredPrefabNames(effectConfig))
            {
                prefabNames.Add(prefabName);
            }
        }

        foreach (string prefabName in prefabNames)
        {
            if (TrySuppressNativeAttackStatusEffect(objectDb, prefabName, IsWarfareNativeAttackStatusEffect))
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    private static bool TrySuppressNativeAttackStatusEffect(
        ObjectDB objectDb,
        string prefabName,
        Func<StatusEffect, bool> shouldSuppress)
    {
        GameObject prefab = objectDb.GetItemPrefab(prefabName);
        if (prefab == null)
        {
            return false;
        }

        ItemDrop itemDrop = prefab.GetComponent<ItemDrop>() ?? prefab.GetComponentInChildren<ItemDrop>();
        if (itemDrop?.m_itemData?.m_shared == null)
        {
            return false;
        }

        StatusEffect? attackStatusEffect = itemDrop.m_itemData.m_shared.m_attackStatusEffect;
        if (attackStatusEffect == null || !shouldSuppress(attackStatusEffect))
        {
            return false;
        }

        if (!AppliedAttackStatusEffectOverrides.ContainsKey(prefabName))
        {
            AppliedAttackStatusEffectOverrides[prefabName] =
                new WarfareAttackStatusEffectOverrideState(itemDrop, attackStatusEffect);
        }

        itemDrop.m_itemData.m_shared.m_attackStatusEffect = null;
        return true;
    }

    private static bool IsWarfareNativeAttackStatusEffect(StatusEffect statusEffect)
    {
        string prefabName = ((UnityEngine.Object)statusEffect).name ?? "";
        string statusName = statusEffect.m_name ?? "";
        string tooltip = statusEffect.m_tooltip ?? "";
        return IsWarfareStatusToken(statusName) ||
               IsWarfareStatusToken(tooltip) ||
               IsWarfareStatusPrefabName(prefabName);
    }

    private static bool IsWarfareStatusToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1);
        }

        return trimmed.StartsWith("SE_", StringComparison.OrdinalIgnoreCase) &&
               trimmed.EndsWith("_TW", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWarfareStatusPrefabName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IsWarfareStatusToken(value) ||
               value.StartsWith("Warfare_", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("WarfareFireAndIce_", StringComparison.OrdinalIgnoreCase);
    }

    // Target assignment helpers isolate reflection against Warfare's Targets/AddToItem APIs.
    private static bool TryApplyTargetAssignment(
        Type effectType,
        string prefabName,
        int? value,
        out WarfareAppliedAssignment? assignment,
        out string reason)
    {
        assignment = null;
        reason = "";

        if (!TryGetTargets(effectType, out object? targets, out reason))
        {
            return TryInvokeAddToItemAuthoritative(effectType, prefabName, value, out assignment, out reason);
        }

        if (targets is IDictionary dictionary)
        {
            if (!value.HasValue)
            {
                reason = $"Warfare type '{effectType.FullName}' requires a value.";
                return false;
            }

            bool hadOriginalTarget = dictionary.Contains(prefabName);
            int? originalValue = hadOriginalTarget && dictionary[prefabName] != null
                ? Convert.ToInt32(dictionary[prefabName])
                : null;
            dictionary[prefabName] = value.Value;
            assignment = new WarfareAppliedAssignment(effectType.FullName!, prefabName, true, hadOriginalTarget, originalValue, value);
            return true;
        }

        if (!TryInvokeSetTargetContains(targets!, prefabName, out bool hadHashTarget, out reason))
        {
            return TryInvokeAddToItemAuthoritative(effectType, prefabName, value, out assignment, out reason);
        }

        if (value.HasValue)
        {
            reason = $"Warfare type '{effectType.FullName}' does not accept a value.";
            return false;
        }

        if (!hadHashTarget && !TryInvokeSetTargetAdd(targets!, prefabName, out reason))
        {
            return false;
        }

        assignment = new WarfareAppliedAssignment(effectType.FullName!, prefabName, false, hadHashTarget, null, null);
        return true;
    }

    private static bool TryInvokeAddToItemAuthoritative(
        Type effectType,
        string prefabName,
        int? value,
        out WarfareAppliedAssignment? assignment,
        out string reason)
    {
        bool previousAllow = _allowWarfareAddToItem;
        _allowWarfareAddToItem = true;
        try
        {
            return TryInvokeAddToItem(effectType, prefabName, value, out assignment, out reason);
        }
        finally
        {
            _allowWarfareAddToItem = previousAllow;
        }
    }

    private static bool TryRestoreTargetAssignment(Type effectType, WarfareAppliedAssignment assignment, out string reason)
    {
        reason = "";
        if (!TryGetTargets(effectType, out object? targets, out reason))
        {
            return false;
        }

        if (assignment.UsesValueTarget)
        {
            if (targets is not IDictionary dictionary)
            {
                reason = $"Warfare type '{effectType.FullName}' no longer exposes a dictionary Targets field.";
                return false;
            }

            if (!dictionary.Contains(assignment.PrefabName))
            {
                return true;
            }

            int? currentValue = dictionary[assignment.PrefabName] != null
                ? Convert.ToInt32(dictionary[assignment.PrefabName])
                : null;
            if (currentValue != assignment.AppliedValue)
            {
                return true;
            }

            if (assignment.HadOriginalTarget)
            {
                dictionary[assignment.PrefabName] = assignment.OriginalValue ?? 0;
            }
            else
            {
                dictionary.Remove(assignment.PrefabName);
            }

            return true;
        }

        if (!TryInvokeSetTargetContains(targets!, assignment.PrefabName, out bool contains, out reason))
        {
            return false;
        }

        if (contains && !assignment.HadOriginalTarget)
        {
            return TryInvokeSetTargetRemove(targets!, assignment.PrefabName, out reason);
        }

        return true;
    }

    private static WarfareTargetAccessors GetTargetAccessors(Type effectType)
    {
        if (!TargetAccessorsByEffectType.TryGetValue(effectType, out WarfareTargetAccessors? accessors))
        {
            accessors = new WarfareTargetAccessors(effectType);
            TargetAccessorsByEffectType[effectType] = accessors;
        }

        return accessors;
    }

    private static WarfareTargetSetAccessors GetTargetSetAccessors(Type targetType)
    {
        if (!TargetSetAccessorsByType.TryGetValue(targetType, out WarfareTargetSetAccessors? accessors))
        {
            accessors = new WarfareTargetSetAccessors(targetType);
            TargetSetAccessorsByType[targetType] = accessors;
        }

        return accessors;
    }

    private static bool TryGetTargets(Type effectType, out object? targets, out string reason)
    {
        targets = null;
        reason = "";
        FieldInfo? targetsField = GetTargetAccessors(effectType).TargetsField;
        if (targetsField == null)
        {
            reason = $"Warfare type '{effectType.FullName}' has no Targets field.";
            return false;
        }

        targets = targetsField.GetValue(null);
        if (targets == null)
        {
            reason = $"Warfare type '{effectType.FullName}' has a null Targets field.";
            return false;
        }

        return true;
    }

    private static bool TryInvokeSetTargetContains(object targets, string prefabName, out bool contains, out string reason)
    {
        contains = false;
        reason = "";
        Type targetsType = targets.GetType();
        MethodInfo? containsMethod = GetTargetSetAccessors(targetsType).ContainsMethod;
        if (containsMethod == null)
        {
            reason = $"Warfare Targets type '{targetsType.FullName}' has no Contains(string) method.";
            return false;
        }

        contains = (bool)containsMethod.Invoke(targets, new object[] { prefabName });
        return true;
    }

    private static bool TryInvokeSetTargetAdd(object targets, string prefabName, out string reason)
    {
        reason = "";
        Type targetsType = targets.GetType();
        MethodInfo? addMethod = GetTargetSetAccessors(targetsType).AddMethod;
        if (addMethod == null)
        {
            reason = $"Warfare Targets type '{targetsType.FullName}' has no Add(string) method.";
            return false;
        }

        addMethod.Invoke(targets, new object[] { prefabName });
        return true;
    }

    private static bool TryInvokeSetTargetRemove(object targets, string prefabName, out string reason)
    {
        reason = "";
        Type targetsType = targets.GetType();
        MethodInfo? removeMethod = GetTargetSetAccessors(targetsType).RemoveMethod;
        if (removeMethod == null)
        {
            reason = $"Warfare Targets type '{targetsType.FullName}' has no Remove(string) method.";
            return false;
        }

        removeMethod.Invoke(targets, new object[] { prefabName });
        return true;
    }

    private static bool TryInvokeAddToItem(
        Type effectType,
        string prefabName,
        int? value,
        out WarfareAppliedAssignment? assignment,
        out string reason)
    {
        assignment = null;
        reason = "";
        MethodInfo[] addToItemMethods = GetTargetAccessors(effectType).AddToItemMethods;
        if (addToItemMethods.Length == 0)
        {
            reason = $"Warfare type '{effectType.FullName}' has no AddToItem method.";
            return false;
        }

        MethodInfo? selectedMethod = null;
        object?[] args;
        if (value.HasValue)
        {
            selectedMethod = addToItemMethods.FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(string) &&
                       CanConvertWarfareValue(parameters[1].ParameterType);
            });
            if (selectedMethod == null)
            {
                reason = $"Warfare type '{effectType.FullName}' has no AddToItem(string, value) overload.";
                return false;
            }

            Type valueType = selectedMethod.GetParameters()[1].ParameterType;
            args = new object?[] { prefabName, ConvertWarfareValue(value.Value, valueType) };
        }
        else
        {
            selectedMethod = addToItemMethods.FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(string);
            });
            if (selectedMethod == null)
            {
                reason = $"Warfare type '{effectType.FullName}' requires a value for AddToItem.";
                return false;
            }

            args = new object?[] { prefabName };
        }

        try
        {
            selectedMethod.Invoke(null, args);
            assignment = new WarfareAppliedAssignment(effectType.FullName!, prefabName, value.HasValue, false, null, value);
            return true;
        }
        catch (TargetInvocationException exception)
        {
            reason = exception.InnerException?.Message ?? exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static bool CanConvertWarfareValue(Type valueType)
    {
        Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return type == typeof(int) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(long) ||
               type == typeof(short) ||
               type == typeof(byte);
    }

    private static object ConvertWarfareValue(int value, Type valueType)
    {
        Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (type == typeof(float))
        {
            return (float)value;
        }

        if (type == typeof(double))
        {
            return (double)value;
        }

        if (type == typeof(long))
        {
            return (long)value;
        }

        if (type == typeof(short))
        {
            return (short)value;
        }

        if (type == typeof(byte))
        {
            return (byte)Mathf.Clamp(value, byte.MinValue, byte.MaxValue);
        }

        return value;
    }

    private sealed class ConfiguredWarfareEffectLookup
    {
        public ConfiguredWarfareEffectLookup(
            WarfareBuiltInEffectRegistration registration,
            EffectBehaviorConfig effectConfig,
            EffectBehaviorOverrideConfig? prefabOverride)
        {
            Registration = registration;
            EffectConfig = effectConfig;
            PrefabOverride = prefabOverride;
        }

        public WarfareBuiltInEffectRegistration Registration { get; }

        public EffectBehaviorConfig EffectConfig { get; }

        public EffectBehaviorOverrideConfig? PrefabOverride { get; }
    }

    private sealed class WarfareTargetAccessors
    {
        public WarfareTargetAccessors(Type effectType)
        {
            TargetsField = effectType.GetField("Targets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            AddToItemMethods = effectType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == "AddToItem")
                .ToArray();
        }

        public FieldInfo? TargetsField { get; }

        public MethodInfo[] AddToItemMethods { get; }
    }

    private sealed class WarfareTargetSetAccessors
    {
        public WarfareTargetSetAccessors(Type targetType)
        {
            ContainsMethod = targetType.GetMethod("Contains", new[] { typeof(string) });
            AddMethod = targetType.GetMethod("Add", new[] { typeof(string) });
            RemoveMethod = targetType.GetMethod("Remove", new[] { typeof(string) });
        }

        public MethodInfo? ContainsMethod { get; }

        public MethodInfo? AddMethod { get; }

        public MethodInfo? RemoveMethod { get; }
    }

    private sealed class WarfareBuiltInEffectRegistration
    {
        private readonly Dictionary<string, WarfareDefaultAssignment> _defaultAssignmentsByPrefabName;

        public WarfareBuiltInEffectRegistration(string id, string patchTypeName, string[] prefabSpecs, params string[] aliases)
            : this(
                id,
                new[]
                {
                    new WarfareEffectTypeSpec(WarfareStatusEffectsNamespace, patchTypeName, prefabSpecs)
                },
                aliases)
        {
        }

        public WarfareBuiltInEffectRegistration(string id, WarfareEffectTypeSpec[] effectTypeSpecs, params string[] aliases)
        {
            Id = id;
            EffectTypeSpecs = (effectTypeSpecs ?? Array.Empty<WarfareEffectTypeSpec>())
                .Where(spec => spec != null && !string.IsNullOrWhiteSpace(spec.EffectTypeName))
                .ToArray();
            if (EffectTypeSpecs.Length == 0)
            {
                EffectTypeSpecs = new[] { new WarfareEffectTypeSpec(WarfareStatusEffectsNamespace, id, Array.Empty<string>()) };
            }

            _defaultAssignmentsByPrefabName = ParsePrefabSpecs(EffectTypeSpecs);
            PrefabNames = _defaultAssignmentsByPrefabName.Keys.ToArray();
            EffectIds = new[] { id }.Concat(aliases).ToArray();
        }

        public string Id { get; }

        public WarfareEffectTypeSpec[] EffectTypeSpecs { get; }

        public string StatusEffectsNamespace => EffectTypeSpecs[0].StatusEffectsNamespace;

        public string PatchTypeName => EffectTypeSpecs[0].PatchTypeName;

        public string CharacterDamagePatchTypeName => $"{StatusEffectsNamespace}.{PatchTypeName}+Character_Damage_Patch";

        public string EffectTypeName => EffectTypeSpecs[0].EffectTypeName;

        public string[] EffectTypeNames => EffectTypeSpecs.Select(spec => spec.EffectTypeName).ToArray();

        public string[] PrefabNames { get; }

        public string[] EffectIds { get; }

        public bool RequiresValue => _defaultAssignmentsByPrefabName.Values.Any(assignment => assignment.Value.HasValue);

        public int? GetDefaultValue(string prefabName)
        {
            return _defaultAssignmentsByPrefabName.TryGetValue(prefabName, out WarfareDefaultAssignment? assignment)
                ? assignment.Value
                : null;
        }

        public string? GetDefaultEffectTypeName(string prefabName)
        {
            return _defaultAssignmentsByPrefabName.TryGetValue(prefabName, out WarfareDefaultAssignment? assignment)
                ? assignment.EffectTypeName
                : null;
        }

        public bool IsDefaultAssignment(string prefabName, int? value)
        {
            return _defaultAssignmentsByPrefabName.TryGetValue(prefabName, out WarfareDefaultAssignment? assignment) &&
                   assignment.Value == value;
        }

        private static Dictionary<string, WarfareDefaultAssignment> ParsePrefabSpecs(IEnumerable<WarfareEffectTypeSpec> effectTypeSpecs)
        {
            Dictionary<string, WarfareDefaultAssignment> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (WarfareEffectTypeSpec effectTypeSpec in effectTypeSpecs)
            {
                foreach (string prefabSpec in effectTypeSpec.PrefabSpecs)
                {
                    if (string.IsNullOrWhiteSpace(prefabSpec))
                    {
                        continue;
                    }

                    string[] parts = prefabSpec.Split(new[] { ':' }, 2);
                    string prefabName = parts[0].Trim();
                    if (string.IsNullOrWhiteSpace(prefabName))
                    {
                        continue;
                    }

                    int? value = null;
                    if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int parsedValue))
                    {
                        value = parsedValue;
                    }

                    values[prefabName] = new WarfareDefaultAssignment(effectTypeSpec.EffectTypeName, value);
                }
            }

            return values;
        }
    }

    private sealed class WarfareEffectTypeSpec
    {
        public WarfareEffectTypeSpec(string statusEffectsNamespace, string patchTypeName, string[] prefabSpecs)
        {
            StatusEffectsNamespace = statusEffectsNamespace;
            PatchTypeName = patchTypeName;
            PrefabSpecs = prefabSpecs ?? Array.Empty<string>();
            EffectTypeName = $"{StatusEffectsNamespace}.{PatchTypeName}";
        }

        public string StatusEffectsNamespace { get; }

        public string PatchTypeName { get; }

        public string EffectTypeName { get; }

        public string CharacterDamagePatchTypeName => $"{StatusEffectsNamespace}.{PatchTypeName}+Character_Damage_Patch";

        public string[] PrefabSpecs { get; }
    }

    private sealed class WarfareDefaultAssignment
    {
        public WarfareDefaultAssignment(string effectTypeName, int? value)
        {
            EffectTypeName = effectTypeName;
            Value = value;
        }

        public string EffectTypeName { get; }

        public int? Value { get; }
    }

    private sealed class WarfareAppliedAssignment
    {
        public WarfareAppliedAssignment(
            string effectTypeName,
            string prefabName,
            bool usesValueTarget,
            bool hadOriginalTarget,
            int? originalValue,
            int? appliedValue)
        {
            EffectTypeName = effectTypeName;
            PrefabName = prefabName;
            UsesValueTarget = usesValueTarget;
            HadOriginalTarget = hadOriginalTarget;
            OriginalValue = originalValue;
            AppliedValue = appliedValue;
        }

        public string EffectTypeName { get; }

        public string PrefabName { get; }

        public bool UsesValueTarget { get; }

        public bool HadOriginalTarget { get; }

        public int? OriginalValue { get; }

        public int? AppliedValue { get; }
    }

    private sealed class WarfareAttackSpawnOverrideState
    {
        public WarfareAttackSpawnOverrideState(
            Attack attack,
            GameObject? originalSpawnOnHit,
            float originalSpawnOnHitChance)
        {
            Attack = attack;
            OriginalSpawnOnHit = originalSpawnOnHit;
            OriginalSpawnOnHitChance = originalSpawnOnHitChance;
        }

        public Attack Attack { get; }

        public GameObject? OriginalSpawnOnHit { get; }

        public float OriginalSpawnOnHitChance { get; }
    }

    private sealed class WarfareAoeOverrideState
    {
        public WarfareAoeOverrideState(
            Aoe aoe,
            HitData.DamageTypes originalDamage,
            float originalRadius,
            float originalTtl,
            float originalHitInterval)
        {
            Aoe = aoe;
            OriginalDamage = originalDamage;
            OriginalRadius = originalRadius;
            OriginalTtl = originalTtl;
            OriginalHitInterval = originalHitInterval;
        }

        public Aoe Aoe { get; }

        public HitData.DamageTypes OriginalDamage { get; }

        public float OriginalRadius { get; }

        public float OriginalTtl { get; }

        public float OriginalHitInterval { get; }
    }

    private sealed class WarfareAttackStatusEffectOverrideState
    {
        public WarfareAttackStatusEffectOverrideState(ItemDrop itemDrop, StatusEffect originalAttackStatusEffect)
        {
            ItemDrop = itemDrop;
            OriginalAttackStatusEffect = originalAttackStatusEffect;
        }

        public ItemDrop ItemDrop { get; }

        public StatusEffect OriginalAttackStatusEffect { get; }
    }

    private sealed class WarfareBleedTuning
    {
        public string PrefabName { get; set; } = "";

        public int? StacksRequired { get; set; }

        public float? StackWindow { get; set; }

        public float? Duration { get; set; }

        public float? TickInterval { get; set; }

        public float? DamageFactor { get; set; }

        public int? NativeValue { get; set; }

        public float? SourceDamage { get; set; }

        public bool HasAnyValue =>
            StacksRequired.HasValue ||
            StackWindow.HasValue ||
            Duration.HasValue ||
            TickInterval.HasValue ||
            DamageFactor.HasValue;
    }

    private sealed class WarfareBleedTuningState
    {
        public WarfareBleedTuningState(WarfareBleedTuning tuning)
        {
            Duration = tuning.Duration;
            TickInterval = tuning.TickInterval;
            DamageFactor = tuning.DamageFactor;
            NativeValue = tuning.NativeValue;
            SourceDamage = tuning.SourceDamage;
        }

        public float? Duration { get; }

        public float? TickInterval { get; }

        public float? DamageFactor { get; }

        public int? NativeValue { get; }

        public float? SourceDamage { get; private set; }

        public bool UseSourceDamageDot => DamageFactor.HasValue;

        public void SetSourceDamage(float sourceDamage)
        {
            SourceDamage = Mathf.Max(0f, sourceDamage);
        }
    }

    private sealed class WarfareHasteTuningState
    {
        public WarfareHasteTuningState(float moveSpeedMultiplier)
        {
            MoveSpeedMultiplier = moveSpeedMultiplier;
        }

        public float MoveSpeedMultiplier { get; }
    }

    private sealed class ItemPrefabNameCacheEntry
    {
        public ItemPrefabNameCacheEntry(string prefabName)
        {
            PrefabName = prefabName;
        }

        public string PrefabName { get; }
    }

    private sealed class ObjectDbItemNameCache
    {
        public ObjectDbItemNameCache(int itemCount, HashSet<string> itemNames)
        {
            ItemCount = itemCount;
            ItemNames = itemNames;
        }

        public int ItemCount { get; }

        public HashSet<string> ItemNames { get; }
    }

    private sealed class WarfareDotSourceDamageContext
    {
        private readonly List<WarfareBleedTuningState> _states = new();

        public WarfareDotSourceDamageContext(Character target, string weaponPrefabName, float sourceDamage, float healthBefore)
        {
            Target = target;
            WeaponPrefabName = weaponPrefabName;
            SourceDamage = sourceDamage;
            HealthBefore = healthBefore;
        }

        public Character Target { get; }

        public string WeaponPrefabName { get; }

        public float SourceDamage { get; }

        public float HealthBefore { get; }

        public void RegisterState(WarfareBleedTuningState state)
        {
            if (!_states.Contains(state))
            {
                _states.Add(state);
            }
        }

        public void ApplyActualDamage(float actualDamage)
        {
            foreach (WarfareBleedTuningState state in _states)
            {
                state.SetSourceDamage(actualDamage);
            }
        }
    }
}

internal readonly struct WarfareDotSourceDamageScope
{
    public WarfareDotSourceDamageScope(object? context)
    {
        Context = context;
    }

    internal object? Context { get; }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class CharacterDamageWarfareDotSourceDamagePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Character __instance, HitData hit, out WarfareDotSourceDamageScope __state)
    {
        __state = WarfareCompat.BeginWarfareDotSourceDamage(__instance, hit);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(WarfareDotSourceDamageScope __state)
    {
        WarfareCompat.EndWarfareDotSourceDamage(__state);
    }
}
