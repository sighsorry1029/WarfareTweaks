using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace WarfareTweaks;

internal static class WarfareItemManagerSyncCompat
{
    private const string ItemManagerTypeName = "ItemManager.Item";
    private const string CraftingTableTypeName = "ItemManager.CraftingTable";
    private const string ToggleTypeName = "ItemManager.Toggle";

    private static readonly HashSet<string> RecipeConfigKeys = new(StringComparer.Ordinal)
    {
        "Crafting Station",
        "Custom Crafting Station",
        "Crafting Station Level",
        "Maximum Crafting Station Level",
        "Require only one resource",
        "Quality Multiplier",
        "Crafting Costs",
        "Upgrading Costs"
    };

    private static readonly HashSet<string> ItemOnlyRecipeConfigKeys = new(StringComparer.Ordinal)
    {
        "Crafting Station Level",
        "Maximum Crafting Station Level",
        "Require only one resource",
        "Quality Multiplier",
        "Upgrading Costs"
    };

    private static readonly ItemManagerSyncTarget[] Targets =
    {
        new(
            WarfareTweaksCompat.WarfareGuid,
            "Warfare",
            new[]
            {
                RecipeEntry("Crystal Battleaxe", "Crafting Station", "Forge"),
                RecipeEntry("Crystal Battleaxe", "Custom Crafting Station", ""),
                RecipeEntry("Crystal Battleaxe", "Crafting Station Level", "4"),
                RecipeEntry("Crystal Battleaxe", "Maximum Crafting Station Level", "7"),
                RecipeEntry("Crystal Battleaxe", "Require only one resource", "Off"),
                RecipeEntry("Crystal Battleaxe", "Quality Multiplier", "1"),
                RecipeEntry("Crystal Battleaxe", "Crafting Costs", "ElderBark:40,Silver:30,Crystal:10"),
                RecipeEntry("Crystal Battleaxe", "Upgrading Costs", "ElderBark:5,Silver:15,Crystal:3"),

                RecipeEntry("Stagbreaker", "Crafting Station", "Workbench"),
                RecipeEntry("Stagbreaker", "Custom Crafting Station", ""),
                RecipeEntry("Stagbreaker", "Crafting Station Level", "2"),
                RecipeEntry("Stagbreaker", "Maximum Crafting Station Level", "5"),
                RecipeEntry("Stagbreaker", "Require only one resource", "Off"),
                RecipeEntry("Stagbreaker", "Quality Multiplier", "1"),
                RecipeEntry("Stagbreaker", "Crafting Costs", "RoundLog:20,TrophyDeer:5,LeatherScraps:2"),
                RecipeEntry("Stagbreaker", "Upgrading Costs", "RoundLog:5,TrophyDeer:2,LeatherScraps:1,BoneFragments:10"),

                RecipeEntry("Iron Sledge", "Crafting Station", "Forge"),
                RecipeEntry("Iron Sledge", "Custom Crafting Station", ""),
                RecipeEntry("Iron Sledge", "Crafting Station Level", "2"),
                RecipeEntry("Iron Sledge", "Maximum Crafting Station Level", "5"),
                RecipeEntry("Iron Sledge", "Require only one resource", "Off"),
                RecipeEntry("Iron Sledge", "Quality Multiplier", "1"),
                RecipeEntry("Iron Sledge", "Crafting Costs", "ElderBark:10,Iron:30,YmirRemains:4,TrophyDraugrElite:1"),
                RecipeEntry("Iron Sledge", "Upgrading Costs", "ElderBark:2,Iron:15,YmirRemains:2"),

                RecipeEntry("Demolisher", "Crafting Station", "BlackForge"),
                RecipeEntry("Demolisher", "Custom Crafting Station", ""),
                RecipeEntry("Demolisher", "Crafting Station Level", "1"),
                RecipeEntry("Demolisher", "Maximum Crafting Station Level", "4"),
                RecipeEntry("Demolisher", "Require only one resource", "Off"),
                RecipeEntry("Demolisher", "Quality Multiplier", "1"),
                RecipeEntry("Demolisher", "Crafting Costs", "YggdrasilWood:10,Iron:20,Eitr:10"),
                RecipeEntry("Demolisher", "Upgrading Costs", "YggdrasilWood:2,Iron:15,Eitr:2")
            })
    };

