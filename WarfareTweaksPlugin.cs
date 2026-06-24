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
    internal const string ModVersion = "1.0.0";
    internal const string Author = "sighsorry";
    internal const string ModGUID = $"{Author}.{ModName}";
    internal const string WarfareYamlFileName = "WarfareTweaks.yml";

    internal static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync =
        new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    internal static string ConfigDirectoryPath => Paths.ConfigPath;
    internal static string WarfareYamlFilePath => Path.Combine(ConfigDirectoryPath, WarfareYamlFileName);
    internal static IReadOnlyDictionary<string, EffectBehaviorConfig> CurrentEffects => _currentEffects;

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

        WarfareTweaksLocalization.Load();
        WarfareTweaksConfigLoader.EnsureLocalFileExists();
        ReloadLocalConfigFromDisk(applyToWorld: false);

        Assembly assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        WarfareCompat.TryInstallHooks();
        WarfareSkillCompat.TryInstallHooks();
        JewelcraftingThrowableCompat.TryInstallHooks();
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
    }

    internal static void ApplyToObjectDb(ObjectDB objectDb, bool logMissingPrefabWarnings = false)
    {
        if (objectDb == null)
        {
            return;
        }

        WarfareCompat.ApplyConfiguredEffects(objectDb, _currentEffects, logMissingPrefabWarnings);
        WarfareThrowableCompat.ApplyToObjectDb(objectDb);
        WarfareSkillCompat.ApplyToObjectDb(objectDb);
    }

    internal static void ApplyToZNetScene(ZNetScene scene)
    {
        if (scene == null)
        {
            return;
        }

        if (ObjectDB.instance != null)
        {
            WarfareCompat.ApplyConfiguredEffects(ObjectDB.instance, _currentEffects, logMissingPrefabWarnings: true);
        }

        WarfareThrowableCompat.ApplyToZNetScene(scene);
        ChainLightningDedupSystem.RestoreVanillaChainLightningBehavior(scene);
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

        lock (_reloadLock)
        {
            ReloadLocalConfigFromDisk(applyToWorld: true);
        }

        _lastConfigReloadTime = now;
    }

    private static void ReloadLocalConfigFromDisk(bool applyToWorld)
    {
        WarfareTweaksConfigLoader.EnsureLocalFileExists();
        string yamlText = File.ReadAllText(WarfareYamlFilePath);
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

        ApplyYamlText(yamlText, applyToWorld);
    }

    private static void OnSyncedWarfareYamlChanged()
    {
        if (_suppressSyncedYamlChanged || _syncedWarfareYaml == null || string.IsNullOrWhiteSpace(_syncedWarfareYaml.Value))
        {
            return;
        }

        ApplyYamlText(_syncedWarfareYaml.Value, applyToWorld: true);
    }

    private static void ApplyYamlText(string yamlText, bool applyToWorld)
    {
        _currentEffects = WarfareTweaksConfigLoader.Parse(yamlText);
        WarfareCompat.RebuildBuiltInEffects(_currentEffects);
        if (!applyToWorld)
        {
            return;
        }

        if (ObjectDB.instance != null)
        {
            ApplyToObjectDb(ObjectDB.instance, logMissingPrefabWarnings: ZNetScene.instance != null);
        }

        if (ZNetScene.instance != null)
        {
            ApplyToZNetScene(ZNetScene.instance);
        }

        ModLogger.LogInfo("WarfareTweaks YAML reload complete.");
    }
}
