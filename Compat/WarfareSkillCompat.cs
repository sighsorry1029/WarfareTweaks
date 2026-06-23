using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WarfareTweaks;

internal static class WarfareSkillCompat
{
    private const string WarfareFireProjectileBurstPatchTypeName =
        "Warfare.WeaponSkillPatch+Attack_FireProjectileBurst_Patch";
    private const string WarfareGuid = "Therzie.Warfare";

    private const string WarfareThrowingStaminaPatchTypeName =
        "Warfare.WeaponSkillPatch+Attack_FireProjectileBurst_Patch+Attack_GetAttackStamina_Patch_Throwing";

    private const string WarfareScythesStaminaPatchTypeName =
        "Warfare.WeaponSkillPatch+Attack_FireProjectileBurst_Patch+Attack_GetAttackStamina_Patch_Scythes";

    private const string WarfarePrefabSuffix = "_TW";
    private const string ThrowingSkillName = "Throwing";
    private const string ScythesSkillName = "Scythes";
    private const string ThrowableWeaponPrefix = "ThrowAxe";
    private const string ThrowableProjectileSuffix = "_projectile_TW";

    private static readonly Skills.SkillType ThrowingSkillType =
        (Skills.SkillType)Math.Abs(ThrowingSkillName.GetStableHashCode());

    private static readonly Skills.SkillType ScythesSkillType =
        (Skills.SkillType)Math.Abs(ScythesSkillName.GetStableHashCode());

