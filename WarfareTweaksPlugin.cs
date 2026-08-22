using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace WarfareTweaks;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInDependency(WarfareTweaksCompat.WarfareGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(WarfareTweaksCompat.WarfareFireAndIceGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(WarfareTweaksCompat.JewelcraftingGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class WarfareTweaksPlugin : BaseUnityPlugin
{
    internal const string ModName = "WarfareTweaks";
    internal const string ModVersion = "1.0.2";
    internal const string Author = "sighsorry";
    internal const string ModGUID = $"{Author}.{ModName}";
    internal const string WarfareYamlFileName = "WarfareTweaks.yml";

    internal static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync =
        new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    internal static string ConfigDirectoryPath => Paths.ConfigPath;
    internal static string WarfareYamlFilePath => Path.Combine(ConfigDirectoryPath, WarfareYamlFileName);
    private static Dictionary<string, EffectBehaviorConfig> _currentEffects = new(StringComparer.OrdinalIgnoreCase);
    private static CustomSyncedValue<string>? _syncedWarfareYaml;
    private static bool _suppressSyncedYamlChanged;

    private readonly Harmony _harmony = new(ModGUID);
    private FileSystemWatcher? _watcher;
    private readonly object _reloadLock = new();
    private DateTime _lastConfigReloadTime;
    private const long ReloadDelayTicks = 10000000;

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public void Awake()
    {
        ConfigSync.AddLockingConfigEntry(Config.Bind(
            "1 - General",
            "Lock Configuration",
            Toggle.On,
            "If on, the server configuration is enforced for clients."));
        _syncedWarfareYaml = new CustomSyncedValue<string>(ConfigSync, "warfare_tweaks_warfare_yaml", "");
        _syncedWarfareYaml.ValueChanged += OnSyncedWarfareYamlChanged;

        WarfareTweaksLocalization.Load(this, _harmony);
        if (!ReloadLocalConfigFromDisk(applyToWorld: false))
        {
            ApplyEmbeddedDefaultConfig();
        }

        Assembly assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        TryInstallCompatibilityHooks("Warfare effects", () => WarfareCompat.TryInstallHooks(_harmony));
        TryInstallCompatibilityHooks("Warfare skills", () => WarfareSkillCompat.TryInstallHooks(_harmony));
        TryInstallCompatibilityHooks("Jewelcrafting throwables", () => JewelcraftingThrowableCompat.TryInstallHooks(_harmony));
        SetupWatcher();
    }

    private void OnDestroy()
    {
        if (_syncedWarfareYaml != null)
        {
            _syncedWarfareYaml.ValueChanged -= OnSyncedWarfareYamlChanged;
        }

        _watcher?.Dispose();
        _harmony.UnpatchSelf();
        WarfareCompat.ResetHookState();
        WarfareSkillCompat.ResetHookState();
        JewelcraftingThrowableCompat.ResetHookState();
    }

    internal static void ApplyToObjectDb(ObjectDB objectDb, bool logMissingPrefabWarnings = false)
    {
        if (objectDb == null)
        {
            return;
        }

        WarfareCompat.ApplyConfiguredEffects(objectDb, _currentEffects, logMissingPrefabWarnings);
        WarfareGreatbowSoundCompat.ApplyToObjectDb(
            objectDb,
            ZNetScene.instance,
            logMissingPrefabWarnings);
        WarfareThrowableCompat.ApplyToObjectDb(objectDb);
        WarfareSkillCompat.ApplyToObjectDb(objectDb);
    }

    internal static void ApplyToZNetScene(ZNetScene scene)
    {
        if (scene == null)
        {
            return;
        }

        ChainLightningDedupSystem.RestoreVanillaChainLightningBehavior(scene);

        if (ObjectDB.instance != null)
        {
            WarfareCompat.ApplyConfiguredEffects(ObjectDB.instance, _currentEffects, logMissingPrefabWarnings: true);
            WarfareGreatbowSoundCompat.ApplyToObjectDb(
                ObjectDB.instance,
                scene,
                logMissingSfxWarning: true);
        }

        WarfareThrowableCompat.ApplyToZNetScene(scene);
    }

    private void SetupWatcher()
    {
        _watcher = new FileSystemWatcher(ConfigDirectoryPath, WarfareYamlFileName);
        _watcher.Changed += ReadConfigValues;
        _watcher.Created += ReadConfigValues;
        _watcher.Renamed += ReadConfigValues;
        _watcher.IncludeSubdirectories = false;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        DateTime now = DateTime.Now;
        if (now.Ticks - _lastConfigReloadTime.Ticks < ReloadDelayTicks)
        {
            return;
        }

        bool reloaded;
        lock (_reloadLock)
        {
            reloaded = ReloadLocalConfigFromDisk(applyToWorld: true);
        }

        if (reloaded)
        {
            _lastConfigReloadTime = DateTime.Now;
        }
    }

    private static bool ReloadLocalConfigFromDisk(bool applyToWorld)
    {
        string yamlText;
        try
        {
            WarfareTweaksConfigLoader.EnsureLocalFileExists();
            yamlText = File.ReadAllText(WarfareYamlFilePath);
        }
        catch (Exception exception)
        {
            ModLogger.LogError(
                $"Failed to read {WarfareYamlFileName}; the last valid configuration remains active: {exception.Message}");
            return false;
        }

        if (!TryApplyYamlText(yamlText, applyToWorld))
        {
            return false;
        }

        PublishSyncedYaml(yamlText);
        return true;
    }

    private static void PublishSyncedYaml(string yamlText)
    {
        if (_syncedWarfareYaml != null)
        {
            _suppressSyncedYamlChanged = true;
            try
            {
                _syncedWarfareYaml.AssignLocalValue(yamlText);
            }
            finally
            {
                _suppressSyncedYamlChanged = false;
            }
        }
    }

    private static void OnSyncedWarfareYamlChanged()
    {
        if (_suppressSyncedYamlChanged || _syncedWarfareYaml == null || string.IsNullOrWhiteSpace(_syncedWarfareYaml.Value))
        {
            return;
        }

        TryApplyYamlText(_syncedWarfareYaml.Value, applyToWorld: true);
    }

    private static bool TryApplyYamlText(string yamlText, bool applyToWorld)
    {
        if (!WarfareTweaksConfigLoader.TryParse(yamlText, out Dictionary<string, EffectBehaviorConfig> parsedEffects))
        {
            return false;
        }

        _currentEffects = parsedEffects;
        WarfareCompat.RebuildBuiltInEffects(_currentEffects);
        if (!applyToWorld)
        {
            return true;
        }

        if (ObjectDB.instance != null)
        {
            WarfareCompat.ApplyConfiguredEffects(
                ObjectDB.instance,
                _currentEffects,
                logMissingPrefabWarnings: ZNetScene.instance != null);
        }

        ModLogger.LogInfo("WarfareTweaks YAML reload complete.");
        return true;
    }

    private static void ApplyEmbeddedDefaultConfig()
    {
        string defaultYaml = WarfareTweaksConfigLoader.LoadEmbeddedDefault(WarfareYamlFileName);
        if (!TryApplyYamlText(defaultYaml, applyToWorld: false))
        {
            throw new InvalidDataException($"Embedded default {WarfareYamlFileName} is invalid.");
        }

        PublishSyncedYaml(defaultYaml);
        ModLogger.LogWarning(
            $"Using embedded default {WarfareYamlFileName} because the local file could not be loaded.");
    }

    private static void TryInstallCompatibilityHooks(string featureName, Action installer)
    {
        try
        {
            installer();
        }
        catch (Exception exception)
        {
            WarfareTweaksWarningLog.LogOnce(
                $"compat_hook_install_failed_{featureName}",
                $"Could not finish installing {featureName} compatibility hooks; " +
                $"successfully installed hooks, if any, remain active: {exception.Message}");
        }
    }
}
