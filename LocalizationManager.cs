using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using YamlDotNet.Serialization;

namespace LocalizationManager;

internal static class Localizer
{
    private static readonly string[] FileExtensions = { ".json", ".yml" };
    private static BaseUnityPlugin? _plugin;

    private static BaseUnityPlugin Plugin =>
        _plugin ?? throw new InvalidOperationException("Localization was used before the plugin was registered.");

    internal static void Load(BaseUnityPlugin plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        if (Localization.instance != null)
        {
            LoadLocalization(Localization.instance, Localization.instance.GetSelectedLanguage());
        }
    }

    private static void LoadLocalizationLater()
    {
        if (Localization.instance != null)
        {
            LoadLocalization(Localization.instance, Localization.instance.GetSelectedLanguage());
        }
    }

    private static void LoadLocalization(Localization __instance, string language)
    {
        Localization localization = __instance;
        Dictionary<string, string> localizationFiles = FindLocalizationFiles();
        if (LoadTranslationFromAssembly("English") is not { } englishAssemblyData)
        {
            Debug.LogWarning($"Found no English localizations in mod {Plugin.Info.Metadata.Name}. Expected an embedded resource translations/English.json or translations/English.yml.");
            return;
        }

        Dictionary<string, string> localizationTexts = DeserializeLocalizationText(
            Encoding.UTF8.GetString(englishAssemblyData),
            "embedded English localization");
        if (localizationTexts.Count == 0)
        {
            Debug.LogWarning($"Localization for mod {Plugin.Info.Metadata.Name} failed: English localization file was empty or invalid.");
            return;
        }

        string? localizationData = null;
        if (language != "English")
        {
            if (localizationFiles.TryGetValue(language, out string? localizationFile))
            {
                localizationData = File.ReadAllText(localizationFile);
            }
            else if (LoadTranslationFromAssembly(language) is { } languageAssemblyData)
            {
                localizationData = Encoding.UTF8.GetString(languageAssemblyData);
            }
        }

        if (localizationData == null && localizationFiles.TryGetValue("English", out string? englishFile))
        {
            localizationData = File.ReadAllText(englishFile);
        }

        if (localizationData != null)
        {
            foreach (KeyValuePair<string, string> kv in DeserializeLocalizationText(localizationData, $"{language} localization"))
            {
                localizationTexts[kv.Key] = kv.Value;
            }
        }

        foreach (KeyValuePair<string, string> text in localizationTexts)
        {
            localization.AddWord(text.Key, text.Value);
        }
    }

    private static Dictionary<string, string> DeserializeLocalizationText(string localizationData, string sourceDescription)
    {
        try
        {
            return new DeserializerBuilder().IgnoreFields().Build()
                       .Deserialize<Dictionary<string, string>?>(localizationData) ??
                   new Dictionary<string, string>();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse {sourceDescription} for mod {Plugin.Info.Metadata.Name}: {e.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static Dictionary<string, string> FindLocalizationFiles()
    {
        Dictionary<string, string> localizationFiles = new();
        string pluginRoot = Paths.PluginPath;
        if (!Directory.Exists(pluginRoot))
        {
            return localizationFiles;
        }

        foreach (string file in Directory.GetFiles(
                     pluginRoot,
                     $"{Plugin.Info.Metadata.Name}.*",
                     SearchOption.AllDirectories).Where(file => FileExtensions.Contains(Path.GetExtension(file))))
        {
            string[] parts = Path.GetFileNameWithoutExtension(file).Split('.');
            if (parts.Length < 2)
            {
                continue;
            }

            string language = parts[1];
            if (localizationFiles.ContainsKey(language))
            {
                Debug.LogWarning($"Duplicate key {language} found for {Plugin.Info.Metadata.Name}. The duplicate file found at {file} will be skipped.");
                continue;
            }

            localizationFiles[language] = file;
        }

        return localizationFiles;
    }

    static Localizer()
    {
        Harmony harmony = new("org.bepinex.helpers.LocalizationManager");
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(Localization), nameof(Localization.SetupLanguage)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Localizer), nameof(LoadLocalization))));
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(FejdStartup), nameof(FejdStartup.SetupGui)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Localizer), nameof(LoadLocalizationLater))));
    }

    private static byte[]? LoadTranslationFromAssembly(string language)
    {
        foreach (string extension in FileExtensions)
        {
            if (ReadEmbeddedFileBytes("translations." + language + extension) is { } data)
            {
                return data;
            }
        }

        return null;
    }

    private static byte[]? ReadEmbeddedFileBytes(string resourceFileName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using MemoryStream stream = new();
        if (assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(resourceFileName, StringComparison.Ordinal)) is { } resourceName)
        {
            assembly.GetManifestResourceStream(resourceName)?.CopyTo(stream);
        }

        return stream.Length == 0 ? null : stream.ToArray();
    }
}
