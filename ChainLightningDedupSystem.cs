using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WarfareTweaks;

internal static class ChainLightningDedupSystem
{
    private const float VanillaLightningDamage = 75f;
    private const float VanillaChainChancePerTarget = 0.3f;
    private static readonly ConditionalWeakTable<Aoe, ChainLightningAoeState> AoeStates = new();
    private static bool _loggedVanillaRestore;
    private static int _nextActivationId;

    [ThreadStatic]
    private static ChainLightningActivation? ActiveActivation;

    internal static void RestoreVanillaChainLightningBehavior(ZNetScene scene)
    {
        GameObject? prefab = scene != null ? scene.GetPrefab("ChainLightning") : null;
        Aoe? aoe = prefab != null ? prefab.GetComponent<Aoe>() ?? prefab.GetComponentInChildren<Aoe>(true) : null;
        if (aoe == null)
        {
            return;
        }

        bool changed = !Mathf.Approximately(aoe.m_damage.m_lightning, VanillaLightningDamage) ||
                       !Mathf.Approximately(aoe.m_chainChancePerTarget, VanillaChainChancePerTarget);
        aoe.m_damage.m_lightning = VanillaLightningDamage;
        aoe.m_chainChancePerTarget = VanillaChainChancePerTarget;
        if (changed && !_loggedVanillaRestore)
        {
            _loggedVanillaRestore = true;
            WarfareTweaksPlugin.ModLogger.LogInfo("Restored vanilla ChainLightning damage and chain target chance after WarfareFireAndIce patch.");
        }
    }

    internal static void TrackSetup(Aoe aoe, Character owner, ItemDrop.ItemData item)
    {
        if (aoe == null)
        {
            return;
        }

        if (!TryGetChainLightningState(aoe, out ChainLightningAoeState state))
        {
            return;
        }

        ChainLightningActivation activation = ActiveActivation ?? new ChainLightningActivation(++_nextActivationId, Time.time, Time.frameCount);
        state.Activation = activation;
        activation.SetupCount++;
    }

    internal static ChainUpdateScope BeginChainUpdate(Aoe aoe)
    {
        ChainLightningActivation? previous = ActiveActivation;
        if (aoe != null)
        {
            if (TryGetChainLightningState(aoe, out ChainLightningAoeState state))
            {
                ActiveActivation = EnsureActivation(state);
            }
        }

        return new ChainUpdateScope(previous);
    }

    internal static void EndChainUpdate(ChainUpdateScope scope)
    {
        ActiveActivation = scope.PreviousActivation;
    }

    internal static bool ShouldAllowChainLightningHit(Aoe aoe, Collider collider)
    {
        if (aoe == null || collider == null)
        {
            return true;
        }

        if (!TryGetChainLightningState(aoe, out ChainLightningAoeState state))
        {
            return true;
        }

        Character? target = TryGetHitCharacter(collider);
        if (target == null)
        {
            return true;
        }

        int targetId = target.GetInstanceID();
        ChainLightningActivation activation = EnsureActivation(state);
        if (activation.HitTargetIds.Contains(targetId))
        {
            return false;
        }

        activation.HitTargetIds.Add(targetId);
        return true;
    }

    internal static void FilterChainLightningCandidate(Aoe aoe, Collider collider, ref bool result)
    {
        if (!result || aoe == null || collider == null)
        {
            return;
        }

        if (!TryGetChainLightningState(aoe, out ChainLightningAoeState state))
        {
            return;
        }

        ChainLightningActivation? activation = state.Activation ?? ActiveActivation;
        if (activation == null)
        {
            return;
        }

        Character? target = TryGetHitCharacter(collider);
        if (target == null)
        {
            return;
        }

        if (!activation.HitTargetIds.Contains(target.GetInstanceID()))
        {
            return;
        }

        result = false;
    }

    private static ChainLightningActivation EnsureActivation(ChainLightningAoeState state)
    {
        return state.Activation ??= ActiveActivation ?? new ChainLightningActivation(++_nextActivationId, Time.time, Time.frameCount);
    }

    private static Character? TryGetHitCharacter(Collider collider)
    {
        GameObject hitObject = Projectile.FindHitObject(collider);
        return hitObject != null ? hitObject.GetComponent<Character>() : null;
    }