    private static readonly MethodInfo ConfigFileBindMethod = typeof(ConfigFile)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Single(static method =>
        {
            if (!method.IsGenericMethodDefinition || method.Name != nameof(ConfigFile.Bind))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 4 &&
                   parameters[0].ParameterType == typeof(string) &&
                   parameters[1].ParameterType == typeof(string) &&
                   parameters[3].ParameterType == typeof(ConfigDescription);
        });

    private static readonly HashSet<string> MissingTargetWarnings = new(StringComparer.OrdinalIgnoreCase);

    internal static void RegisterMissingRecipeConfigs()
    {
        foreach (ItemManagerSyncTarget target in Targets)
        {
            try
            {
                RegisterMissingRecipeConfigs(target);
            }
            catch (Exception exception)
            {
                LogTargetWarningOnce(target, $"Failed to run {target.DisplayName} ItemManager sync compat: {exception.Message}");
            }
        }
    }

    private static void RegisterMissingRecipeConfigs(ItemManagerSyncTarget target)
    {
        if (!TryGetPlugin(target.PluginGuid, out BaseUnityPlugin? plugin) || plugin == null)
        {
            return;
        }

        Assembly assembly = plugin.GetType().Assembly;
        Type? itemManagerType = assembly.GetType(ItemManagerTypeName, throwOnError: false);
        Type? craftingTableType = assembly.GetType(CraftingTableTypeName, throwOnError: false);
        Type? toggleType = assembly.GetType(ToggleTypeName, throwOnError: false);
        if (itemManagerType == null || craftingTableType == null || toggleType == null)
        {
            LogTargetWarningOnce(target, $"Skipping {target.DisplayName} ItemManager sync compat: embedded ItemManager types were not found.");
            return;
        }

        object? itemManagerConfigSync = GetItemManagerConfigSync(itemManagerType);
        if (itemManagerConfigSync == null)
        {
            LogTargetWarningOnce(target, $"Skipping {target.DisplayName} ItemManager sync compat: ItemManager ConfigSync could not be resolved.");
            return;
        }

        Dictionary<string, RecipeConfigDefinition> definitions = BuildDefinitions(target, plugin.Config, craftingTableType, toggleType);
        if (definitions.Count == 0)
        {
            return;
        }

        HashSet<string> registeredConfigKeys = GetRegisteredConfigKeys(itemManagerConfigSync);
        int registeredCount = 0;
        bool saveOnConfigSet = plugin.Config.SaveOnConfigSet;
        plugin.Config.SaveOnConfigSet = false;

        try
        {
            foreach (RecipeConfigDefinition definition in definitions.Values)
            {
                if (registeredConfigKeys.Contains(ConfigKey(definition.Section, definition.Key)))
                {
                    continue;
                }

                ConfigEntryBase entry = BindConfigEntry(plugin.Config, definition);
                AddConfigEntry(itemManagerConfigSync, entry);
                registeredConfigKeys.Add(ConfigKey(definition.Section, definition.Key));
                registeredCount++;
            }
        }
        catch (Exception exception)
        {
            LogTargetWarningOnce(target, $"Failed while registering {target.DisplayName} ItemManager sync compat entries: {exception.Message}");
            return;
        }
        finally
        {
            plugin.Config.SaveOnConfigSet = saveOnConfigSet;
        }

        if (registeredCount > 0)
        {
            WarfareTweaksPlugin.ModLogger.LogInfo(
                $"Registered {registeredCount} missing {target.DisplayName} ItemManager recipe config entry(ies) with ServerSync.");
        }
    }