    private static readonly HashSet<string> ThrowableSharedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$throw_axe_flint_TW",
        "$throw_axe_bronze_TW",
        "$throw_axe_iron_TW",
        "$throw_axe_silver_TW",
        "$throw_axe_blackmetal_TW",
        "$throw_axe_dvergr_TW",
        "$throw_axe_njord_TW",
        "$throw_axe_surtr_TW"
    };

    private static readonly Dictionary<string, Skills.SkillType> ExplicitSkillOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["DualScytheBloodthirst_TW"] = Skills.SkillType.Axes,
            ["ScytheVampiric_TW"] = Skills.SkillType.Polearms
        };

    private static readonly Dictionary<string, Type> LoadedTypesByName = new(StringComparer.Ordinal);
    private static bool _hooksInstalled;

    internal static void TryInstallHooks()
    {
        if (_hooksInstalled)
        {
            return;
        }

        Type? fireProjectileBurstPatchType = FindLoadedType(WarfareFireProjectileBurstPatchTypeName);
        Type? throwingStaminaPatchType = FindLoadedType(WarfareThrowingStaminaPatchTypeName);
        Type? scythesStaminaPatchType = FindLoadedType(WarfareScythesStaminaPatchTypeName);
        if (fireProjectileBurstPatchType == null &&
            throwingStaminaPatchType == null &&
            scythesStaminaPatchType == null)
        {
            return;
        }

        Harmony harmony = new("sighsorry.WarfareTweaks.WarfareSkillCompat");
        int patchedCount = 0;

        MethodInfo? modifyMethod = fireProjectileBurstPatchType != null
            ? AccessTools.DeclaredMethod(fireProjectileBurstPatchType, "Modify")
            : null;
        if (modifyMethod != null)
        {
            harmony.Patch(
                modifyMethod,
                prefix: new HarmonyMethod(typeof(WarfareSkillCompat), nameof(ReplaceWarfareThrowingProjectileSkillPrefix)));
            patchedCount++;
        }

        int unpatchedStaminaPostfixes = UnpatchWarfareStaminaPostfixes(harmony);
        if (unpatchedStaminaPostfixes > 0)
        {
            patchedCount += unpatchedStaminaPostfixes;
        }
        else
        {
            patchedCount += TrySuppressWarfareStaminaPostfix(harmony, throwingStaminaPatchType);
            patchedCount += TrySuppressWarfareStaminaPostfix(harmony, scythesStaminaPatchType);
        }

        if (patchedCount <= 0)
        {
            return;
        }

        _hooksInstalled = true;
        WarfareTweaksPlugin.ModLogger.LogInfo(
            $"Installed {patchedCount} Warfare skill compatibility hook(s); Throwing/Scythes gameplay bonuses are replaced with vanilla skill assignments.");
    }

    internal static void ApplyToObjectDb(ObjectDB objectDb)
    {
        if (objectDb == null)
        {
            return;
        }

        int reassignedCount = 0;
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData? sharedData = itemDrop?.m_itemData?.m_shared;
            if (sharedData == null || !TryResolveVanillaSkill(itemPrefab, sharedData, out Skills.SkillType skillType))
            {
                continue;
            }

            if (sharedData.m_skillType == skillType)
            {
                continue;
            }

            Skills.SkillType previousSkillType = sharedData.m_skillType;
            sharedData.m_skillType = skillType;
            reassignedCount++;
            if (WarfareThrowableCompat.DebugLoggingEnabled)
            {
                WarfareThrowableCompat.LogDebug(
                    $"Reassigned Warfare skill prefab={GetPrefabName(itemPrefab)} shared={sharedData.m_name} {previousSkillType}->{skillType}");
            }
        }

        if (reassignedCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Reassigned {reassignedCount} Warfare throwing/scythe weapon prefab(s) to vanilla skills.");
        }
    }

    private static int TrySuppressWarfareStaminaPostfix(Harmony harmony, Type? patchType)
    {
        MethodInfo? postfixMethod = patchType != null ? AccessTools.DeclaredMethod(patchType, "Postfix") : null;
        if (postfixMethod == null)
        {
            return 0;
        }

        harmony.Patch(
            postfixMethod,
            prefix: new HarmonyMethod(typeof(WarfareSkillCompat), nameof(SuppressWarfareSkillPostfixPrefix)));
        return 1;
    }

    private static int UnpatchWarfareStaminaPostfixes(Harmony harmony)
    {
        MethodInfo? getAttackStamina = AccessTools.DeclaredMethod(typeof(Attack), "GetAttackStamina");
        if (getAttackStamina == null)
        {
            return 0;
        }

        int postfixCount = CountWarfarePostfixes(getAttackStamina);
        if (postfixCount <= 0)
        {
            return 0;
        }

        harmony.Unpatch(getAttackStamina, HarmonyPatchType.Postfix, WarfareGuid);
        return postfixCount;
    }

    private static int CountWarfarePostfixes(MethodBase original)
    {
        Patches? patchInfo = Harmony.GetPatchInfo(original);
        if (patchInfo == null)
        {
            return 0;
        }

        int count = 0;
        foreach (Patch postfix in patchInfo.Postfixes)
        {
            if (postfix.owner == WarfareGuid)
            {
                count++;
            }
        }

        return count;
    }

    private static bool ReplaceWarfareThrowingProjectileSkillPrefix(
        Attack a,
        HitData hit,
        ref float projVelocity,
        ref float projectileAccuracy)
    {
        if (a?.m_character != Player.m_localPlayer || hit == null || !IsWarfareThrowableWeapon(a.m_weapon))
        {
            return false;
        }

        hit.m_skill = Skills.SkillType.Axes;
        float skillFactor = Player.m_localPlayer.GetSkillFactor(Skills.SkillType.Axes);
        projVelocity *= 1f + skillFactor;
        projectileAccuracy *= 1f + skillFactor;
        if (WarfareThrowableCompat.DebugLoggingEnabled)
        {
            WarfareThrowableCompat.LogDebug(
                $"Replaced Warfare throwing projectile bonus with Axes factor={skillFactor} item={DescribeWeapon(a.m_weapon)}");
        }

        return false;
    }

    private static bool SuppressWarfareSkillPostfixPrefix()
    {
        return false;
    }

    private static bool TryResolveVanillaSkill(
        GameObject itemPrefab,
        ItemDrop.ItemData.SharedData sharedData,
        out Skills.SkillType skillType)
    {
        string prefabName = GetPrefabName(itemPrefab);
        if (IsWarfareThrowableWeaponPrefabName(prefabName) || ThrowableSharedNames.Contains(sharedData.m_name))
        {
            skillType = Skills.SkillType.Axes;
            return true;
        }

        if (ExplicitSkillOverrides.TryGetValue(prefabName, out skillType))
        {
            return true;
        }

        skillType = Skills.SkillType.None;
        return false;
    }

    private static bool IsWarfareThrowableWeapon(ItemDrop.ItemData? item)
    {
        ItemDrop.ItemData.SharedData? sharedData = item?.m_shared;
        if (sharedData == null)
        {
            return false;
        }

        string prefabName = GetItemPrefabName(item);
        return IsWarfareThrowableWeaponPrefabName(prefabName) ||
               ThrowableSharedNames.Contains(sharedData.m_name) ||
               UsesWarfareThrowableProjectile(sharedData.m_attack) ||
               UsesWarfareThrowableProjectile(sharedData.m_secondaryAttack);
    }

    private static bool UsesWarfareThrowableProjectile(Attack? attack)
    {
        return attack?.m_attackProjectile != null &&
               IsWarfareThrowableProjectilePrefabName(GetPrefabName(attack.m_attackProjectile));
    }

    private static bool IsWarfareThrowableWeaponPrefabName(string prefabName)
    {
        return prefabName.StartsWith(ThrowableWeaponPrefix, StringComparison.OrdinalIgnoreCase) &&
               prefabName.EndsWith(WarfarePrefabSuffix, StringComparison.OrdinalIgnoreCase) &&
               !prefabName.EndsWith(ThrowableProjectileSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWarfareThrowableProjectilePrefabName(string prefabName)
    {
        return prefabName.StartsWith(ThrowableWeaponPrefix, StringComparison.OrdinalIgnoreCase) &&
               prefabName.EndsWith(ThrowableProjectileSuffix, StringComparison.OrdinalIgnoreCase);
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

    private static string GetItemPrefabName(ItemDrop.ItemData? item)
    {
        return item?.m_dropPrefab != null ? GetPrefabName(item.m_dropPrefab) : string.Empty;
    }

    private static string GetPrefabName(GameObject prefab)
    {
        const string cloneSuffix = "(Clone)";
        string prefabName = prefab.name;
        return prefabName.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? prefabName.Substring(0, prefabName.Length - cloneSuffix.Length)
            : prefabName;
    }

    private static string DescribeWeapon(ItemDrop.ItemData? item)
    {
        ItemDrop.ItemData.SharedData? sharedData = item?.m_shared;
        return sharedData == null
            ? "<null>"
            : $"{GetItemPrefabName(item)} shared={sharedData.m_name} skill={sharedData.m_skillType}";
    }

    internal static bool IsHiddenWarfareSkill(Skills.SkillType skillType)
    {
        return skillType == ThrowingSkillType || skillType == ScythesSkillType;
    }
}

[HarmonyPatch(typeof(Skills), nameof(Skills.GetSkillList))]
internal static class SkillsGetSkillListWarfareSkillCompatPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(List<Skills.Skill> __result)
    {
        __result?.RemoveAll(static skill => skill?.m_info != null &&
                                            WarfareSkillCompat.IsHiddenWarfareSkill(skill.m_info.m_skill));
    }
}

[HarmonyPatch(typeof(Skills), nameof(Skills.GetTotalSkill))]
internal static class SkillsGetTotalSkillWarfareSkillCompatPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Dictionary<Skills.SkillType, Skills.Skill> ___m_skillData, ref float __result)
    {
        if (___m_skillData == null || __result <= 0f)
        {
            return;
        }

        foreach (KeyValuePair<Skills.SkillType, Skills.Skill> skillData in ___m_skillData)
        {
            if (WarfareSkillCompat.IsHiddenWarfareSkill(skillData.Key))
            {
                __result -= skillData.Value.m_level;
            }
        }

        __result = Mathf.Max(0f, __result);
    }
}