    private static bool TryGetChainLightningState(Aoe aoe, out ChainLightningAoeState state)
    {
        state = ChainLightningAoeState.NotChainLightning;
        if (!CouldBeChainLightningAoe(aoe))
        {
            return false;
        }

        state = AoeStates.GetValue(aoe, CreateAoeState);
        return state.IsChainLightning;
    }

    private static ChainLightningAoeState CreateAoeState(Aoe aoe)
    {
        return TryResolveChainLightningSourceName(aoe, out string sourceName)
            ? new ChainLightningAoeState(true, NormalizeSourceName(sourceName).GetStableHashCode())
            : ChainLightningAoeState.NotChainLightning;
    }

    private static bool TryResolveChainLightningSourceName(Aoe aoe, out string sourceName)
    {
        string aoeName = aoe.name;
        Transform transform = aoe.transform;
        bool aoeNameMatches = ContainsChainLightningName(aoeName);
        bool rootNameMatches = transform.root != null && ContainsChainLightningName(transform.root.name);
        bool parentNameMatches = transform.parent != null && ContainsChainLightningName(transform.parent.name);
        if (!aoeNameMatches && !rootNameMatches && !parentNameMatches)
        {
            sourceName = "";
            return false;
        }

        sourceName = !aoeNameMatches && transform.root != null ? transform.root.name : aoeName;
        return true;
    }

    private static bool CouldBeChainLightningAoe(Aoe aoe)
    {
        if (aoe == null)
        {
            return false;
        }

        if (ContainsChainLightningName(aoe.name))
        {
            return true;
        }

        if (aoe.m_chainStartChance <= 0f &&
            aoe.m_chainChancePerTarget <= 0f &&
            aoe.m_chainObj == null)
        {
            return false;
        }

        if (ContainsChainLightningName(aoe.m_chainObj != null ? aoe.m_chainObj.name : ""))
        {
            return true;
        }

        Transform transform = aoe.transform;
        return transform.root != null && ContainsChainLightningName(transform.root.name) ||
               transform.parent != null && ContainsChainLightningName(transform.parent.name);
    }

    private static bool ContainsChainLightningName(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf("ChainLightning", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeSourceName(string name)
    {
        return name.Replace("(Clone)", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    internal readonly struct ChainUpdateScope
    {
        internal ChainUpdateScope(ChainLightningActivation? previousActivation)
        {
            PreviousActivation = previousActivation;
        }

        internal ChainLightningActivation? PreviousActivation { get; }
    }

    internal sealed class ChainLightningActivation
    {
        public ChainLightningActivation(int id, float createdAt, int createdFrame)
        {
            Id = id;
            CreatedAt = createdAt;
            CreatedFrame = createdFrame;
        }

        public int Id { get; }
        public float CreatedAt { get; }
        public int CreatedFrame { get; }
        public int SetupCount { get; set; }
        public HashSet<int> HitTargetIds { get; } = new();
    }

    private sealed class ChainLightningAoeState
    {
        internal static readonly ChainLightningAoeState NotChainLightning = new(false, 0);

        public ChainLightningAoeState(bool isChainLightning, int sourceId)
        {
            IsChainLightning = isChainLightning;
            SourceId = sourceId;
        }

        public bool IsChainLightning { get; }
        public int SourceId { get; }
        public ChainLightningActivation? Activation { get; set; }
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

[HarmonyPatch(typeof(Aoe), "ShouldHit", new[] { typeof(Collider) })]
internal static class AoeShouldHitChainLightningCandidatePatch
{
    private static void Postfix(Aoe __instance, Collider collider, ref bool __result)
    {
        ChainLightningDedupSystem.FilterChainLightningCandidate(__instance, collider, ref __result);
    }
}

[HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
internal static class AoeSetupChainLightningActivationPatch
{
    private static void Postfix(Aoe __instance, Character owner, ItemDrop.ItemData item)
    {
        ChainLightningDedupSystem.TrackSetup(__instance, owner, item);
    }
}

[HarmonyPatch(typeof(Aoe), nameof(Aoe.CustomFixedUpdate))]
internal static class AoeCustomFixedUpdateChainLightningActivationPatch
{
    private static void Prefix(Aoe __instance, out ChainLightningDedupSystem.ChainUpdateScope __state)
    {
        __state = ChainLightningDedupSystem.BeginChainUpdate(__instance);
    }

    private static void Postfix(ChainLightningDedupSystem.ChainUpdateScope __state)
    {
        ChainLightningDedupSystem.EndChainUpdate(__state);
    }
}
