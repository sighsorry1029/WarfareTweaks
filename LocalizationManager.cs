using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using YamlDotNet.Serialization;

namespace LocalizationManager;

public class Localizer
{
    private static readonly Dictionary<string, Dictionary<string, string>> LoadedTexts = new();
    private static readonly ConditionalWeakTable<Localization, string> LocalizationLanguage = new();
    private static readonly List<WeakReference<Localization>> LocalizationObjects = new();
    private static readonly string[] FileExtensions = { ".json", ".yml" };
    private static BaseUnityPlugin? _plugin;

    private static BaseUnityPlugin Plugin
    {
        get
        {
            if (_plugin == null)
            {
                IEnumerable<TypeInfo> types;
                try
                {
                    types = Assembly.GetExecutingAssembly().DefinedTypes.ToList();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(type => type != null).Select(type => type!.GetTypeInfo());
                }

                _plugin = (BaseUnityPlugin)Chainloader.ManagerObject.GetComponent(
                    types.First(type => type.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(type)));
            }

            return _plugin;
        }
    }

    public static void Load()
    {
        _ = Plugin;
        if (Localization.instance != null)
        {
            LoadLocalization(Localization.instance, Localization.instance.GetSelectedLanguage());
        }
    }

    public static void LoadLocalizationLater()
    {
        if (Localization.instance != null)
        {
            LoadLocalization(Localization.instance, Localization.instance.GetSelectedLanguage());
        }
    }

    private static void LoadLocalization(Localization localization, string language)
    {
        if (!LocalizationLanguage.Remove(localization))
        {
            LocalizationObjects.Add(new WeakReference<Localization>(localization));
        }

        LocalizationLanguage.Add(localization, language);

        Dictionary<string, string> localizationFiles = FindLocalizationFiles();
        if (LoadTranslationFromAssembly("English") is not { } englishAssemblyData)
        {
            throw new Exception($"Found no English localizations in mod {Plugin.Info.Metadata.Name}. Expected an embedded resource translations/English.json or translations/English.yml.");
        }

        Dictionary<string, string> localizationTexts =
            new DeserializerBuilder().IgnoreFields().Build().Deserialize<Dictionary<string, string>?>(
                Encoding.UTF8.GetString(englishAssemblyData)) ?? new Dictionary<string, string>();
        if (localizationTexts.Count == 0)
        {
            throw new Exception($"Localization for mod {Plugin.Info.Metadata.Name} failed: Localization file was empty.");
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
            foreach (KeyValuePair<string, string> kv in new DeserializerBuilder().IgnoreFields().Build()
                         .Deserialize<Dictionary<string, string>?>(localizationData) ?? new Dictionary<string, string>())
            {
                localizationTexts[kv.Key] = kv.Value;
            }
        }

        LoadedTexts[language] = localizationTexts;
        foreach (string key in localizationTexts.Keys)
        {
            UpdateText(localization, key);
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

    private static void UpdateText(Localization localization, string key)
    {
        LocalizationLanguage.TryGetValue(localization, out string language);
        if (LoadedTexts.TryGetValue(language, out Dictionary<string, string>? texts) &&
            texts.TryGetValue(key, out string text))
        {
            localization.AddWord(key, text);
        }
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

public static class LocalizationManagerVersion
{
    public const string Version = "1.4.0";
}
