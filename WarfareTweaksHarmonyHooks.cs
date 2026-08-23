using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WarfareTweaks;

[HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Awake))]
internal static class FejdStartupAwakeWarfareItemManagerSyncCompatPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        WarfareItemManagerSyncCompat.RegisterMissingRecipeConfigs();
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class ObjectDbAwakeWarfareTweaksPatch
{
    private static void Postfix(ObjectDB __instance)
    {
        WarfareTweaksPlugin.ApplyToObjectDb(__instance);
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
internal static class ObjectDbCopyOtherDbWarfareTweaksPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ObjectDB __instance)
    {
        WarfareTweaksPlugin.ApplyToObjectDb(__instance);
    }
}

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
[HarmonyAfter(WarfareTweaksCompat.WarfareFireAndIceGuid)]
internal static class ZNetSceneAwakeWarfareTweaksPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ZNetScene __instance)
    {
        WarfareTweaksPlugin.ApplyToZNetScene(__instance);
    }
}

[HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
internal static class SeManApplyStatusEffectSpeedModsWarfareEffectsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(SEMan __instance, ref float speed)
    {
        WarfareCompat.ApplyWarfareSpeedModifiers(__instance, ref speed);
    }
}

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
internal static class ItemDataGetTooltipWarfareTweaksFallbackTooltipPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        WarfareCompat.AppendMissingConfiguredEffectTooltips(item, ref __result);
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
internal static class ProjectileSetupWarfareThrowablePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Projectile __instance)
    {
        WarfareThrowableCompat.PrepareProjectileIfNeeded(__instance);
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
internal static class ProjectileOnHitWarfareTweaksPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Projectile __instance, out ProjectileHitContextScope __state)
    {
        WarfareTweaksProjectileHitContext.Scope projectileScope =
            WarfareTweaksProjectileHitContext.Begin(__instance);
        __state = new ProjectileHitContextScope(projectileScope, default);
        __state = new ProjectileHitContextScope(
            projectileScope,
            DirectWeaponHitContextSystem.BeginProjectileHit(__instance));
        WarfareThrowableCompat.PrepareProjectileIfNeeded(__instance);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ProjectileHitContextScope __state)
    {
        DirectWeaponHitContextSystem.End(__state.DirectHitScope);
        WarfareTweaksProjectileHitContext.End(__state.ProjectileScope);
    }

    private static Exception? Finalizer(Exception? __exception, ProjectileHitContextScope __state)
    {
        DirectWeaponHitContextSystem.End(__state.DirectHitScope);
        WarfareTweaksProjectileHitContext.End(__state.ProjectileScope);

        return __exception;
    }

    private readonly struct ProjectileHitContextScope
    {
        public ProjectileHitContextScope(
            WarfareTweaksProjectileHitContext.Scope projectileScope,
            DirectWeaponHitContextSystem.Scope directHitScope)
        {
            ProjectileScope = projectileScope;
            DirectHitScope = directHitScope;
        }

        public WarfareTweaksProjectileHitContext.Scope ProjectileScope { get; }

        public DirectWeaponHitContextSystem.Scope DirectHitScope { get; }
    }
}

[HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
internal static class DestructibleDamageWarfareThrowablePatch
{
    private static void Prefix(HitData hit)
    {
        WarfareThrowableCompat.ApplyProjectileToolTierIfNeeded(hit);
    }
}

[HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
internal static class MineRockDamageWarfareThrowablePatch
{
    private static void Prefix(HitData hit)
    {
        WarfareThrowableCompat.ApplyProjectileToolTierIfNeeded(hit);
    }
}

[HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
internal static class MineRock5DamageWarfareThrowablePatch
{
    private static void Prefix(HitData hit)
    {
        WarfareThrowableCompat.ApplyProjectileToolTierIfNeeded(hit);
    }
}

[HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
internal static class TreeBaseDamageWarfareThrowablePatch
{
    private static void Prefix(HitData hit)
    {
        WarfareThrowableCompat.ApplyProjectileToolTierIfNeeded(hit);
        WarfareThrowableCompat.ApplyProjectileWoodCuttingSkillIfNeeded(hit);
    }
}

[HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
internal static class TreeLogDamageWarfareThrowablePatch
{
    private static void Prefix(HitData hit)
    {
        WarfareThrowableCompat.ApplyProjectileToolTierIfNeeded(hit);
        WarfareThrowableCompat.ApplyProjectileWoodCuttingSkillIfNeeded(hit);
    }
}

[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
internal static class WearNTearDamageWarfareThrowablePatch
{
    private static void Prefix(HitData hit)
    {
        WarfareThrowableCompat.ApplyProjectileToolTierIfNeeded(hit);
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList), new[] { typeof(List<Recipe>) })]
internal static class InventoryGuiUpdateRecipeListWarfareThrowableUpgradePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(InventoryGui __instance, List<Recipe> recipes)
    {
        WarfareThrowableCompat.PrepareUpgradeRecipeList(recipes, !__instance.InCraftTab());
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidStartAttackWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Humanoid __instance)
    {
        if (__instance is Player)
        {
            WarfareThrowableCompat.PrepareWeaponForUse(__instance.GetCurrentWeapon());
        }
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
internal static class AttackOnAttackTriggerWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Attack __instance, out int __state)
    {
        __state = WarfareThrowableCompat.BeginInventoryRemovalPreservation(__instance);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(int __state)
    {
        WarfareThrowableCompat.EndInventoryRemovalPreservation(__state);
    }

    private static Exception? Finalizer(Exception? __exception, int __state)
    {
        WarfareThrowableCompat.EndInventoryRemovalPreservation(__state);

        return __exception;
    }
}

[HarmonyPatch(typeof(Attack), "ConsumeItem")]
internal static class AttackConsumeItemWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Attack __instance)
    {
        return !WarfareThrowableCompat.ShouldPreserveWeaponOnConsume(__instance);
    }
}

[HarmonyPatch(typeof(Attack), "UseAmmo")]
internal static class AttackUseAmmoWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Attack __instance, ref bool __result, ref ItemDrop.ItemData ammoItem)
    {
        if (!WarfareThrowableCompat.ShouldPreserveWeaponOnConsume(__instance))
        {
            return true;
        }

        ammoItem = null!;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(ItemDrop.ItemData) })]
internal static class InventoryRemoveItemWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemDrop.ItemData item, ref bool __result)
    {
        if (!WarfareThrowableCompat.ShouldBlockInventoryRemoval(item))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
internal static class InventoryRemoveItemAmountWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemDrop.ItemData item, ref bool __result)
    {
        if (!WarfareThrowableCompat.ShouldBlockInventoryRemoval(item))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveOneItem), new[] { typeof(ItemDrop.ItemData) })]
internal static class InventoryRemoveOneItemWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemDrop.ItemData item, ref bool __result)
    {
        if (!WarfareThrowableCompat.ShouldBlockInventoryRemoval(item))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem), new[] { typeof(ItemDrop.ItemData), typeof(bool) })]
internal static class HumanoidUnequipItemWarfareThrowableAttackPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemDrop.ItemData item)
    {
        return !WarfareThrowableCompat.ShouldBlockInventoryRemoval(item, allowBrokenUnequip: true);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.ConsumeItem), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) })]
internal static class HumanoidConsumeItemWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemDrop.ItemData item, ref bool __result)
    {
        if (!WarfareThrowableCompat.ShouldBlockInventoryRemoval(item))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
internal static class InventoryRemoveNamedItemWarfareThrowablePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(string name)
    {
        return !WarfareThrowableCompat.ShouldBlockNamedInventoryRemoval(name);
    }
}

[HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
internal static class AttackProjectileAttackTriggeredWarfareThrowableDurabilityPatch
{
    private static void Prefix(Attack __instance, out WarfareThrowableCompat.ProjectileDurabilityDrainState __state)
    {
        __state = WarfareThrowableCompat.CaptureProjectileDurabilityDrain(__instance);
    }

    private static void Postfix(WarfareThrowableCompat.ProjectileDurabilityDrainState __state)
    {
        WarfareThrowableCompat.ApplyMissingProjectileDurabilityDrain(__state);
    }
}
