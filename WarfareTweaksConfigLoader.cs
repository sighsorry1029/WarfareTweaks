using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WarfareTweaks;

internal static class WarfareTweaksConfigLoader
{
    private static readonly HashSet<string> IgnoredLegacyEffectFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "trigger",
        "damageType",
        "modifier",
        "damage",
        "heal",
        "healthThresholdPercent",
        "damageMultiplier",
        "consumeOnModify"
    };

    private static readonly HashSet<string> IgnoredLegacyPrefabOverrideFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "type",
        "prefab",
        "trigger",
        "lightningDamage",
        "radius",
        "ttl",
        "hitInterval",
        "damageType",
        "modifier",
        "damage",
        "heal",
        "healthThresholdPercent",
        "damageMultiplier",
        "consumeOnModify"
    };

    private static readonly HashSet<string> IgnoredLegacyScalarValueFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "mode"
    };

    private static readonly Dictionary<string, string> PrefabScalarFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["adrenaline"] = "staminaRestore.value",
        ["bash"] = "value",
        ["bleeding"] = "damageFactor",
        ["bleedingSecondary"] = "damageFactor",
        ["bleeding_secondary"] = "damageFactor",
        ["burningSecondary"] = "damageFactor",
        ["burning_secondary"] = "damageFactor",
        ["executioner"] = "value",
        ["haste"] = "moveSpeedMultiplier",
        ["impale"] = "damageFactor",
        ["lightningBurst"] = "value",
        ["pinning"] = "value",
        ["pierceGreatbow"] = "value",
        ["pierceGreatbowFireAndIce"] = "value",
        ["piercingGreatbowFireAndIce"] = "value",
        ["piercingGreatbowMistlands"] = "value",
        ["piercingGreatbowModer"] = "value",
        ["piercingGreatbowPlains"] = "value",
        ["vampirism"] = "value"
    };

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    internal static void EnsureLocalFileExists()
    {
        Directory.CreateDirectory(WarfareTweaksPlugin.ConfigDirectoryPath);
        if (!File.Exists(WarfareTweaksPlugin.WarfareYamlFilePath))
        {
            File.WriteAllText(
                WarfareTweaksPlugin.WarfareYamlFilePath,
                WarfareTweaksDefaultYamlResources.Load(WarfareTweaksPlugin.WarfareYamlFileName));
        }
    }

    internal static bool TryParse(
        string yamlText,
        out Dictionary<string, EffectBehaviorConfig> parsed)
    {
        parsed = new Dictionary<string, EffectBehaviorConfig>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            WarfareTweaksPlugin.ModLogger.LogWarning(
                $"Ignoring empty {WarfareTweaksPlugin.WarfareYamlFileName}; the last valid configuration remains active. Use '{{}}' to explicitly clear all effect assignments.");
            return false;
        }

        try
        {
            YamlStream stream = new();
            stream.Load(new StringReader(yamlText));
            if (stream.Documents.Count == 0 ||
                stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                throw new InvalidDataException("The YAML root must be a mapping.");
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in root.Children)
            {
                string rootKey = (entry.Key as YamlScalarNode)?.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(rootKey))
                {
                    continue;
                }

                try
                {
                    parsed[rootKey] = DeserializeEffectBehaviorConfig(rootKey, entry.Value);
                }
                catch (Exception entryException)
                {
                    throw new InvalidDataException(
                        $"Block '{rootKey}' is invalid: {entryException.Message}",
                        entryException);
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            WarfareTweaksPlugin.ModLogger.LogError(
                $"Failed to parse {WarfareTweaksPlugin.WarfareYamlFileName}; the last valid configuration remains active: {exception.Message}");
            parsed.Clear();
            return false;
        }
    }

    private static EffectBehaviorConfig DeserializeEffectBehaviorConfig(string rootKey, YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
        {
            return DeserializeYamlNode<EffectBehaviorConfig>(node) ?? new EffectBehaviorConfig();
        }

        NormalizePrefabScalarOverrides(rootKey, mapping);
        RemoveIgnoredLegacyFields(rootKey, mapping);
        return DeserializeYamlNode<EffectBehaviorConfig>(mapping) ?? new EffectBehaviorConfig();
    }

    private static void RemoveIgnoredLegacyFields(string rootKey, YamlMappingNode mapping)
    {
        RemoveIgnoredMappingFields(
            mapping,
            IgnoredLegacyEffectFields,
            $"block '{rootKey}'");
        RemoveIgnoredScalarValueMode(mapping, "staminaRestore", $"block '{rootKey}'");

        if (!TryGetMappingChild(mapping, "prefabs", out YamlMappingNode? prefabs) || prefabs == null)
        {
            return;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> entry in prefabs.Children)
        {
            if (entry.Value is not YamlMappingNode prefabOverride)
            {
                continue;
            }

            string prefabName = (entry.Key as YamlScalarNode)?.Value?.Trim() ?? "";
            string location = $"prefab '{prefabName}' in block '{rootKey}'";
            RemoveIgnoredMappingFields(
                prefabOverride,
                IgnoredLegacyPrefabOverrideFields,
                location);
            RemoveIgnoredScalarValueMode(prefabOverride, "staminaRestore", location);
        }
    }

    private static void RemoveIgnoredScalarValueMode(
        YamlMappingNode owner,
        string fieldName,
        string location)
    {
        if (TryGetMappingChild(owner, fieldName, out YamlMappingNode? scalarValue) && scalarValue != null)
        {
            RemoveIgnoredMappingFields(
                scalarValue,
                IgnoredLegacyScalarValueFields,
                $"field '{fieldName}' in {location}");
        }
    }

    private static void RemoveIgnoredMappingFields(
        YamlMappingNode mapping,
        HashSet<string> ignoredLegacyFields,
        string location)
    {
        foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children.ToList())
        {
            string fieldName = (entry.Key as YamlScalarNode)?.Value?.Trim() ?? "";
            if (!ignoredLegacyFields.Contains(fieldName))
            {
                continue;
            }

            mapping.Children.Remove(entry.Key);
            if (WarfareTweaksWarningLog.TryMarkReported($"ignored_yaml_field_{fieldName}"))
            {
                WarfareTweaksPlugin.ModLogger.LogWarning(
                    $"Ignoring unsupported {WarfareTweaksPlugin.WarfareYamlFileName} field '{fieldName}' in {location}; it has no WarfareTweaks behavior.");
            }
        }
    }

    private static void NormalizePrefabScalarOverrides(string rootKey, YamlMappingNode mapping)
    {
        string? scalarField = ResolvePrefabScalarField(rootKey, mapping);
        if (string.IsNullOrWhiteSpace(scalarField) ||
            !TryGetMappingChild(mapping, "prefabs", out YamlMappingNode? prefabs) ||
            prefabs == null)
        {
            return;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> entry in prefabs.Children.ToList())
        {
            if (entry.Value is not YamlScalarNode scalar)
            {
                continue;
            }

            string value = scalar.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                prefabs.Children[entry.Key] = new YamlMappingNode();
                continue;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidDataException(
                    $"Prefab '{entry.Key}' must use a numeric {scalarField}, got '{value}'.");
            }

            if (string.Equals(scalarField, "value", StringComparison.OrdinalIgnoreCase) &&
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidDataException(
                    $"Prefab '{entry.Key}' must use an integer value, got '{value}'.");
            }

            prefabs.Children[entry.Key] = BuildScalarOverrideNode(scalarField!, value);
        }
    }

    private static YamlMappingNode BuildScalarOverrideNode(string scalarField, string value)
    {
        string[] parts = scalarField
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        if (parts.Length == 0)
        {
            return new YamlMappingNode();
        }

        YamlMappingNode root = new();
        YamlMappingNode current = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            YamlMappingNode child = new();
            current.Children[new YamlScalarNode(parts[i])] = child;
            current = child;
        }

        current.Children[new YamlScalarNode(parts[^1])] = new YamlScalarNode(value);
        return root;
    }

    private static string? ResolvePrefabScalarField(string rootKey, YamlMappingNode mapping)
    {
        string effectType = TryGetScalarChild(mapping, "type", out string? configuredType) && !string.IsNullOrWhiteSpace(configuredType)
            ? configuredType!
            : rootKey;
        string trimmedEffectType = effectType.Trim();
        if (trimmedEffectType.IndexOf("ChainLightning", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(trimmedEffectType, "chainLightning", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedEffectType, "chainLightningMistlands", StringComparison.OrdinalIgnoreCase))
        {
            return "procChance";
        }

        return PrefabScalarFields.TryGetValue(trimmedEffectType, out string? scalarField)
            ? scalarField
            : null;
    }

    private static bool TryGetMappingChild(YamlMappingNode mapping, string key, out YamlMappingNode? child)
    {
        child = null;
        foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value?.Trim(), key, StringComparison.OrdinalIgnoreCase) &&
                entry.Value is YamlMappingNode childMapping)
            {
                child = childMapping;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetScalarChild(YamlMappingNode mapping, string key, out string? value)
    {
        value = null;
        foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value?.Trim(), key, StringComparison.OrdinalIgnoreCase) &&
                entry.Value is YamlScalarNode valueNode)
            {
                value = valueNode.Value;
                return true;
            }
        }

        return false;
    }

    private static T? DeserializeYamlNode<T>(YamlNode node)
    {
        using StringWriter writer = new();
        YamlStream stream = new(new YamlDocument(node));
        stream.Save(writer, assignAnchors: false);
        return Deserializer.Deserialize<T>(writer.ToString());
    }
}
