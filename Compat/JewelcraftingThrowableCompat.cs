using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace WarfareTweaks;

internal static class JewelcraftingThrowableCompat
{
    private const string UtilsTypeName = "Jewelcrafting.Utils";
    private const string JewelcraftingTypeName = "Jewelcrafting.Jewelcrafting";
    private static bool _hooksInstalled;
    private static bool _reportedFailure;
    private static FieldInfo? _socketBlacklistField;
    private static FieldInfo? _prefabBlacklistField;

    internal static void TryInstallHooks()
    {
        if (_hooksInstalled)
        {
            return;
        }

        if (!TryGetJewelcraftingAssembly(out Assembly? jewelcraftingAssembly) || jewelcraftingAssembly == null)
        {
            return;
        }

        Type? utilsType = jewelcraftingAssembly.GetType(UtilsTypeName, throwOnError: false);
        MethodInfo? isSocketableItemMethod = utilsType?.GetMethod(
            "IsSocketableItem",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(ItemDrop.ItemData) },
            modifiers: null);
        MethodInfo? postfixMethod = AccessTools.DeclaredMethod(
            typeof(JewelcraftingThrowableCompat),
            nameof(IsSocketableItemPostfix));
        MethodInfo? updateRecipeListMethod = AccessTools.DeclaredMethod(
            typeof(InventoryGui),
            nameof(InventoryGui.UpdateRecipeList));
        MethodInfo? prepareInventoryPrefixMethod = AccessTools.DeclaredMethod(
            typeof(JewelcraftingThrowableCompat),
            nameof(PrepareInventoryBeforeRecipeListPrefix));

        if (isSocketableItemMethod == null || postfixMethod == null || prepareInventoryPrefixMethod == null)
        {
            ReportFailure("Jewelcrafting.Utils.IsSocketableItem(ItemData) was not found.");
            return;
        }

        CacheBlacklistFields(jewelcraftingAssembly);
        Harmony harmony = new("sighsorry.WarfareTweaks.JewelcraftingThrowableCompat");
        harmony.Patch(isSocketableItemMethod, postfix: new HarmonyMethod(postfixMethod));
        if (updateRecipeListMethod != null)
        {
            harmony.Patch(
                updateRecipeListMethod,
                prefix: new HarmonyMethod(prepareInventoryPrefixMethod) { priority = Priority.First });
        }
        else
        {
            ReportFailure("InventoryGui.UpdateRecipeList was not found; socket tab pre-normalization is unavailable.");
        }

        _hooksInstalled = true;
        WarfareTweaksPlugin.ModLogger.LogInfo(
            "Installed Jewelcrafting compatibility hooks for Warfare throwing axe socketing.");
    }

    private static void PrepareInventoryBeforeRecipeListPrefix()
    {
        Inventory? inventory = Player.m_localPlayer?.GetInventory();
        if (inventory == null)
        {
            return;
        }

        foreach (ItemDrop.ItemData item in inventory.m_inventory)
        {
            if (!WarfareThrowableCompat.TryPrepareJewelcraftingSocketableWeapon(item))
            {
                continue;
            }

            RemovePrefabBlacklistEntry(item);
            WarfareThrowableCompat.LogDebug(
                $"Jewelcrafting pre-normalized Warfare throwable prefab={GetItemPrefabName(item)} shared={item.m_shared?.m_name}");
        }
    }

    private static void IsSocketableItemPostfix(ItemDrop.ItemData item, ref bool __result)
    {
        if (__result || item == null || IsUserSocketBlacklisted(item))
        {
            return;
        }

        if (!WarfareThrowableCompat.TryPrepareJewelcraftingSocketableWeapon(item))
        {
            return;
        }

        RemovePrefabBlacklistEntry(item);
        __result = true;
        WarfareThrowableCompat.LogDebug(
            $"Jewelcrafting socketability allowed for Warfare throwable prefab={GetItemPrefabName(item)} shared={item.m_shared?.m_name}");
    }

    private static bool IsUserSocketBlacklisted(ItemDrop.ItemData item)
    {
        string prefabName = GetItemPrefabName(item);
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        if (_socketBlacklistField?.GetValue(null) is ConfigEntry<string> socketBlacklist)
        {
            string[] entries = socketBlacklist.Value.Replace(" ", "").Split(',');
            foreach (string entry in entries)
            {
                if (string.Equals(entry, prefabName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void RemovePrefabBlacklistEntry(ItemDrop.ItemData item)
    {
        string prefabName = GetItemPrefabName(item);
        if (string.IsNullOrWhiteSpace(prefabName) ||
            _prefabBlacklistField?.GetValue(null) is not ICollection<string> prefabBlacklist ||
            !prefabBlacklist.Contains(prefabName))
        {
            return;
        }

        prefabBlacklist.Remove(prefabName);
    }

    private static void CacheBlacklistFields(Assembly jewelcraftingAssembly)
    {
        Type? jewelcraftingType = jewelcraftingAssembly.GetType(JewelcraftingTypeName, throwOnError: false);
        _socketBlacklistField = jewelcraftingType != null
            ? AccessTools.Field(jewelcraftingType, "socketBlacklist")
            : null;
        _prefabBlacklistField = jewelcraftingType != null
            ? AccessTools.Field(jewelcraftingType, "PrefabBlacklist")
            : null;
    }

    private static bool TryGetJewelcraftingAssembly(out Assembly? jewelcraftingAssembly)
    {
        jewelcraftingAssembly = null;
        if (Chainloader.PluginInfos.TryGetValue(WarfareTweaksCompat.JewelcraftingGuid, out var pluginInfo))
        {
            jewelcraftingAssembly = pluginInfo.Instance?.GetType().Assembly;
            if (jewelcraftingAssembly != null)
            {
                return true;
            }
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType(UtilsTypeName, throwOnError: false) != null)
            {
                jewelcraftingAssembly = assembly;
                return true;
            }
        }

        return false;
    }

    private static void ReportFailure(string message)
    {
        if (_reportedFailure)
        {
            return;
        }

        _reportedFailure = true;
        WarfareTweaksPlugin.ModLogger.LogWarning(
            $"Jewelcrafting Warfare throwable compatibility skipped: {message}");
    }

    private static string GetItemPrefabName(ItemDrop.ItemData item)
    {
        const string cloneSuffix = "(Clone)";
        if (item.m_dropPrefab == null)
        {
            return string.Empty;
        }

        string prefabName = item.m_dropPrefab.name;
        return prefabName.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? prefabName.Substring(0, prefabName.Length - cloneSuffix.Length)
            : prefabName;
    }
}
