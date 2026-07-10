using HarmonyLib;

namespace WarfareTweaks;

internal static class DirectWeaponHitContextSystem
{
    private static int _directHitDepth;
    private static int _characterDamageDepth;
    private static string _weaponPrefabName = "";

    internal static bool IsDirectWeaponHitActive => _directHitDepth > 0;

    internal static bool ShouldCountWeaponEffectHit =>
        _directHitDepth > 0 &&
        _characterDamageDepth == 1 &&
        !WarfareTweaksBridge.IsExternalGeneratedDamageActive;

    internal static Scope BeginAttackHit(Attack attack)
    {
        if (attack?.m_character != Player.m_localPlayer)
        {
            return default;
        }

        string previousWeaponPrefabName = _weaponPrefabName;
        _weaponPrefabName = GetWeaponPrefabName(attack.m_weapon);
        _directHitDepth++;
        return new Scope(ScopeKind.DirectHit, previousWeaponPrefabName);
    }

    internal static Scope BeginProjectileHit(Projectile projectile)
    {
        if (projectile == null ||
            ProjectileAccess.GetOwner(projectile) != Player.m_localPlayer ||
            WarfareTweaksBridge.ShouldSuppressProjectile(projectile))
        {
            return default;
        }

        string previousWeaponPrefabName = _weaponPrefabName;
        ItemDrop.ItemData? weapon = ProjectileAccess.GetWeapon(projectile);
        _weaponPrefabName = GetWeaponPrefabName(weapon);
        _directHitDepth++;
        return new Scope(ScopeKind.DirectHit, previousWeaponPrefabName);
    }

    internal static Scope BeginCharacterDamage()
    {
        _characterDamageDepth++;
        if (_directHitDepth == 0 &&
            WarfareTweaksBridge.TryGetCaptainValheimShieldHitWeaponPrefabName(out string weaponPrefabName))
        {
            string previousWeaponPrefabName = _weaponPrefabName;
            _weaponPrefabName = weaponPrefabName;
            _directHitDepth++;
            return new Scope(ScopeKind.CharacterDamageWithExternalDirectHit, previousWeaponPrefabName);
        }

        return new Scope(ScopeKind.CharacterDamage);
    }

    internal static bool TryGetCurrentWeaponPrefabName(out string prefabName)
    {
        prefabName = _weaponPrefabName;
        return _directHitDepth > 0 && !string.IsNullOrWhiteSpace(prefabName);
    }

    private static string GetWeaponPrefabName(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
    }

    internal static void End(Scope scope)
    {
        switch (scope.Kind)
        {
            case ScopeKind.DirectHit when _directHitDepth > 0:
                _directHitDepth--;
                _weaponPrefabName = scope.PreviousWeaponPrefabName;

                break;
            case ScopeKind.CharacterDamage when _characterDamageDepth > 0:
                _characterDamageDepth--;
                break;
            case ScopeKind.CharacterDamageWithExternalDirectHit:
                if (_characterDamageDepth > 0)
                {
                    _characterDamageDepth--;
                }

                if (_directHitDepth > 0)
                {
                    _directHitDepth--;
                    _weaponPrefabName = scope.PreviousWeaponPrefabName;
                }

                break;
        }
    }

    internal readonly struct Scope
    {
        internal Scope(ScopeKind kind, string previousWeaponPrefabName = "")
        {
            Kind = kind;
            PreviousWeaponPrefabName = previousWeaponPrefabName;
        }

        internal ScopeKind Kind { get; }

        internal string PreviousWeaponPrefabName { get; }
    }

    internal enum ScopeKind
    {
        None,
        DirectHit,
        CharacterDamage,
        CharacterDamageWithExternalDirectHit
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
internal static class AttackDoMeleeAttackDirectWeaponHitPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Attack __instance, out DirectWeaponHitContextSystem.Scope __state)
    {
        __state = DirectWeaponHitContextSystem.BeginAttackHit(__instance);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(__state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
internal static class AttackDoAreaAttackDirectWeaponHitPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Attack __instance, out DirectWeaponHitContextSystem.Scope __state)
    {
        __state = DirectWeaponHitContextSystem.BeginAttackHit(__instance);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(__state);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class CharacterDamageDirectWeaponHitDepthPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(out DirectWeaponHitContextSystem.Scope __state)
    {
        __state = DirectWeaponHitContextSystem.BeginCharacterDamage();
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(__state);
    }
}
