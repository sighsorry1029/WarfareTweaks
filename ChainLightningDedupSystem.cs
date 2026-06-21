using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WarfareTweaks;

internal static class ChainLightningDedupSystem
{
    private const float VanillaChainChancePerTarget = 0.3f;
    private const float DuplicateHitWindow = 0.35f;
    private static readonly FieldInfo? AoeOwnerField = AccessTools.Field(typeof(Aoe), "m_owner");
    private static readonly Dictionary<ChainLightningHitKey, float> RecentHits = new();
    private static bool _loggedChainRestore;

    internal static void RestoreVanillaChainChance(ZNetScene scene)
    {
        GameObject? prefab = scene != null ? scene.GetPrefab("ChainLightning") : null;
        Aoe? aoe = prefab != null ? prefab.GetComponent<Aoe>() ?? prefab.GetComponentInChildren<Aoe>(true) : null;
        if (aoe == null || aoe.m_chainChancePerTarget > 0f)
        {
            return;
        }

        aoe.m_chainChancePerTarget = VanillaChainChancePerTarget;
        if (!_loggedChainRestore)
        {
            _loggedChainRestore = true;
            WarfareTweaksPlugin.ModLogger.LogInfo("Restored vanilla ChainLightning chain target chance after WarfareFireAndIce patch.");
        }
    }

    internal static bool ShouldAllowChainLightningHit(Aoe aoe, Collider collider)
    {
        if (aoe == null || collider == null || !IsChainLightningAoe(aoe))
        {
            return true;
        }

        GameObject hitObject = Projectile.FindHitObject(collider);
        Character? target = hitObject != null ? hitObject.GetComponent<Character>() : null;
        if (target == null)
        {
            return true;
        }

        Character? owner = AoeOwnerField?.GetValue(aoe) as Character;
        int ownerId = owner != null ? owner.GetInstanceID() : 0;
        int targetId = target.GetInstanceID();
        int sourceId = ResolveSourceId(aoe);
        ChainLightningHitKey key = new(ownerId, targetId, sourceId);
        float now = Time.time;

        CleanupExpired(now);
        if (RecentHits.TryGetValue(key, out float expiresAt) && expiresAt > now)
        {
            return false;
        }

        RecentHits[key] = now + DuplicateHitWindow;
        return true;
    }

    private static bool IsChainLightningAoe(Aoe aoe)
    {
        Transform transform = aoe.transform;
        return ContainsChainLightningName(aoe.name) ||
               ContainsChainLightningName(transform.root != null ? transform.root.name : "") ||
               ContainsChainLightningName(transform.parent != null ? transform.parent.name : "");
    }

    private static bool ContainsChainLightningName(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf("ChainLightning", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int ResolveSourceId(Aoe aoe)
    {
        string name = aoe.name;
        Transform transform = aoe.transform;
        if (!ContainsChainLightningName(name) && transform.root != null)
        {
            name = transform.root.name;
        }

        return name.Replace("(Clone)", "", StringComparison.OrdinalIgnoreCase).Trim().GetStableHashCode();
    }

    private static void CleanupExpired(float now)
    {
        if (RecentHits.Count < 128)
        {
            return;
        }

        List<ChainLightningHitKey>? expired = null;
        foreach ((ChainLightningHitKey key, float expiresAt) in RecentHits)
        {
            if (expiresAt > now)
            {
                continue;
            }

            expired ??= new List<ChainLightningHitKey>();
            expired.Add(key);
        }

        if (expired == null)
        {
            return;
        }

        foreach (ChainLightningHitKey key in expired)
        {
            RecentHits.Remove(key);
        }
    }

    private readonly struct ChainLightningHitKey : IEquatable<ChainLightningHitKey>
    {
        private readonly int _ownerId;
        private readonly int _targetId;
        private readonly int _sourceId;

        public ChainLightningHitKey(int ownerId, int targetId, int sourceId)
        {
            _ownerId = ownerId;
            _targetId = targetId;
            _sourceId = sourceId;
        }

        public bool Equals(ChainLightningHitKey other)
        {
            return _ownerId == other._ownerId &&
                   _targetId == other._targetId &&
                   _sourceId == other._sourceId;
        }

        public override bool Equals(object? obj)
        {
            return obj is ChainLightningHitKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _ownerId;
                hash = (hash * 397) ^ _targetId;
                hash = (hash * 397) ^ _sourceId;
                return hash;
            }
        }
    }
}

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
[HarmonyAfter(WarfareTweaksCompat.WarfareFireAndIceGuid)]
internal static class ZNetSceneAwakeChainLightningRestorePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ZNetScene __instance)
    {
        ChainLightningDedupSystem.RestoreVanillaChainChance(__instance);
    }
}

[HarmonyPatch(typeof(Aoe), "OnHit", new[] { typeof(Collider), typeof(Vector3) })]
internal static class AoeOnHitChainLightningDedupPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Aoe __instance, Collider collider, ref bool __result)
    {
        if (ChainLightningDedupSystem.ShouldAllowChainLightningHit(__instance, collider))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
