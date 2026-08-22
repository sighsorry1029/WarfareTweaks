using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WarfareTweaks;

internal static class WarfareTweaksCompat
{
    internal const string WarfareGuid = "Therzie.Warfare";
    internal const string WarfareFireAndIceGuid = "Therzie.WarfareFireAndIce";
    internal const string JewelcraftingGuid = "org.bepinex.plugins.jewelcrafting";

    private static readonly Dictionary<string, Type> LoadedTypesByName = new(StringComparer.Ordinal);

    internal static Type? FindLoadedType(string? fullTypeName)
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

    internal static string NormalizePrefabName(string? prefabName)
    {
        string normalizedName = prefabName?.Trim() ?? "";
        const string cloneSuffix = "(Clone)";
        if (normalizedName.EndsWith(cloneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = normalizedName.Substring(0, normalizedName.Length - cloneSuffix.Length).TrimEnd();
        }

        return normalizedName;
    }

    internal static bool TryPatch(
        Harmony harmony,
        MethodBase original,
        string featureName,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null,
        HarmonyMethod? transpiler = null)
    {
        try
        {
            harmony.Patch(original, prefix, postfix, transpiler);
            return true;
        }
        catch (Exception exception)
        {
            string targetName = $"{original.DeclaringType?.FullName}.{original.Name}";
            WarfareTweaksWarningLog.LogOnce(
                $"compat_patch_failed_{featureName}_{targetName}",
                $"Skipping {featureName} compatibility hook for '{targetName}': {exception.Message}");
            return false;
        }
    }
}