    private static Dictionary<string, RecipeConfigDefinition> BuildDefinitions(
        ItemManagerSyncTarget target,
        ConfigFile configFile,
        Type craftingTableType,
        Type toggleType)
    {
        Dictionary<string, RecipeConfigDefinition> definitions = new(StringComparer.Ordinal);

        foreach (RecipeConfigSeed seed in target.KnownRecipeEntries)
        {
            if (TryCreateDefinition(seed.Section, seed.Key, seed.DefaultValue, craftingTableType, toggleType, out RecipeConfigDefinition? definition))
            {
                definitions[ConfigKey(seed.Section, seed.Key)] = definition.GetValueOrDefault();
            }
        }

        foreach (IGrouping<string, KeyValuePair<ConfigDefinition, string>> sectionGroup in GetOrphanedEntries(configFile)
                     .Where(static entry => RecipeConfigKeys.Contains(entry.Key.Key))
                     .GroupBy(static entry => entry.Key.Section, StringComparer.Ordinal))
        {
            if (!sectionGroup.Any(static entry => ItemOnlyRecipeConfigKeys.Contains(entry.Key.Key)))
            {
                continue;
            }

            foreach (KeyValuePair<ConfigDefinition, string> orphanedEntry in sectionGroup)
            {
                string section = orphanedEntry.Key.Section;
                string key = orphanedEntry.Key.Key;
                if (TryCreateDefinition(section, key, orphanedEntry.Value, craftingTableType, toggleType, out RecipeConfigDefinition? definition))
                {
                    definitions[ConfigKey(section, key)] = definition.GetValueOrDefault();
                }
            }
        }

        return definitions;
    }

