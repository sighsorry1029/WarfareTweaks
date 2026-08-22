using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarfareTweaks;

internal static class WarfareGreatbowSoundCompat
{
    private enum PatchResult
    {
        Unchanged,
        Enabled,
        Added
    }

    private const string StandardBowFireSfxName = "sfx_bow_fire_TW";

    private static readonly string[] GreatbowPrefabNames =
    {
        "GreatbowModer_TW",
        "GreatbowBlackmetal_TW",
        "GreatbowDvergr_TW",
        "GreatbowNjord_TW",
        "GreatbowSurtr_TW"
    };

    private static readonly string[] BowSfxSourcePrefabNames =
    {
        "BowBlackmetal_TW",
        "BowTrollBone_TW"
    };

    internal static void ApplyToObjectDb(
        ObjectDB objectDb,
        ZNetScene? scene = null,
        bool logMissingSfxWarning = false)
    {
        if (objectDb == null)
        {
            return;
        }

        List<ItemDrop.ItemData.SharedData> greatbows = new();
        foreach (string prefabName in GreatbowPrefabNames)
        {
            ItemDrop.ItemData.SharedData? sharedData = FindItemSharedData(objectDb, prefabName);
            if (sharedData != null)
            {
                greatbows.Add(sharedData);
            }
        }

        if (greatbows.Count == 0)
        {
            return;
        }

        GameObject? standardBowFireSfx = FindStandardBowFireSfx(objectDb, scene);
        if (standardBowFireSfx == null)
        {
            if (logMissingSfxWarning)
            {
                WarfareTweaksWarningLog.LogOnce(
                    "warfare_greatbow_standard_bow_fire_sfx_missing",
                    $"Skipping Warfare greatbow fire sound compatibility: prefab '{StandardBowFireSfxName}' was not found.");
            }

            return;
        }

        int enabledCount = 0;
        int addedCount = 0;
        foreach (ItemDrop.ItemData.SharedData sharedData in greatbows)
        {
            switch (ApplyStandardBowFireSfx(sharedData, standardBowFireSfx))
            {
                case PatchResult.Enabled:
                    enabledCount++;
                    break;
                case PatchResult.Added:
                    addedCount++;
                    break;
            }
        }

        int patchedCount = enabledCount + addedCount;
        if (patchedCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Patched {patchedCount} Warfare greatbow fire sound assignment(s): " +
                $"enabled {enabledCount}, added {addedCount}.");
        }
    }

    private static GameObject? FindStandardBowFireSfx(ObjectDB objectDb, ZNetScene? scene)
    {
        GameObject? scenePrefab = scene?.GetPrefab(StandardBowFireSfxName);
        if (scenePrefab != null)
        {
            return scenePrefab;
        }

        GameObject? effectPrefab = FindTriggerEffectPrefab(
            objectDb,
            BowSfxSourcePrefabNames,
            StandardBowFireSfxName);
        if (effectPrefab != null)
        {
            return effectPrefab;
        }

        return FindTriggerEffectPrefab(objectDb, GreatbowPrefabNames, StandardBowFireSfxName);
    }

    private static GameObject? FindTriggerEffectPrefab(
        ObjectDB objectDb,
        string[] itemPrefabNames,
        string effectPrefabName)
    {
        foreach (string itemPrefabName in itemPrefabNames)
        {
            EffectList.EffectData[]? effects =
                FindItemSharedData(objectDb, itemPrefabName)?.m_triggerEffect?.m_effectPrefabs;
            if (effects == null)
            {
                continue;
            }

            foreach (EffectList.EffectData? effect in effects)
            {
                if (effect?.m_prefab != null && HasPrefabName(effect.m_prefab, effectPrefabName))
                {
                    return effect.m_prefab;
                }
            }
        }

        return null;
    }

    private static PatchResult ApplyStandardBowFireSfx(
        ItemDrop.ItemData.SharedData sharedData,
        GameObject standardBowFireSfx)
    {
        sharedData.m_triggerEffect ??= new EffectList();
        EffectList.EffectData[] effects =
            sharedData.m_triggerEffect.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>();

        EffectList.EffectData? disabledStandardBowFireSfx = null;
        foreach (EffectList.EffectData? effect in effects)
        {
            if (effect?.m_prefab == null)
            {
                continue;
            }

            if (HasPrefabName(effect.m_prefab, StandardBowFireSfxName))
            {
                if (effect.m_enabled)
                {
                    return PatchResult.Unchanged;
                }

                disabledStandardBowFireSfx ??= effect;
            }
        }

        if (disabledStandardBowFireSfx != null)
        {
            disabledStandardBowFireSfx.m_enabled = true;
            return PatchResult.Enabled;
        }

        EffectList.EffectData[] expandedEffects = new EffectList.EffectData[effects.Length + 1];
        Array.Copy(effects, expandedEffects, effects.Length);
        expandedEffects[^1] = new EffectList.EffectData
        {
            m_prefab = standardBowFireSfx,
            m_enabled = true,
            m_variant = -1
        };
        sharedData.m_triggerEffect.m_effectPrefabs = expandedEffects;
        return PatchResult.Added;
    }

    private static ItemDrop.ItemData.SharedData? FindItemSharedData(ObjectDB objectDb, string prefabName)
    {
        GameObject? prefab = objectDb.GetItemPrefab(prefabName);
        if (prefab == null)
        {
            return null;
        }

        ItemDrop? itemDrop = prefab.GetComponent<ItemDrop>() ?? prefab.GetComponentInChildren<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared;
    }

    private static bool HasPrefabName(GameObject prefab, string expectedName)
    {
        return string.Equals(
            WarfareTweaksCompat.NormalizePrefabName(prefab?.name),
            expectedName,
            StringComparison.OrdinalIgnoreCase);
    }
}
