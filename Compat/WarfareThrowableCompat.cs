using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace WarfareTweaks;

internal static class WarfareThrowableCompat
{
    private const string WarfarePrefabSuffix = "_TW";
    private const string ThrowingSkillName = "Throwing";
    private const string ThrowableProjectilePrefix = "ThrowAxe";
    private const string ThrowableWeaponPrefix = "ThrowAxe";
    private const string ThrowableSharedNameToken = "throw_axe";
    private const string ThrowableProjectileSuffix = "_projectile_TW";
    private const float DefaultMaxDurability = 100f;
    private const float DefaultUseDurabilityDrain = 1f;
    private const string DurabilityInitializedKey = "WarfareTweaks_WarfareThrowableDurabilityInitialized";
    private const float BrokenRemovalPreservationSeconds = 5f;

    private static readonly Skills.SkillType ThrowingSkillType =
        (Skills.SkillType)Math.Abs(ThrowingSkillName.GetStableHashCode());
    private static readonly HashSet<string> PatchedWeaponPrefabNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PatchedWeaponSharedNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PatchedProjectilePrefabNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<ItemDrop.ItemData, BrokenRemovalPreservationState> BrokenRemovalPreservations = new();
    private static readonly Dictionary<string, RecipeLookupEntry> RecipesByPrefabOrSharedName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, GameObject> DropPrefabsBySharedName = new(StringComparer.OrdinalIgnoreCase);
    private static ObjectDB? _cachedRecipeObjectDb;
    private static ObjectDB? _cachedDropPrefabObjectDb;
    private static int _cachedRecipeCount = -1;
    private static int _cachedDropPrefabItemCount = -1;
    private static readonly Dictionary<string, string[]> UpgradeTemplatePrefabsByThrowable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ThrowAxeFlint_TW"] = new[] { "AxeFlint" },
            ["ThrowAxeBronze_TW"] = new[] { "AxeBronze" },
            ["ThrowAxeIron_TW"] = new[] { "AxeIron" },
            ["ThrowAxeSilver_TW"] = new[] { "AxeSilver_TW" },
            ["ThrowAxeBlackmetal_TW"] = new[] { "AxeBlackMetal" },
            ["ThrowAxeDvergr_TW"] = new[] { "AxeDvergr_TW", "AxeJotunBane" },
            ["ThrowAxeNjord_TW"] = new[] { "AxeNjord_TW", "AxeDvergr_TW", "AxeJotunBane" },
            ["ThrowAxeSurtr_TW"] = new[] { "AxeSurtr_TW", "AxeFlametal_TW", "AxeBerzerkr" }
        };

    [ThreadStatic]
    private static int InventoryRemovalPreservationDepth;

    internal static bool DebugLoggingEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => false;
    }

    internal static void LogDebug(string message)
    {
    }

    // ObjectDB/ZNetScene patching keeps prefab and recipe normalization together.
    internal static void ApplyToObjectDb(ObjectDB objectDb)
    {
        if (objectDb == null)
        {
            return;
        }

        HashSet<string> patchedSharedNames = new(StringComparer.OrdinalIgnoreCase);
        int weaponCount = 0;
        int projectileCount = 0;
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData? sharedData = itemDrop?.m_itemData?.m_shared;
            bool looksLikeThrowable = LooksLikeWarfareThrowablePrefab(itemPrefab, sharedData);
            if (looksLikeThrowable && DebugLoggingEnabled)
            {
                LogDebug(
                    $"ObjectDB candidate prefab={GetPrefabName(itemPrefab)} recognized={IsWarfareThrowableWeaponPrefab(itemPrefab, sharedData)} item={DescribeItem(itemDrop?.m_itemData)} primary={DescribeAttack(sharedData?.m_attack)} secondary={DescribeAttack(sharedData?.m_secondaryAttack)}");
            }

            if (!IsWarfareThrowableWeaponPrefab(itemPrefab, sharedData))
            {
                continue;
            }

            if (DebugLoggingEnabled)
            {
                LogDebug($"ObjectDB patch before prefab={GetPrefabName(itemPrefab)} item={DescribeItem(itemDrop?.m_itemData)}");
            }

            PatchThrowableWeapon(itemDrop!, itemPrefab, out int weaponProjectileCount);
            if (DebugLoggingEnabled)
            {
                LogDebug($"ObjectDB patch after prefab={GetPrefabName(itemPrefab)} item={DescribeItem(itemDrop?.m_itemData)} primary={DescribeAttack(sharedData?.m_attack)} secondary={DescribeAttack(sharedData?.m_secondaryAttack)}");
            }

            weaponCount++;
            projectileCount += weaponProjectileCount;
            patchedSharedNames.Add(sharedData!.m_name);
            PatchedWeaponPrefabNames.Add(GetPrefabName(itemPrefab));
            PatchedWeaponSharedNames.Add(sharedData.m_name);
        }

        int recipeCount = PatchRecipes(objectDb, patchedSharedNames, out int upgradeRecipeCount);
        if (weaponCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Patched {weaponCount} Warfare throwing weapon prefab(s) as durability weapons, updated {recipeCount} recipe(s), enabled upgrade scaling on {upgradeRecipeCount} recipe(s), and disabled pickup respawn on {projectileCount} projectile prefab reference(s).");
        }
    }

    internal static void ApplyToZNetScene(ZNetScene scene)
    {
        if (scene == null)
        {
            return;
        }

        int projectileCount = 0;
        foreach (GameObject prefab in scene.m_prefabs)
        {
            if (prefab == null || !ShouldPatchProjectilePrefabName(GetPrefabName(prefab)))
            {
                continue;
            }

            if (PatchProjectilePrefab(prefab))
            {
                projectileCount++;
            }
        }

        if (projectileCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Patched {projectileCount} Warfare throwing projectile prefab(s) to vanish on impact.");
        }
    }

    // Projectile hit/runtime entry points are called from Harmony hot paths.
    internal static void PrepareProjectileIfNeeded(Projectile projectile)
    {
        if (projectile == null || !IsWarfareThrowableProjectile(projectile))
        {
            return;
        }

        if (DebugLoggingEnabled)
        {
            LogDebug($"PrepareProjectileIfNeeded projectile={DescribeProjectile(projectile)}");
        }

        ConfigureProjectileToVanish(projectile);
    }

    internal static void ApplyProjectileToolTierIfNeeded(HitData? hit, string source)
    {
        if (hit == null ||
            !WarfareTweaksRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext context) ||
            context.Projectile == null ||
            !IsWarfareThrowableProjectile(context.Projectile))
        {
            return;
        }

        short toolTier = ResolveProjectileToolTier(context.Projectile);
        if (toolTier <= hit.m_toolTier)
        {
            return;
        }

        short previousToolTier = hit.m_toolTier;
        hit.m_toolTier = toolTier;
        if (DebugLoggingEnabled)
        {
            LogDebug(
                $"{source} restored projectile toolTier {previousToolTier}->{hit.m_toolTier} projectile={DescribeProjectile(context.Projectile)}");
        }
    }

    internal static void ApplyProjectileWoodCuttingSkillIfNeeded(HitData? hit, string source)
    {
        if (hit == null ||
            hit.m_damage.m_chop <= 0f ||
            !WarfareTweaksRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext context) ||
            context.Projectile == null ||
            !IsWarfareThrowableProjectile(context.Projectile))
        {
            return;
        }

        Character? attacker = hit.GetAttacker();
        if (attacker != Player.m_localPlayer)
        {
            return;
        }

        Skills.SkillType previousSkill = hit.m_skill;
        hit.m_skill = Skills.SkillType.WoodCutting;
        hit.m_skillLevel = attacker.GetSkillLevel(Skills.SkillType.WoodCutting);
        attacker.RaiseSkill(Skills.SkillType.WoodCutting, hit.m_skillRaiseAmount);
        if (DebugLoggingEnabled)
        {
            LogDebug(
                $"{source} routed throwable chop hit skill {previousSkill}->{hit.m_skill} and raised WoodCutting by {hit.m_skillRaiseAmount} projectile={DescribeProjectile(context.Projectile)}");
        }
    }

    internal static void PrepareWeaponForUse(ItemDrop.ItemData? weapon)
    {
        if (!IsWarfareThrowableWeapon(weapon))
        {
            if (LooksLikeWarfareThrowableItem(weapon))
            {
                if (DebugLoggingEnabled)
                {
                    LogDebug($"PrepareWeaponForUse skipped unrecognized item={DescribeItem(weapon)}");
                }
            }

            return;
        }

        if (DebugLoggingEnabled)
        {
            LogDebug($"PrepareWeaponForUse before item={DescribeItem(weapon)}");
        }

        ConfigureWeaponDurability(weapon!);
        if (DebugLoggingEnabled)
        {
            LogDebug($"PrepareWeaponForUse after item={DescribeItem(weapon)}");
        }
    }

    internal static bool TryPrepareJewelcraftingSocketableWeapon(ItemDrop.ItemData? weapon)
    {
        if (!IsWarfareThrowableWeapon(weapon) && !LooksLikeWarfareThrowableItem(weapon))
        {
            return false;
        }

        EnsureDropPrefab(weapon!);
        ConfigureWeaponDurability(weapon!);
        weapon!.m_shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
        weapon.m_shared.m_skillType = Skills.SkillType.Axes;
        return true;
    }

    // Crafting UI repair fills missing upgrade metadata on copied throwable items.
    internal static void PrepareUpgradeRecipeList(List<Recipe>? recipes, bool includeMissingInventoryRecipes)
    {
        ObjectDB? objectDb = ObjectDB.instance;
        if (objectDb == null)
        {
            return;
        }

        int preparedRecipeCount = 0;
        if (recipes != null)
        {
            foreach (Recipe recipe in recipes)
            {
                if (TryPrepareThrowableUpgradeRecipe(objectDb, recipe))
                {
                    preparedRecipeCount++;
                }
            }
        }

        int preparedInventoryCount = 0;
        Inventory? inventory = Player.m_localPlayer?.GetInventory();
        if (inventory != null)
        {
            foreach (ItemDrop.ItemData item in inventory.m_inventory)
            {
                if (TryPrepareThrowableUpgradeItem(objectDb, item))
                {
                    preparedInventoryCount++;
                }
            }

            if (includeMissingInventoryRecipes && recipes != null)
            {
                preparedRecipeCount += AddMissingInventoryThrowableRecipes(objectDb, inventory, recipes);
            }
        }

        if (preparedRecipeCount > 0 || preparedInventoryCount > 0)
        {
            if (DebugLoggingEnabled)
            {
                LogDebug(
                    $"Prepared Warfare throwable upgrade data for recipes={preparedRecipeCount} inventoryItems={preparedInventoryCount}");
            }
        }
    }

    private static int AddMissingInventoryThrowableRecipes(
        ObjectDB objectDb,
        Inventory inventory,
        List<Recipe> recipes)
    {
        Player? player = Player.m_localPlayer;
        HashSet<string> listedSharedNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (Recipe recipe in recipes)
        {
            string? sharedName = recipe?.m_item?.m_itemData?.m_shared?.m_name;
            if (!string.IsNullOrWhiteSpace(sharedName))
            {
                listedSharedNames.Add(sharedName!);
            }
        }

        int addedCount = 0;
        foreach (ItemDrop.ItemData item in inventory.m_inventory)
        {
            ItemDrop.ItemData.SharedData? sharedData = item?.m_shared;
            if (sharedData == null ||
                listedSharedNames.Contains(sharedData.m_name) ||
                (!IsWarfareThrowableWeapon(item) && !LooksLikeWarfareThrowableItem(item)))
            {
                continue;
            }

            EnsureDropPrefab(item!);
            string prefabName = GetItemPrefabName(item);
            Recipe? recipe = FindRecipeForItem(objectDb, prefabName, sharedData.m_name);
            if (recipe == null ||
                player != null &&
                !player.m_noPlacementCost &&
                !player.RequiredCraftingStation(recipe, 1, checkLevel: false))
            {
                continue;
            }

            if (!TryPrepareThrowableUpgradeRecipe(objectDb, recipe))
            {
                continue;
            }

            recipes.Add(recipe);
            listedSharedNames.Add(sharedData.m_name);
            addedCount++;
            if (DebugLoggingEnabled)
            {
                LogDebug(
                    $"Added missing Warfare throwable upgrade recipe prefab={prefabName} shared={sharedData.m_name}");
            }
        }

        return addedCount;
    }

    // Attack and inventory guards preserve throwable durability semantics.
    internal static void PrepareAttackForUse(Attack? attack)
    {
        if (attack?.m_weapon == null || !IsWarfareThrowableWeapon(attack.m_weapon))
        {
            if (LooksLikeWarfareThrowableItem(attack?.m_weapon))
            {
                if (DebugLoggingEnabled)
                {
                    LogDebug($"PrepareAttackForUse skipped unrecognized attack={DescribeAttack(attack)} item={DescribeItem(attack?.m_weapon)}");
                }
            }

            return;
        }

        if (DebugLoggingEnabled)
        {
            LogDebug($"PrepareAttackForUse before attack={DescribeAttack(attack)} item={DescribeItem(attack.m_weapon)} ammo={DescribeItem(attack.m_ammoItem)} lastAmmo={DescribeItem(attack.m_lastUsedAmmo)}");
        }

        ConfigureWeaponDurability(attack.m_weapon);
        attack.m_consumeItem = false;
        attack.m_ammoItem = null;
        attack.m_lastUsedAmmo = null;
        if (IsWarfareThrowableItemPrefab(attack.m_spawnOnHit))
        {
            attack.m_spawnOnHit = null;
            attack.m_spawnOnHitChance = 0f;
        }

        if (attack.m_attackProjectile != null)
        {
            PatchProjectilePrefab(attack.m_attackProjectile);
        }

        if (DebugLoggingEnabled)
        {
            LogDebug($"PrepareAttackForUse after attack={DescribeAttack(attack)} item={DescribeItem(attack.m_weapon)}");
        }
    }

    internal static bool ShouldPreserveWeaponOnConsume(Attack? attack)
    {
        if (attack?.m_weapon == null || !IsWarfareThrowableWeapon(attack.m_weapon))
        {
            if (LooksLikeWarfareThrowableItem(attack?.m_weapon))
            {
                if (DebugLoggingEnabled)
                {
                    LogDebug($"Attack.ConsumeItem not blocked: item was not recognized. attack={DescribeAttack(attack)} item={DescribeItem(attack?.m_weapon)}");
                }
            }

            return false;
        }

        PrepareAttackForUse(attack);
        if (DebugLoggingEnabled)
        {
            LogDebug($"Attack.ConsumeItem blocked for item={DescribeItem(attack.m_weapon)}");
        }

        return true;
    }

    internal static bool ShouldSkipAmmoConsumption(Attack? attack)
    {
        if (attack?.m_weapon == null || !IsWarfareThrowableWeapon(attack.m_weapon))
        {
            if (LooksLikeWarfareThrowableItem(attack?.m_weapon))
            {
                if (DebugLoggingEnabled)
                {
                    LogDebug($"Attack.UseAmmo not skipped: item was not recognized. attack={DescribeAttack(attack)} item={DescribeItem(attack?.m_weapon)} ammo={DescribeItem(attack?.m_ammoItem)}");
                }
            }

            return false;
        }

        PrepareAttackForUse(attack);
        if (DebugLoggingEnabled)
        {
            LogDebug($"Attack.UseAmmo skipped for item={DescribeItem(attack.m_weapon)}");
        }

        return true;
    }

    internal static bool BeginInventoryRemovalPreservation(Attack? attack)
    {
        if (attack?.m_weapon == null || !IsWarfareThrowableWeapon(attack.m_weapon))
        {
            if (LooksLikeWarfareThrowableItem(attack?.m_weapon))
            {
                if (DebugLoggingEnabled)
                {
                    LogDebug($"Inventory preservation not started: item was not recognized. attack={DescribeAttack(attack)} item={DescribeItem(attack?.m_weapon)}");
                }
            }

            return false;
        }

        PrepareAttackForUse(attack);
        InventoryRemovalPreservationDepth++;
        if (DebugLoggingEnabled)
        {
            LogDebug($"Inventory preservation started depth={InventoryRemovalPreservationDepth} attack={DescribeAttack(attack)} item={DescribeItem(attack.m_weapon)}");
        }

        return true;
    }

    internal static void EndInventoryRemovalPreservation(bool active)
    {
        if (!active || InventoryRemovalPreservationDepth <= 0)
        {
            return;
        }

        InventoryRemovalPreservationDepth--;
        if (DebugLoggingEnabled)
        {
            LogDebug($"Inventory preservation ended depth={InventoryRemovalPreservationDepth}");
        }
    }

    internal static bool ShouldBlockInventoryRemoval(ItemDrop.ItemData? item, string source, int amount = -1)
    {
        bool recognized = IsWarfareThrowableWeapon(item);
        bool looksLikeThrowable = LooksLikeWarfareThrowableItem(item);
        if ((recognized || looksLikeThrowable) && DebugLoggingEnabled)
        {
            LogDebug(
                $"{source} observed amount={amount} preserveDepth={InventoryRemovalPreservationDepth} recognized={recognized} item={DescribeItem(item)}");
        }

        bool preserveBrokenRemoval = recognized && ShouldPreserveBrokenRemoval(item, source);
        if (preserveBrokenRemoval && source.StartsWith("Humanoid.UnequipItem", StringComparison.Ordinal))
        {
            if (DebugLoggingEnabled)
            {
                LogDebug($"{source} allowed broken item unequip for item={DescribeItem(item)}");
            }

            return false;
        }

        if ((InventoryRemovalPreservationDepth <= 0 && !preserveBrokenRemoval) || !recognized)
        {
            return false;
        }

        ConfigureWeaponDurability(item!);
        if (DebugLoggingEnabled)
        {
            LogDebug($"{source} blocked item removal for item={DescribeItem(item)}");
        }

        return true;
    }

    internal static bool ShouldBlockNamedInventoryRemoval(string source, string name, int amount)
    {
        bool looksLikeThrowable = LooksLikeWarfareThrowableName(name);
        if (looksLikeThrowable && DebugLoggingEnabled)
        {
            LogDebug($"{source} observed amount={amount} preserveDepth={InventoryRemovalPreservationDepth} name={name}");
        }

        if (InventoryRemovalPreservationDepth <= 0 || !looksLikeThrowable)
        {
            return false;
        }

        if (DebugLoggingEnabled)
        {
            LogDebug($"{source} blocked named item removal name={name} amount={amount}");
        }

        return true;
    }

    internal static ProjectileDurabilityDrainState CaptureProjectileDurabilityDrain(Attack attack)
    {
        if (attack?.m_character is not Player || !IsWarfareThrowableWeapon(attack.m_weapon))
        {
            if (LooksLikeWarfareThrowableItem(attack?.m_weapon))
            {
                if (DebugLoggingEnabled)
                {
                    LogDebug($"Projectile durability capture skipped. attack={DescribeAttack(attack)} item={DescribeItem(attack?.m_weapon)}");
                }
            }

            return ProjectileDurabilityDrainState.Empty;
        }

        PrepareAttackForUse(attack);
        ItemDrop.ItemData weapon = attack.m_weapon;
        if (DebugLoggingEnabled)
        {
            LogDebug($"Projectile durability capture before={weapon.m_durability} drain={GetDurabilityDrain(weapon)} item={DescribeItem(weapon)}");
        }

        return new ProjectileDurabilityDrainState(weapon, weapon.m_durability);
    }

    internal static void ApplyMissingProjectileDurabilityDrain(ProjectileDurabilityDrainState state)
    {
        if (!state.Applies || state.Weapon == null)
        {
            return;
        }

        ItemDrop.ItemData weapon = state.Weapon;
        if (weapon.m_durability < state.BeforeDurability - 0.001f)
        {
            if (DebugLoggingEnabled)
            {
                LogDebug($"Projectile durability already drained before={state.BeforeDurability} after={weapon.m_durability} item={DescribeItem(weapon)}");
            }

            MarkBrokenRemovalPreservationIfNeeded(weapon);
            return;
        }

        weapon.m_durability = Mathf.Max(0f, weapon.m_durability - GetDurabilityDrain(weapon));
        MarkBrokenRemovalPreservationIfNeeded(weapon);
        if (DebugLoggingEnabled)
        {
            LogDebug($"Projectile durability manually drained before={state.BeforeDurability} after={weapon.m_durability} item={DescribeItem(weapon)}");
        }
    }

    // Prefab mutation helpers are shared by ObjectDB setup and runtime attack repair.
    private static void PatchThrowableWeapon(ItemDrop itemDrop, GameObject itemPrefab, out int projectileCount)
    {
        projectileCount = 0;
        ItemDrop.ItemData itemData = itemDrop.m_itemData;
        ConfigureWeaponDurability(itemData);
        ItemDrop.ItemData.SharedData sharedData = itemData.m_shared;
        sharedData.m_maxStackSize = 1;
        sharedData.m_autoStack = false;

        projectileCount += PatchAttack(sharedData.m_attack);
        projectileCount += PatchAttack(sharedData.m_secondaryAttack);
        PatchedWeaponPrefabNames.Add(GetPrefabName(itemPrefab));
    }

    private static int PatchAttack(Attack? attack)
    {
        if (attack == null)
        {
            return 0;
        }

        if (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackProjectile != null)
        {
            attack.m_consumeItem = false;
        }

        if (IsWarfareThrowableItemPrefab(attack.m_spawnOnHit))
        {
            attack.m_spawnOnHit = null;
            attack.m_spawnOnHitChance = 0f;
        }

        return attack.m_attackProjectile != null && PatchProjectilePrefab(attack.m_attackProjectile)
            ? 1
            : 0;
    }

    private static bool PatchProjectilePrefab(GameObject projectilePrefab)
    {
        Projectile? projectile = projectilePrefab.GetComponent<Projectile>() ??
                                 projectilePrefab.GetComponentInChildren<Projectile>();
        if (projectile == null)
        {
            return false;
        }

        PatchedProjectilePrefabNames.Add(GetPrefabName(projectilePrefab));
        PatchedProjectilePrefabNames.Add(GetPrefabName(projectile.gameObject));
        ConfigureProjectileToVanish(projectile);
        return true;
    }

    private static void ConfigureProjectileToVanish(Projectile projectile)
    {
        projectile.m_respawnItemOnHit = false;
        projectile.m_spawnItem = null;
        if (IsWarfareThrowableItemPrefab(projectile.m_spawnOnHit))
        {
            projectile.m_spawnOnHit = null;
            projectile.m_spawnOnHitChance = 0f;
        }

        projectile.m_randomSpawnOnHit.RemoveAll(IsWarfareThrowableItemPrefab);
        projectile.m_stayAfterHitStatic = false;
        projectile.m_stayAfterHitDynamic = false;
        projectile.m_attachToRigidBody = false;
        projectile.m_attachToClosestBone = false;
        projectile.m_bounce = false;
        projectile.m_bounceOnWater = false;
    }

    private static int PatchRecipes(
        ObjectDB objectDb,
        HashSet<string> patchedSharedNames,
        out int upgradeRecipeCount)
    {
        upgradeRecipeCount = 0;
        if (patchedSharedNames.Count == 0)
        {
            return 0;
        }

        int recipeCount = 0;
        foreach (Recipe recipe in objectDb.m_recipes)
        {
            ItemDrop.ItemData.SharedData? sharedData = recipe?.m_item?.m_itemData?.m_shared;
            string itemPrefabName = recipe?.m_item != null ? GetPrefabName(recipe.m_item.gameObject) : string.Empty;
            if (sharedData == null || !patchedSharedNames.Contains(sharedData.m_name))
            {
                continue;
            }

            recipe!.m_amount = 1;
            if (TryApplyUpgradeTemplate(objectDb, recipe, itemPrefabName, sharedData))
            {
                upgradeRecipeCount++;
            }

            recipeCount++;
        }

        return recipeCount;
    }

    private static bool TryApplyUpgradeTemplate(
        ObjectDB objectDb,
        Recipe throwableRecipe,
        string throwablePrefabName,
        ItemDrop.ItemData.SharedData throwableShared)
    {
        if (!TryFindUpgradeTemplate(
                objectDb,
                throwablePrefabName,
                out ItemDrop.ItemData.SharedData? templateShared,
                out Recipe? templateRecipe) ||
            templateShared == null)
        {
            return false;
        }

        ApplyUpgradeTemplateToShared(throwableShared, templateShared);

        bool recipeHasUpgradeCosts = templateRecipe != null &&
                                     ApplyUpgradeRequirementsFromTemplate(throwableRecipe, templateRecipe);
        if (!recipeHasUpgradeCosts)
        {
            recipeHasUpgradeCosts = EnsureFallbackUpgradeRequirements(throwableRecipe);
        }

        if (DebugLoggingEnabled)
        {
            LogDebug(
                $"Applied upgrade template to throwable prefab={throwablePrefabName} maxQuality={throwableShared.m_maxQuality} damagesPerLevel={DescribeDamage(throwableShared.m_damagesPerLevel)} durabilityPerLevel={throwableShared.m_durabilityPerLevel}");
        }

        return throwableShared.m_maxQuality > 1 && recipeHasUpgradeCosts;
    }

    private static bool TryPrepareThrowableUpgradeRecipe(ObjectDB objectDb, Recipe? recipe)
    {
        ItemDrop? item = recipe?.m_item;
        ItemDrop.ItemData.SharedData? sharedData = item?.m_itemData?.m_shared;
        if (item == null || sharedData == null)
        {
            return false;
        }

        string prefabName = GetPrefabName(item.gameObject);
        if (!LooksLikeWarfareThrowableName(prefabName) &&
            !LooksLikeWarfareThrowableName(sharedData.m_name) &&
            !PatchedWeaponPrefabNames.Contains(prefabName) &&
            !PatchedWeaponSharedNames.Contains(sharedData.m_name))
        {
            return false;
        }

        return TryApplyUpgradeTemplate(objectDb, recipe!, prefabName, sharedData);
    }

    private static bool TryPrepareThrowableUpgradeItem(ObjectDB objectDb, ItemDrop.ItemData? item)
    {
        if (!IsWarfareThrowableWeapon(item) && !LooksLikeWarfareThrowableItem(item))
        {
            return false;
        }

        EnsureDropPrefab(item!);
        ConfigureWeaponDurability(item!);
        string prefabName = GetItemPrefabName(item);
        return !string.IsNullOrWhiteSpace(prefabName) &&
               TryApplyUpgradeTemplateToShared(objectDb, prefabName, item!.m_shared);
    }

    private static bool TryApplyUpgradeTemplateToShared(
        ObjectDB objectDb,
        string throwablePrefabName,
        ItemDrop.ItemData.SharedData throwableShared)
    {
        if (!TryFindUpgradeTemplate(
                objectDb,
                throwablePrefabName,
                out ItemDrop.ItemData.SharedData? templateShared,
                out _) ||
            templateShared == null)
        {
            return false;
        }

        ApplyUpgradeTemplateToShared(throwableShared, templateShared);
        return throwableShared.m_maxQuality > 1;
    }

    // Upgrade template application keeps damage scaling and recipe repair co-located.
    private static void ApplyUpgradeTemplateToShared(
        ItemDrop.ItemData.SharedData throwableShared,
        ItemDrop.ItemData.SharedData templateShared)
    {
        throwableShared.m_maxQuality = Mathf.Max(throwableShared.m_maxQuality, templateShared.m_maxQuality);
        throwableShared.m_damagesPerLevel = ScaleDamagePerLevelByTemplatePercent(
            throwableShared.m_damages,
            templateShared.m_damages,
            templateShared.m_damagesPerLevel);
        throwableShared.m_durabilityPerLevel = templateShared.m_durabilityPerLevel;
        throwableShared.m_blockPowerPerLevel = templateShared.m_blockPowerPerLevel;
        throwableShared.m_deflectionForcePerLevel = templateShared.m_deflectionForcePerLevel;
    }

    private static HitData.DamageTypes ScaleDamagePerLevelByTemplatePercent(
        HitData.DamageTypes throwableBase,
        HitData.DamageTypes templateBase,
        HitData.DamageTypes templatePerLevel)
    {
        float fallbackRatio = CalculateTotalDamageRatio(templateBase, templatePerLevel);
        return new HitData.DamageTypes
        {
            m_damage = ScaleDamageComponent(
                throwableBase.m_damage,
                templateBase.m_damage,
                templatePerLevel.m_damage,
                fallbackRatio),
            m_blunt = ScaleDamageComponent(
                throwableBase.m_blunt,
                templateBase.m_blunt,
                templatePerLevel.m_blunt,
                fallbackRatio),
            m_slash = ScaleDamageComponent(
                throwableBase.m_slash,
                templateBase.m_slash,
                templatePerLevel.m_slash,
                fallbackRatio),
            m_pierce = ScaleDamageComponent(
                throwableBase.m_pierce,
                templateBase.m_pierce,
                templatePerLevel.m_pierce,
                fallbackRatio),
            m_chop = ScaleDamageComponent(
                throwableBase.m_chop,
                templateBase.m_chop,
                templatePerLevel.m_chop,
                fallbackRatio),
            m_pickaxe = ScaleDamageComponent(
                throwableBase.m_pickaxe,
                templateBase.m_pickaxe,
                templatePerLevel.m_pickaxe,
                fallbackRatio),
            m_fire = ScaleDamageComponent(
                throwableBase.m_fire,
                templateBase.m_fire,
                templatePerLevel.m_fire,
                fallbackRatio),
            m_frost = ScaleDamageComponent(
                throwableBase.m_frost,
                templateBase.m_frost,
                templatePerLevel.m_frost,
                fallbackRatio),
            m_lightning = ScaleDamageComponent(
                throwableBase.m_lightning,
                templateBase.m_lightning,
                templatePerLevel.m_lightning,
                fallbackRatio),
            m_poison = ScaleDamageComponent(
                throwableBase.m_poison,
                templateBase.m_poison,
                templatePerLevel.m_poison,
                fallbackRatio),
            m_spirit = ScaleDamageComponent(
                throwableBase.m_spirit,
                templateBase.m_spirit,
                templatePerLevel.m_spirit,
                fallbackRatio)
        };
    }

    private static float ScaleDamageComponent(
        float throwableBase,
        float templateBase,
        float templatePerLevel,
        float fallbackRatio)
    {
        if (throwableBase <= 0f)
        {
            return 0f;
        }

        float ratio = templateBase > 0.001f ? templatePerLevel / templateBase : fallbackRatio;
        return ratio > 0f ? throwableBase * ratio : 0f;
    }

    private static float CalculateTotalDamageRatio(
        HitData.DamageTypes templateBase,
        HitData.DamageTypes templatePerLevel)
    {
        float baseTotal = SumPositiveDamage(templateBase);
        if (baseTotal <= 0.001f)
        {
            return 0f;
        }

        return SumPositiveDamage(templatePerLevel) / baseTotal;
    }

    private static float SumPositiveDamage(HitData.DamageTypes damage)
    {
        return Mathf.Max(0f, damage.m_damage)
               + Mathf.Max(0f, damage.m_blunt)
               + Mathf.Max(0f, damage.m_slash)
               + Mathf.Max(0f, damage.m_pierce)
               + Mathf.Max(0f, damage.m_chop)
               + Mathf.Max(0f, damage.m_pickaxe)
               + Mathf.Max(0f, damage.m_fire)
               + Mathf.Max(0f, damage.m_frost)
               + Mathf.Max(0f, damage.m_lightning)
               + Mathf.Max(0f, damage.m_poison)
               + Mathf.Max(0f, damage.m_spirit);
    }

    private static bool TryFindUpgradeTemplate(
        ObjectDB objectDb,
        string throwablePrefabName,
        out ItemDrop.ItemData.SharedData? templateShared,
        out Recipe? templateRecipe)
    {
        templateShared = null;
        templateRecipe = null;
        if (!UpgradeTemplatePrefabsByThrowable.TryGetValue(throwablePrefabName, out string[] templatePrefabNames))
        {
            return false;
        }

        foreach (string templatePrefabName in templatePrefabNames)
        {
            GameObject? templatePrefab = objectDb.GetItemPrefab(templatePrefabName);
            ItemDrop? templateItemDrop = templatePrefab != null ? templatePrefab.GetComponent<ItemDrop>() : null;
            ItemDrop.ItemData.SharedData? sharedData = templateItemDrop?.m_itemData?.m_shared;
            if (sharedData == null)
            {
                continue;
            }

            templateShared = sharedData;
            templateRecipe = FindRecipeForItem(objectDb, templatePrefabName, sharedData.m_name);
            return true;
        }

        return false;
    }

    private static Recipe? FindRecipeForItem(ObjectDB objectDb, string prefabName, string sharedName)
    {
        EnsureRecipeLookup(objectDb);
        RecipeLookupEntry? bestEntry = null;
        if (!string.IsNullOrWhiteSpace(prefabName) &&
            RecipesByPrefabOrSharedName.TryGetValue(prefabName, out RecipeLookupEntry? prefabEntry))
        {
            bestEntry = prefabEntry;
        }

        if (!string.IsNullOrWhiteSpace(sharedName) &&
            RecipesByPrefabOrSharedName.TryGetValue(sharedName, out RecipeLookupEntry? sharedNameEntry) &&
            (bestEntry == null || sharedNameEntry.Index < bestEntry.Index))
        {
            bestEntry = sharedNameEntry;
        }

        return bestEntry?.Recipe;
    }

    private static void EnsureRecipeLookup(ObjectDB objectDb)
    {
        int recipeCount = objectDb.m_recipes?.Count ?? 0;
        if (ReferenceEquals(_cachedRecipeObjectDb, objectDb) &&
            _cachedRecipeCount == recipeCount)
        {
            return;
        }

        RecipesByPrefabOrSharedName.Clear();
        _cachedRecipeObjectDb = objectDb;
        _cachedRecipeCount = recipeCount;
        if (objectDb.m_recipes == null)
        {
            return;
        }

        for (int index = 0; index < objectDb.m_recipes.Count; index++)
        {
            Recipe recipe = objectDb.m_recipes[index];
            ItemDrop? item = recipe?.m_item;
            ItemDrop.ItemData.SharedData? sharedData = item?.m_itemData?.m_shared;
            if (item == null || sharedData == null)
            {
                continue;
            }

            AddRecipeLookup(GetPrefabName(item.gameObject), recipe!, index);
            AddRecipeLookup(sharedData.m_name, recipe!, index);
        }
    }

    private static void AddRecipeLookup(string key, Recipe recipe, int index)
    {
        if (!string.IsNullOrWhiteSpace(key) && !RecipesByPrefabOrSharedName.ContainsKey(key))
        {
            RecipesByPrefabOrSharedName.Add(key, new RecipeLookupEntry(recipe, index));
        }
    }

    private static bool ApplyUpgradeRequirementsFromTemplate(Recipe targetRecipe, Recipe templateRecipe)
    {
        List<Piece.Requirement> requirements = new(targetRecipe.m_resources ?? Array.Empty<Piece.Requirement>());
        HashSet<Piece.Requirement> templateHandledRequirements = new();
        bool changed = false;
        foreach (Piece.Requirement templateRequirement in templateRecipe.m_resources ?? Array.Empty<Piece.Requirement>())
        {
            if (templateRequirement?.m_resItem == null)
            {
                continue;
            }

            Piece.Requirement? targetRequirement = FindRequirement(requirements, templateRequirement.m_resItem);
            if (targetRequirement == null)
            {
                if (templateRequirement.m_amountPerLevel <= 0)
                {
                    continue;
                }

                requirements.Add(new Piece.Requirement
                {
                    m_resItem = templateRequirement.m_resItem,
                    m_amount = 0,
                    m_amountPerLevel = templateRequirement.m_amountPerLevel,
                    m_recover = templateRequirement.m_recover
                });
                changed = true;
                continue;
            }

            templateHandledRequirements.Add(targetRequirement);
            if (targetRequirement.m_amountPerLevel != templateRequirement.m_amountPerLevel)
            {
                targetRequirement.m_amountPerLevel = templateRequirement.m_amountPerLevel;
                changed = true;
            }
        }

        foreach (Piece.Requirement requirement in requirements)
        {
            if (requirement?.m_resItem == null || templateHandledRequirements.Contains(requirement))
            {
                continue;
            }

            if (requirement.m_amountPerLevel != 0)
            {
                requirement.m_amountPerLevel = 0;
                changed = true;
            }
        }

        targetRecipe.m_resources = requirements.ToArray();
        return changed || HasUpgradeRequirements(targetRecipe);
    }

    private static bool EnsureFallbackUpgradeRequirements(Recipe recipe)
    {
        bool hasUpgradeRequirements = false;
        foreach (Piece.Requirement requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
        {
            if (requirement?.m_resItem == null)
            {
                continue;
            }

            if (requirement.m_amountPerLevel <= 0)
            {
                requirement.m_amountPerLevel = Mathf.Max(1, Mathf.CeilToInt(requirement.m_amount * 0.5f));
            }

            hasUpgradeRequirements = true;
        }

        return hasUpgradeRequirements;
    }

    private static bool HasUpgradeRequirements(Recipe recipe)
    {
        foreach (Piece.Requirement requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
        {
            if (requirement?.m_resItem != null && requirement.m_amountPerLevel > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Piece.Requirement? FindRequirement(
        List<Piece.Requirement> requirements,
        ItemDrop resourceItem)
    {
        string resourceName = GetPrefabName(resourceItem.gameObject);
        foreach (Piece.Requirement requirement in requirements)
        {
            if (requirement?.m_resItem == null)
            {
                continue;
            }

            if (string.Equals(GetPrefabName(requirement.m_resItem.gameObject), resourceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requirement.m_resItem.m_itemData.m_shared.m_name, resourceItem.m_itemData.m_shared.m_name, StringComparison.OrdinalIgnoreCase))
            {
                return requirement;
            }
        }

        return null;
    }

    private static void ConfigureWeaponDurability(ItemDrop.ItemData weapon)
    {
        ItemDrop.ItemData.SharedData sharedData = weapon.m_shared;
        weapon.m_stack = 1;
        sharedData.m_maxStackSize = 1;
        sharedData.m_autoStack = false;
        sharedData.m_useDurability = true;
        sharedData.m_canBeReparied = true;
        sharedData.m_ammoType = "";
        if (sharedData.m_useDurabilityDrain <= 0f)
        {
            sharedData.m_useDurabilityDrain = DefaultUseDurabilityDrain;
        }

        float previousMaxDurability = sharedData.m_maxDurability;
        bool maxDurabilityWasConsumableSized = sharedData.m_maxDurability <= sharedData.m_useDurabilityDrain + 0.001f;
        if (sharedData.m_maxDurability <= 0f || maxDurabilityWasConsumableSized)
        {
            sharedData.m_maxDurability = DefaultMaxDurability;
        }

        PatchAttack(sharedData.m_attack);
        PatchAttack(sharedData.m_secondaryAttack);

        bool initialized = weapon.m_customData.ContainsKey(DurabilityInitializedKey);
        if (!initialized || maxDurabilityWasConsumableSized)
        {
            if (weapon.m_durability <= 0f ||
                maxDurabilityWasConsumableSized && weapon.m_durability <= previousMaxDurability + 0.001f)
            {
                weapon.m_durability = weapon.GetMaxDurability();
            }

            weapon.m_customData[DurabilityInitializedKey] = "true";
        }
    }

    private static void EnsureDropPrefab(ItemDrop.ItemData weapon)
    {
        ObjectDB? objectDb = ObjectDB.instance;
        if (weapon.m_dropPrefab != null || objectDb?.m_items == null || weapon.m_shared == null)
        {
            return;
        }

        EnsureDropPrefabLookup(objectDb);
        if (!DropPrefabsBySharedName.TryGetValue(weapon.m_shared.m_name, out GameObject? itemPrefab))
        {
            return;
        }

        weapon.m_dropPrefab = itemPrefab;
        PatchedWeaponPrefabNames.Add(GetPrefabName(itemPrefab));
        PatchedWeaponSharedNames.Add(weapon.m_shared.m_name);
        if (DebugLoggingEnabled)
        {
            LogDebug(
                $"Restored missing drop prefab for Warfare throwable prefab={GetPrefabName(itemPrefab)} shared={weapon.m_shared.m_name}");
        }
    }

    private static void EnsureDropPrefabLookup(ObjectDB objectDb)
    {
        int itemCount = objectDb.m_items?.Count ?? 0;
        if (ReferenceEquals(_cachedDropPrefabObjectDb, objectDb) &&
            _cachedDropPrefabItemCount == itemCount)
        {
            return;
        }

        DropPrefabsBySharedName.Clear();
        _cachedDropPrefabObjectDb = objectDb;
        _cachedDropPrefabItemCount = itemCount;
        if (objectDb.m_items == null)
        {
            return;
        }

        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            string? sharedName = itemDrop?.m_itemData?.m_shared?.m_name;
            if (!string.IsNullOrWhiteSpace(sharedName) && !DropPrefabsBySharedName.ContainsKey(sharedName!))
            {
                DropPrefabsBySharedName.Add(sharedName!, itemPrefab);
            }
        }
    }

    private static float GetDurabilityDrain(ItemDrop.ItemData weapon)
    {
        float drain = weapon.m_shared.m_useDurabilityDrain;
        return drain > 0f ? drain : DefaultUseDurabilityDrain;
    }

    private static void MarkBrokenRemovalPreservationIfNeeded(ItemDrop.ItemData weapon)
    {
        if (weapon.m_durability > 0.001f)
        {
            BrokenRemovalPreservations.Remove(weapon);
            return;
        }

        float expiresAt = Time.time + BrokenRemovalPreservationSeconds;
        BrokenRemovalPreservations.Remove(weapon);
        BrokenRemovalPreservations.Add(weapon, new BrokenRemovalPreservationState(expiresAt));
        if (DebugLoggingEnabled)
        {
            LogDebug($"Broken item removal preservation armed until={expiresAt} item={DescribeItem(weapon)}");
        }
    }

    private static bool ShouldPreserveBrokenRemoval(ItemDrop.ItemData? item, string source)
    {
        if (item == null)
        {
            return false;
        }

        if (item.m_durability > 0.001f)
        {
            BrokenRemovalPreservations.Remove(item);
            return false;
        }

        if (!BrokenRemovalPreservations.TryGetValue(item, out BrokenRemovalPreservationState? state))
        {
            return false;
        }

        if (Time.time > state.ExpiresAt)
        {
            BrokenRemovalPreservations.Remove(item);
            if (DebugLoggingEnabled)
            {
                LogDebug($"Broken item removal preservation expired source={source} item={DescribeItem(item)}");
            }

            return false;
        }

        return true;
    }

    // Recognition helpers intentionally accept both patched prefabs and copied item instances.
    private static bool IsWarfareThrowableProjectile(Projectile projectile)
    {
        if (IsWarfareThrowableWeapon(projectile.m_spawnItem) ||
            IsWarfareThrowableItemPrefab(projectile.m_spawnOnHit))
        {
            return true;
        }

        ItemDrop.ItemData? weapon = ProjectileAccess.GetWeapon(projectile);
        if (IsWarfareThrowableWeapon(weapon))
        {
            return true;
        }

        return ShouldPatchProjectilePrefabName(GetPrefabName(projectile.gameObject));
    }

    private static short ResolveProjectileToolTier(Projectile projectile)
    {
        short toolTier = 0;
        HitData? originalHitData = ProjectileAccess.GetOriginalHitData(projectile);
        if (originalHitData != null)
        {
            toolTier = originalHitData.m_toolTier;
        }

        ItemDrop.ItemData? weapon = ProjectileAccess.GetWeapon(projectile);
        if (weapon?.m_shared != null)
        {
            toolTier = (short)Mathf.Max(toolTier, weapon.m_shared.m_toolTier);
        }

        return toolTier;
    }

    private static bool IsWarfareThrowableWeapon(ItemDrop.ItemData? item)
    {
        ItemDrop.ItemData.SharedData? sharedData = item?.m_shared;
        if (sharedData == null)
        {
            return false;
        }

        string prefabName = GetItemPrefabName(item);
        bool knownSharedName = PatchedWeaponSharedNames.Contains(sharedData.m_name);
        bool knownPrefabName = PatchedWeaponPrefabNames.Contains(prefabName) ||
                               IsWarfareThrowableWeaponPrefabName(prefabName);
        bool hasProjectileAttack = HasProjectileAttack(sharedData);
        if ((knownSharedName || knownPrefabName) && hasProjectileAttack)
        {
            return true;
        }

        if (!HasThrowingSkill(sharedData) || !hasProjectileAttack)
        {
            return false;
        }

        return string.IsNullOrEmpty(prefabName) ||
               IsWarfarePrefabName(prefabName) ||
               HasWarfareThrowableProjectileAttack(sharedData);
    }

    private static bool IsWarfareThrowableItemPrefab(GameObject? prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        string prefabName = GetPrefabName(prefab);
        if (PatchedWeaponPrefabNames.Contains(prefabName) ||
            IsWarfareThrowableWeaponPrefabName(prefabName))
        {
            return true;
        }

        ItemDrop? itemDrop = prefab.GetComponent<ItemDrop>() ?? prefab.GetComponentInChildren<ItemDrop>();
        return IsWarfareThrowableWeapon(itemDrop?.m_itemData);
    }

    private static bool IsWarfareThrowableWeaponPrefab(GameObject itemPrefab, ItemDrop.ItemData.SharedData? sharedData)
    {
        if (sharedData == null || !HasProjectileAttack(sharedData))
        {
            return false;
        }

        string prefabName = GetPrefabName(itemPrefab);
        return IsWarfareThrowableWeaponPrefabName(prefabName) ||
               (HasThrowingSkill(sharedData) && IsWarfarePrefabName(prefabName));
    }

    private static bool HasThrowingSkill(ItemDrop.ItemData.SharedData sharedData)
    {
        return sharedData.m_skillType == ThrowingSkillType;
    }

    private static bool HasProjectileAttack(ItemDrop.ItemData.SharedData sharedData)
    {
        return IsProjectileAttack(sharedData.m_attack) || IsProjectileAttack(sharedData.m_secondaryAttack);
    }

    private static bool HasWarfareThrowableProjectileAttack(ItemDrop.ItemData.SharedData sharedData)
    {
        return UsesWarfareThrowableProjectile(sharedData.m_attack) ||
               UsesWarfareThrowableProjectile(sharedData.m_secondaryAttack);
    }

    private static bool IsProjectileAttack(Attack? attack)
    {
        return attack != null &&
               (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackProjectile != null);
    }

    private static bool UsesWarfareThrowableProjectile(Attack? attack)
    {
        return attack?.m_attackProjectile != null &&
               ShouldPatchProjectilePrefabName(GetPrefabName(attack.m_attackProjectile));
    }

    private static bool IsWarfareThrowableWeaponPrefabName(string prefabName)
    {
        return prefabName.StartsWith(ThrowableWeaponPrefix, StringComparison.OrdinalIgnoreCase) &&
               prefabName.EndsWith(WarfarePrefabSuffix, StringComparison.OrdinalIgnoreCase) &&
               !prefabName.EndsWith(ThrowableProjectileSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPatchProjectilePrefabName(string prefabName)
    {
        return PatchedProjectilePrefabNames.Contains(prefabName) ||
               (prefabName.StartsWith(ThrowableProjectilePrefix, StringComparison.OrdinalIgnoreCase) &&
                prefabName.EndsWith(ThrowableProjectileSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWarfarePrefabName(string prefabName)
    {
        return prefabName.EndsWith(WarfarePrefabSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPrefabName(GameObject prefab)
    {
        const string cloneSuffix = "(Clone)";
        string prefabName = prefab.name;
        return prefabName.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? prefabName.Substring(0, prefabName.Length - cloneSuffix.Length)
            : prefabName;
    }

    private static string GetItemPrefabName(ItemDrop.ItemData? item)
    {
        return item?.m_dropPrefab != null ? GetPrefabName(item.m_dropPrefab) : string.Empty;
    }

    private static bool LooksLikeWarfareThrowablePrefab(GameObject itemPrefab, ItemDrop.ItemData.SharedData? sharedData)
    {
        string prefabName = GetPrefabName(itemPrefab);
        return LooksLikeWarfareThrowableName(prefabName) ||
               LooksLikeWarfareThrowableName(sharedData?.m_name) ||
               (sharedData != null && HasThrowingSkill(sharedData) && HasProjectileAttack(sharedData));
    }

    private static bool LooksLikeWarfareThrowableItem(ItemDrop.ItemData? item)
    {
        ItemDrop.ItemData.SharedData? sharedData = item?.m_shared;
        if (sharedData == null)
        {
            return false;
        }

        string prefabName = GetItemPrefabName(item);
        return LooksLikeWarfareThrowableName(prefabName) ||
               LooksLikeWarfareThrowableName(sharedData.m_name) ||
               PatchedWeaponSharedNames.Contains(sharedData.m_name) ||
               PatchedWeaponPrefabNames.Contains(prefabName) ||
               (HasThrowingSkill(sharedData) && HasProjectileAttack(sharedData));
    }

    private static bool LooksLikeWarfareThrowableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name!.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return name.IndexOf(ThrowableWeaponPrefix, StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf(ThrowableSharedNameToken, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Debug formatters stay at the bottom so runtime logic does not interleave with logging details.
    private static string DescribeItem(ItemDrop.ItemData? item)
    {
        if (item?.m_shared == null)
        {
            return "<null>";
        }

        ItemDrop.ItemData.SharedData sharedData = item.m_shared;
        string initialized = item.m_customData != null && item.m_customData.ContainsKey(DurabilityInitializedKey)
            ? "yes"
            : "no";
        return "{"
               + $"prefab={GetItemPrefabName(item)}"
               + $", shared={sharedData.m_name}"
               + $", type={sharedData.m_itemType}"
               + $", skill={sharedData.m_skillType}({(int)sharedData.m_skillType})"
               + $", toolTier={sharedData.m_toolTier}"
               + $", stack={item.m_stack}/{sharedData.m_maxStackSize}"
               + $", autoStack={sharedData.m_autoStack}"
               + $", equipped={item.m_equipped}"
               + $", ammoType={sharedData.m_ammoType}"
               + $", useDurability={sharedData.m_useDurability}"
               + $", durability={item.m_durability}/{sharedData.m_maxDurability}"
               + $", drain={sharedData.m_useDurabilityDrain}"
               + $", initialized={initialized}"
               + "}";
    }

    private static string DescribeAttack(Attack? attack)
    {
        if (attack == null)
        {
            return "<null>";
        }

        return "{"
               + $"type={attack.m_attackType}"
               + $", animation={attack.m_attackAnimation}"
               + $", projectile={GetObjectName(attack.m_attackProjectile)}"
               + $", consumeItem={attack.m_consumeItem}"
               + $", spawnOnHit={GetObjectName(attack.m_spawnOnHit)}"
               + $", spawnOnHitChance={attack.m_spawnOnHitChance}"
               + $", projectiles={attack.m_projectiles}"
               + $", bursts={attack.m_projectileBursts}"
               + $", perBurstResource={attack.m_perBurstResourceUsage}"
               + "}";
    }

    private static string DescribeDamage(HitData.DamageTypes damage)
    {
        return "{"
               + $"damage={damage.m_damage}"
               + $", blunt={damage.m_blunt}"
               + $", slash={damage.m_slash}"
               + $", pierce={damage.m_pierce}"
               + $", chop={damage.m_chop}"
               + $", pickaxe={damage.m_pickaxe}"
               + $", fire={damage.m_fire}"
               + $", frost={damage.m_frost}"
               + $", lightning={damage.m_lightning}"
               + $", poison={damage.m_poison}"
               + $", spirit={damage.m_spirit}"
               + "}";
    }

    private static string DescribeProjectile(Projectile? projectile)
    {
        if (projectile == null)
        {
            return "<null>";
        }

        return "{"
               + $"prefab={GetPrefabName(projectile.gameObject)}"
               + $", weapon={DescribeItem(ProjectileAccess.GetWeapon(projectile))}"
               + $", spawnItem={DescribeItem(projectile.m_spawnItem)}"
               + $", respawnItemOnHit={projectile.m_respawnItemOnHit}"
               + $", spawnOnHit={GetObjectName(projectile.m_spawnOnHit)}"
               + $", spawnOnHitChance={projectile.m_spawnOnHitChance}"
               + $", randomSpawnOnHit={DescribePrefabList(projectile.m_randomSpawnOnHit)}"
               + $", aoe={projectile.m_aoe}"
               + $", originalToolTier={ProjectileAccess.GetOriginalHitData(projectile)?.m_toolTier ?? 0}"
               + "}";
    }

    private static string DescribePrefabList(List<GameObject> prefabs)
    {
        if (prefabs == null || prefabs.Count == 0)
        {
            return "[]";
        }

        List<string> names = new();
        foreach (GameObject prefab in prefabs)
        {
            names.Add(GetObjectName(prefab));
        }

        return "[" + string.Join(",", names) + "]";
    }

    private static string GetObjectName(GameObject? prefab)
    {
        return prefab != null ? GetPrefabName(prefab) : "<null>";
    }

    private sealed class RecipeLookupEntry
    {
        public RecipeLookupEntry(Recipe recipe, int index)
        {
            Recipe = recipe;
            Index = index;
        }

        public Recipe Recipe { get; }

        public int Index { get; }
    }

    private sealed class BrokenRemovalPreservationState
    {
        public BrokenRemovalPreservationState(float expiresAt)
        {
            ExpiresAt = expiresAt;
        }

        public float ExpiresAt { get; }
    }

    internal readonly struct ProjectileDurabilityDrainState
    {
        internal static readonly ProjectileDurabilityDrainState Empty = new(null, 0f);

        internal ProjectileDurabilityDrainState(ItemDrop.ItemData? weapon, float beforeDurability)
        {
            Weapon = weapon;
            BeforeDurability = beforeDurability;
        }

        internal ItemDrop.ItemData? Weapon { get; }

        internal float BeforeDurability { get; }

        internal bool Applies => Weapon != null;
    }
}