    private static IEnumerable<KeyValuePair<ConfigDefinition, string>> GetOrphanedEntries(ConfigFile configFile)
    {
        PropertyInfo? orphanedEntriesProperty = typeof(ConfigFile).GetProperty(
            "OrphanedEntries",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (orphanedEntriesProperty?.GetValue(configFile) is not IEnumerable orphanedEntries)
        {
            yield break;
        }

        foreach (object orphanedEntry in orphanedEntries)
        {
            Type entryType = orphanedEntry.GetType();
            if (entryType.GetProperty("Key")?.GetValue(orphanedEntry) is ConfigDefinition definition &&
                entryType.GetProperty("Value")?.GetValue(orphanedEntry) is string value)
            {
                yield return new KeyValuePair<ConfigDefinition, string>(definition, value);
            }
        }
    }

    private static bool TryCreateDefinition(
        string section,
        string key,
        string serializedDefaultValue,
        Type craftingTableType,
        Type toggleType,
        out RecipeConfigDefinition? definition)
    {
        Type? settingType = GetSettingType(key, craftingTableType, toggleType);
        if (settingType == null)
        {
            definition = null;
            return false;
        }

        object defaultValue = ParseDefaultValue(settingType, serializedDefaultValue);
        definition = new RecipeConfigDefinition(
            section,
            key,
            settingType,
            defaultValue,
            "Registered by WarfareTweaks to keep Therzie ItemManager ServerSync recipe config entries aligned.");
        return true;
    }

    private static Type? GetSettingType(string key, Type craftingTableType, Type toggleType)
    {
        return key switch
        {
            "Crafting Station" => craftingTableType,
            "Custom Crafting Station" => typeof(string),
            "Crafting Station Level" => typeof(int),
            "Maximum Crafting Station Level" => typeof(int),
            "Require only one resource" => toggleType,
            "Quality Multiplier" => typeof(float),
            "Crafting Costs" => typeof(string),
            "Upgrading Costs" => typeof(string),
            _ => null
        };
    }

    private static object ParseDefaultValue(Type settingType, string serializedDefaultValue)
    {
        if (settingType == typeof(string))
        {
            return serializedDefaultValue;
        }

        if (settingType == typeof(int) &&
            int.TryParse(serializedDefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
        {
            return intValue;
        }

        if (settingType == typeof(float) &&
            float.TryParse(serializedDefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            return floatValue;
        }

        if (settingType.IsEnum)
        {
            try
            {
                return Enum.Parse(settingType, serializedDefaultValue, ignoreCase: true);
            }
            catch
            {
                Array values = Enum.GetValues(settingType);
                return values.Length > 0 ? values.GetValue(0)! : Activator.CreateInstance(settingType)!;
            }
        }

        return settingType.IsValueType ? Activator.CreateInstance(settingType)! : serializedDefaultValue;
    }

    private static ConfigEntryBase BindConfigEntry(ConfigFile configFile, RecipeConfigDefinition definition)
    {
        MethodInfo bindMethod = ConfigFileBindMethod.MakeGenericMethod(definition.SettingType);
        ConfigDescription description = new(definition.Description);
        return (ConfigEntryBase)bindMethod.Invoke(
            configFile,
            new[] { definition.Section, definition.Key, definition.DefaultValue, description })!;
    }

    private static object? GetItemManagerConfigSync(Type itemManagerType)
    {
        PropertyInfo? property = AccessTools.Property(itemManagerType, "configSync");
        if (property != null)
        {
            return property.GetValue(null);
        }

        MethodInfo? getter = AccessTools.Method(itemManagerType, "get_configSync");
        if (getter != null)
        {
            return getter.Invoke(null, Array.Empty<object>());
        }

        return AccessTools.Field(itemManagerType, "_configSync")?.GetValue(null);
    }

    private static void AddConfigEntry(object configSync, ConfigEntryBase entry)
    {
        MethodInfo addConfigEntryMethod = configSync
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(static method => method.Name == "AddConfigEntry" &&
                                     method.IsGenericMethodDefinition &&
                                     method.GetParameters().Length == 1);

        addConfigEntryMethod.MakeGenericMethod(entry.SettingType).Invoke(configSync, new object[] { entry });
    }

    private static HashSet<string> GetRegisteredConfigKeys(object configSync)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        if (AccessTools.Field(configSync.GetType(), "allConfigs")?.GetValue(configSync) is not IEnumerable allConfigs)
        {
            return keys;
        }

        foreach (object config in allConfigs)
        {
            PropertyInfo? baseConfigProperty = config.GetType().GetProperty("BaseConfig", BindingFlags.Instance | BindingFlags.Public);
            if (baseConfigProperty?.GetValue(config) is ConfigEntryBase entry)
            {
                keys.Add(ConfigKey(entry.Definition.Section, entry.Definition.Key));
            }
        }

        return keys;
    }

    private static bool TryGetPlugin(string pluginGuid, out BaseUnityPlugin? plugin)
    {
        if (Chainloader.PluginInfos.TryGetValue(pluginGuid, out PluginInfo pluginInfo) &&
            pluginInfo.Instance != null)
        {
            plugin = pluginInfo.Instance;
            return true;
        }

        plugin = null;
        return false;
    }

    private static void LogTargetWarningOnce(ItemManagerSyncTarget target, string message)
    {
        if (MissingTargetWarnings.Add(target.PluginGuid))
        {
            WarfareTweaksPlugin.ModLogger.LogWarning(message);
        }
    }

    private static string ConfigKey(string section, string key)
    {
        return section + '\u001f' + key;
    }

    private static RecipeConfigSeed RecipeEntry(string section, string key, string defaultValue)
    {
        return new RecipeConfigSeed(section, key, defaultValue);
    }

    private readonly struct ItemManagerSyncTarget
    {
        public ItemManagerSyncTarget(string pluginGuid, string displayName, IReadOnlyList<RecipeConfigSeed> knownRecipeEntries)
        {
            PluginGuid = pluginGuid;
            DisplayName = displayName;
            KnownRecipeEntries = knownRecipeEntries;
        }

        public string PluginGuid { get; }

        public string DisplayName { get; }

        public IReadOnlyList<RecipeConfigSeed> KnownRecipeEntries { get; }
    }

    private readonly struct RecipeConfigSeed
    {
        public RecipeConfigSeed(string section, string key, string defaultValue)
        {
            Section = section;
            Key = key;
            DefaultValue = defaultValue;
        }

        public string Section { get; }

        public string Key { get; }

        public string DefaultValue { get; }
    }

    private readonly struct RecipeConfigDefinition
    {
        public RecipeConfigDefinition(string section, string key, Type settingType, object defaultValue, string description)
        {
            Section = section;
            Key = key;
            SettingType = settingType;
            DefaultValue = defaultValue;
            Description = description;
        }

        public string Section { get; }

        public string Key { get; }

        public Type SettingType { get; }

        public object DefaultValue { get; }

        public string Description { get; }
    }
}
