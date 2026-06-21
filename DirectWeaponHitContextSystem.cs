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
        !WeaponEffectManager.IsApplyingGeneratedEffectDamage;

    internal static Scope BeginAttackHit(Attack attack)
    {
        if (attack?.m_character != Player.m_localPlayer)
        {
            return default;
        }

        _weaponPrefabName = GetWeaponPrefabName(attack.m_weapon);
        _directHitDepth++;
        return new Scope(ScopeKind.DirectHit);
    }

    internal static Scope BeginProjectileHit(Projectile projectile)
    {
        if (projectile == null ||
            ProjectileAccess.GetOwner(projectile) != Player.m_localPlayer ||
            WarfareTweaksBridge.ShouldSuppressProjectile(projectile))
        {
            return default;
        }

        ItemDrop.ItemData? weapon = ProjectileAccess.GetWeapon(projectile);
        _weaponPrefabName = GetWeaponPrefabName(weapon);
        _directHitDepth++;
        return new Scope(ScopeKind.DirectHit);
    }

    internal static Scope BeginCharacterDamage()
    {
        _characterDamageDepth++;
        return new Scope(ScopeKind.CharacterDamage);
    }

    internal static bool TryGetCurrentProjectileWeaponPrefabName(out string prefabName)
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
                if (_directHitDepth == 0)
                {
                    _weaponPrefabName = "";
                }

                break;
            case ScopeKind.CharacterDamage when _characterDamageDepth > 0:
                _characterDamageDepth--;
                break;
        }
    }

    internal readonly struct Scope
    {
        internal Scope(ScopeKind kind)
        {
            Kind = kind;
        }

        internal ScopeKind Kind { get; }
    }

    internal enum ScopeKind
    {
        None,
        DirectHit,
        CharacterDamage
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
