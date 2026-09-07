using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace WarfareTweaks.Tests
{
    internal static class Program
    {
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Assembly _runtime = null!;
        private static string _runtimeDirectory = "";
        private static int _passed;
        private static int _failed;

        private static int Main(string[] args)
        {
            Run("parser: explicit empty mapping", () => Equal(0, Parse("{}").Count));
            Run("parser: embedded defaults and Greatbow Pinned assignment", () =>
            {
                var effects = Parse(WarfareTweaksConfigLoader.LoadEmbeddedDefault(WarfareTweaksPlugin.WarfareYamlFileName));
                Equal(4, effects["pinning"].Prefabs!["GreatbowModer_TW"].Value);
            });
            Run("parser: scalar policies remain distinct", () =>
            {
                var effects = Parse("pinning: { prefabs: { Bow: 4 } }\nhaste: { prefabs: { Sword: 1.25 } }\nbleeding: { prefabs: { Knife: 0.5 } }\nadrenaline: { prefabs: { Fist: 12.5 } }\nchainLightning: { prefabs: { Axe: 30 } }");
                Equal(4, effects["PINNING"].Prefabs!["Bow"].Value);
                Equal(1.25f, effects["haste"].Prefabs!["Sword"].MoveSpeedMultiplier);
                Equal(0.5f, effects["bleeding"].Prefabs!["Knife"].DamageFactor);
                Equal(12.5f, effects["adrenaline"].Prefabs!["Fist"].StaminaRestore!.Value);
                Equal(30f, effects["chainLightning"].Prefabs!["Axe"].ProcChance);
            });
            Run("parser: configured type selects scalar policy", () =>
                Equal(1.2f, Parse("custom: { type: haste, prefabs: { Sword: 1.2 } }")["custom"].Prefabs!["Sword"].MoveSpeedMultiplier));
            Run("parser: empty prefab override keeps defaults", () =>
                Equal(null, Parse("pinning: { prefabs: { Bow: } }")["pinning"].Prefabs!["Bow"].Value));
            Run("parser: invalid input yields no partial assignments", () =>
            {
                foreach (string yaml in new[] { "", " ", "[]", "pinning: [", "pinning: { prefabs: { Bow: 1.5 } }", "pinning: { unknownField: 1 }", "pinning: { prefabs: { Bow: 4 } }\nbash: { prefabs: { Club: bad } }" })
                {
                    Check(!WarfareTweaksConfigLoader.TryParse(yaml, out var rejected), "Invalid input was accepted: " + yaml);
                    Equal(0, rejected.Count);
                }
            });
            Run("parser: legacy no-op fields do not remove active values", () =>
            {
                var config = Parse("adrenaline:\n  trigger: old\n  staminaRestore: { value: 10, mode: percent }\n  prefabs:\n    Fist: { staminaRestore: { value: 15, mode: flat }, damage: 100 }")["adrenaline"];
                Equal(10f, config.StaminaRestore.Value);
                Equal(15f, config.Prefabs!["Fist"].StaminaRestore!.Value);
                Check(TestLog.Messages.Exists(message => message.Contains("Ignoring unsupported")), "Expected legacy field warning.");
            });
            Run("parser: invariant numeric syntax", () =>
            {
                CultureInfo previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    Equal(1.25f, Parse("haste: { prefabs: { Sword: 1.25 } }")["haste"].Prefabs!["Sword"].MoveSpeedMultiplier);
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });

            try
            {
                string repository = FindRepository();
                string assemblyPath = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(repository, "bin", "Debug", "WarfareTweaks.dll"));
                if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Build WarfareTweaks Debug first, or pass the current DLL path.", assemblyPath);
                _runtimeDirectory = Path.GetDirectoryName(assemblyPath)!;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveRuntimeDependency;
                _runtime = Assembly.LoadFrom(assemblyPath);
                Console.WriteLine("Runtime methods: " + assemblyPath);
                RunRuntimeTests();
            }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine("FAIL runtime test setup: " + Unwrap(exception));
            }

            Console.WriteLine($"{_passed} passed; {_failed} failed. Unity/Harmony/network integration is not exercised.");
            return _failed == 0 ? 0 : 1;
        }

        private static void RunRuntimeTests()
        {
            Run("runtime: prefab normalization", () =>
            {
                Equal("ThrowAxeIron_TW", Invoke("WarfareTweaksCompat", "NormalizePrefabName", "  ThrowAxeIron_TW(Clone)  "));
                Equal("", Invoke("WarfareTweaksCompat", "NormalizePrefabName", new object?[] { null }));
                Equal("Name(Clone)Suffix", Invoke("WarfareTweaksCompat", "NormalizePrefabName", "Name(Clone)Suffix"));
            });
            Run("runtime: Haste precedence, clamp and default sentinel", () =>
            {
                Type configType = RuntimeType("EffectBehaviorConfig");
                Type overrideType = RuntimeType("EffectBehaviorOverrideConfig");
                object config = Activator.CreateInstance(configType)!;
                object prefab = Activator.CreateInstance(overrideType)!;
                MethodInfo resolver = RuntimeType("WarfareCompat").GetMethod("ResolveConfiguredHasteMoveSpeedMultiplier", StaticMembers, null, new[] { configType, overrideType, typeof(float) }, null)!;
                Equal(1.4f, resolver.Invoke(null, new[] { config, null, (object)1.4f }));
                configType.GetProperty("MoveSpeedMultiplier")!.SetValue(config, 1.2f);
                Equal(1.2f, resolver.Invoke(null, new[] { config, null, (object)1.4f }));
                overrideType.GetProperty("MoveSpeedMultiplier")!.SetValue(prefab, 1f);
                Equal(1f, resolver.Invoke(null, new[] { config, prefab, (object)1.4f }));
                overrideType.GetProperty("MoveSpeedMultiplier")!.SetValue(prefab, -1f);
                Equal(0f, resolver.Invoke(null, new[] { config, prefab, (object)1.4f }));
            });
            Run("runtime: tooltip matching preserves lines and label boundaries", () =>
            {
                var lines = (string[])Invoke("WarfareCompat", "NormalizeTooltipLines", " <color=red>Haste</color>: +40%\r\nPinned\rBleeding damage")!;
                Equal(3, lines.Length);
                Equal("Haste: +40%", lines[0]);
                Equal(true, Invoke("WarfareCompat", "TooltipContainsAny", lines, new[] { "<b>haste</b>" }));
                Equal(true, Invoke("WarfareCompat", "TooltipContainsAny", lines, new[] { "pinned" }));
                Equal(false, Invoke("WarfareCompat", "TooltipContainsAny", lines, new[] { " ", "Hast", "Bleeding" }));
                Equal(false, Invoke("WarfareCompat", "TooltipContainsAny", new[] { "Other: Haste" }, new[] { "Haste" }));
            });
            Run("runtime: unchanged prefab cache entry preserves its string instance", () =>
            {
                Type sharedType = Assembly.Load("assembly_valheim").GetType("ItemDrop+ItemData+SharedData", true)!;
                object shared = RuntimeHelpers.GetUninitializedObject(sharedType);
                string first = new string("ThrowAxeIron_TW".ToCharArray());
                Invoke("WarfareCompat", "CacheItemPrefabName", shared, first);
                object cache = Field("WarfareCompat", "ItemPrefabNamesBySharedData")!;
                MethodInfo getValue = cache.GetType().GetMethod("TryGetValue")!;
                object?[] lookup = { shared, null };
                Equal(true, getValue.Invoke(cache, lookup));
                Check(ReferenceEquals(first, lookup[1]), "Initial cache entry was not retained.");
                Invoke("WarfareCompat", "CacheItemPrefabName", shared, new string(first.ToCharArray()));
                Equal(true, getValue.Invoke(cache, lookup));
                Check(ReferenceEquals(first, lookup[1]), "Equal text replaced the existing cache entry.");
                Invoke("WarfareCompat", "CacheItemPrefabName", shared, "ThrowAxeSilver_TW");
                Equal(true, getValue.Invoke(cache, lookup));
                Equal("ThrowAxeSilver_TW", lookup[1]);
            });
            Run("runtime: direct-hit nested cleanup and duplicate End", () =>
            {
                object outer = Invoke("DirectWeaponHitContextSystem", "BeginCharacterDamage")!;
                object inner = Invoke("DirectWeaponHitContextSystem", "BeginCharacterDamage")!;
                try
                {
                    Equal(2, Field("DirectWeaponHitContextSystem", "_characterDamageDepth"));
                    Invoke("DirectWeaponHitContextSystem", "End", outer);
                    Equal(2, Field("DirectWeaponHitContextSystem", "_characterDamageDepth"));
                    Invoke("CharacterDamageDirectWeaponHitDepthPatch", "Postfix", inner);
                    var failure = new InvalidOperationException("exception identity sentinel");
                    Check(ReferenceEquals(failure, Invoke("CharacterDamageDirectWeaponHitDepthPatch", "Finalizer", failure, inner)), "Finalizer changed the original exception.");
                    Equal(1, Field("DirectWeaponHitContextSystem", "_characterDamageDepth"));
                }
                finally
                {
                    Invoke("DirectWeaponHitContextSystem", "End", inner);
                    Invoke("DirectWeaponHitContextSystem", "End", outer);
                    Invoke("DirectWeaponHitContextSystem", "End", outer);
                }
                Equal(0, Field("DirectWeaponHitContextSystem", "_characterDamageDepth"));
            });
            Run("runtime: chain nested cleanup and duplicate End without scene objects", () =>
            {
                object outer = Invoke("ChainLightningDedupSystem", "BeginChainUpdate", new object?[] { null })!;
                object inner = Invoke("ChainLightningDedupSystem", "BeginChainUpdate", new object?[] { null })!;
                try
                {
                    Invoke("ChainLightningDedupSystem", "EndChainUpdate", outer);
                    Equal(2, Field("ChainLightningDedupSystem", "ActiveScopeDepth"));
                    Invoke("ChainLightningDedupSystem", "EndChainUpdate", inner);
                    Invoke("ChainLightningDedupSystem", "EndChainUpdate", inner);
                    Equal(1, Field("ChainLightningDedupSystem", "ActiveScopeDepth"));
                }
                finally
                {
                    Invoke("ChainLightningDedupSystem", "EndChainUpdate", inner);
                    Invoke("ChainLightningDedupSystem", "EndChainUpdate", outer);
                    Invoke("ChainLightningDedupSystem", "EndChainUpdate", outer);
                }
                Equal(0, Field("ChainLightningDedupSystem", "ActiveScopeDepth"));
            });
            Run("metadata: automatic break-cleanup Harmony target exists with exact signature", () =>
            {
                Assembly valheim = Assembly.Load("assembly_valheim");
                Type itemType = valheim.GetType("ItemDrop+ItemData", true)!;
                MethodInfo? method = valheim.GetType("Humanoid", true)!.GetMethod("DrainEquipedItemDurability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { itemType, typeof(float) }, null);
                Check(method != null, "Humanoid.DrainEquipedItemDurability(ItemData, float) was not found.");
                Equal(typeof(void), method!.ReturnType);
            });
            Run("runtime: broken-removal scope protects only its exact inventory and item", TestBrokenRemovalScope);
        }

        private static void TestBrokenRemovalScope()
        {
            Assembly valheim = Assembly.Load("assembly_valheim");
            Type inventoryType = valheim.GetType("Inventory", true)!;
            object inventory = Activator.CreateInstance(inventoryType, "test", null, 4, 4)!;
            object otherInventory = Activator.CreateInstance(inventoryType, "other", null, 4, 4)!;
            Type itemType = valheim.GetType("ItemDrop+ItemData", true)!;
            object item = MakeThrowable(itemType);
            object otherItem = MakeThrowable(itemType);
            object ordinaryItem = MakeThrowable(itemType);
            object ordinaryShared = itemType.GetField("m_shared")!.GetValue(ordinaryItem)!;
            ordinaryShared.GetType().GetField("m_name")!.SetValue(ordinaryShared, "regression_ordinary_item");
            FieldInfo ordinarySkill = ordinaryShared.GetType().GetField("m_skillType")!;
            ordinarySkill.SetValue(ordinaryShared, Enum.ToObject(ordinarySkill.FieldType, 0));
            object outer = Invoke("WarfareThrowableCompat", "BeginBrokenRemovalPreservation", inventory, item)!;
            object inner = Invoke("WarfareThrowableCompat", "BeginBrokenRemovalPreservation", otherInventory, otherItem)!;
            try
            {
                Equal(true, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", otherInventory, otherItem, false));
                Equal(false, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, otherItem, false));
                Equal(false, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", otherInventory, item, false));
                Invoke("WarfareThrowableCompat", "EndBrokenRemovalPreservation", inner);
                Invoke("WarfareThrowableCompat", "EndBrokenRemovalPreservation", inner);
                Equal(true, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, item, false));
                Equal(false, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, item, true));
                object ordinaryScope = Invoke("WarfareThrowableCompat", "BeginBrokenRemovalPreservation", inventory, ordinaryItem)!;
                try
                {
                    Equal(false, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, ordinaryItem, false));
                    Equal(false, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, item, false));
                }
                finally { Invoke("WarfareThrowableCompat", "EndBrokenRemovalPreservation", ordinaryScope); }
                Equal(true, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, item, false));
            }
            finally
            {
                Invoke("WarfareThrowableCompat", "EndBrokenRemovalPreservation", inner);
                Invoke("WarfareThrowableCompat", "EndBrokenRemovalPreservation", outer);
                Invoke("WarfareThrowableCompat", "EndBrokenRemovalPreservation", outer);
            }
            Equal(false, Invoke("WarfareThrowableCompat", "ShouldBlockInventoryRemoval", inventory, item, false));
            Equal(0f, itemType.GetField("m_durability")!.GetValue(item));
            Equal(1, itemType.GetField("m_stack")!.GetValue(item));
        }

        private static object MakeThrowable(Type itemType)
        {
            object item = Activator.CreateInstance(itemType)!;
            FieldInfo sharedField = itemType.GetField("m_shared")!;
            // SharedData/Attack constructors create native AnimationCurves. Only fields used
            // by the managed recognition/removal path are initialized in these real types.
            object shared = sharedField.GetValue(item) ?? RuntimeHelpers.GetUninitializedObject(sharedField.FieldType);
            sharedField.SetValue(item, shared);
            Type sharedType = shared.GetType();
            sharedType.GetField("m_name")!.SetValue(shared, "regression_throw_axe");
            sharedType.GetField("m_destroyBroken")!.SetValue(shared, true);
            sharedType.GetField("m_skillType")!.SetValue(shared, Field("WarfareThrowableCompat", "ThrowingSkillType"));
            FieldInfo attackField = sharedType.GetField("m_attack")!;
            object attack = attackField.GetValue(shared) ?? RuntimeHelpers.GetUninitializedObject(attackField.FieldType);
            attackField.SetValue(shared, attack);
            FieldInfo attackType = attack.GetType().GetField("m_attackType")!;
            attackType.SetValue(attack, Enum.Parse(attackType.FieldType, "Projectile"));
            itemType.GetField("m_durability")!.SetValue(item, 0f);
            itemType.GetField("m_stack")!.SetValue(item, 1);
            return item;
        }

        private static Assembly? ResolveRuntimeDependency(object? sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name!;
            foreach (string file in new[] { name + ".dll", name + "_publicized.dll" })
            {
                string path = Path.Combine(_runtimeDirectory, file);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }

        private static Type RuntimeType(string name) => _runtime.GetType("WarfareTweaks." + name, true)!;
        private static object? Field(string type, string name) => RuntimeType(type).GetField(name, StaticMembers)!.GetValue(null);
        private static object? Invoke(string type, string name, params object?[] args) => RuntimeType(type).GetMethod(name, StaticMembers)!.Invoke(null, args);
        private static Dictionary<string, EffectBehaviorConfig> Parse(string yaml)
        {
            Check(WarfareTweaksConfigLoader.TryParse(yaml, out var parsed), "Expected valid YAML.");
            return parsed;
        }

        private static string FindRepository()
        {
            for (DirectoryInfo? directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "WarfareTweaks.csproj"))) return directory.FullName;
            throw new DirectoryNotFoundException("Run the harness from its repository build output.");
        }

        private static void Run(string name, Action test)
        {
            try { test(); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception exception) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + Unwrap(exception)); }
        }
        private static Exception Unwrap(Exception exception) => exception is TargetInvocationException invocation && invocation.InnerException != null ? Unwrap(invocation.InnerException) : exception;
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Equal(object? expected, object? actual) => Check(Equals(expected, actual), $"Expected '{expected ?? "<null>"}', got '{actual ?? "<null>"}'.");
    }
}

// Only source-linked parsing uses these substitutes. The runtime DLL keeps its real plugin and logger.
namespace WarfareTweaks
{
    internal static class WarfareTweaksPlugin
    {
        internal const string WarfareYamlFileName = "WarfareTweaks.yml";
        internal static readonly TestLog ModLogger = new();
        internal static string ConfigDirectoryPath => throw new InvalidOperationException("Tests must not write game configuration.");
        internal static string WarfareYamlFilePath => throw new InvalidOperationException("Tests must not write game configuration.");
    }

    internal sealed class TestLog
    {
        internal static readonly List<string> Messages = new();
        public void LogWarning(object message) => Messages.Add(message.ToString() ?? "");
        public void LogError(object message) => Messages.Add(message.ToString() ?? "");
    }
}
