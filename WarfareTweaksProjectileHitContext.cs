namespace WarfareTweaks;

internal static class WarfareTweaksProjectileHitContext
{
    [System.ThreadStatic]
    private static Projectile? _currentProjectile;

    internal static Scope Begin(Projectile projectile)
    {
        if (projectile == null)
        {
            return default;
        }

        Projectile? previous = _currentProjectile;
        _currentProjectile = projectile;
        return new Scope(previous);
    }

    internal static void End(Scope scope)
    {
        _currentProjectile = scope.Previous;
    }

    internal static bool TryPeek(out ProjectileHitContext context)
    {
        Projectile? projectile = _currentProjectile;
        context = projectile != null ? new ProjectileHitContext(projectile) : default;
        return projectile != null;
    }

    internal readonly struct Scope
    {
        internal Scope(Projectile? previous)
        {
            Previous = previous;
        }

        internal Projectile? Previous { get; }
    }
}

internal readonly struct ProjectileHitContext
{
    public ProjectileHitContext(Projectile projectile)
    {
        Projectile = projectile;
    }

    public Projectile? Projectile { get; }
}

internal static class WarfareTweaksRuntimeContext
{
    internal static bool TryPeekProjectileHitContext(out ProjectileHitContext context)
    {
        return WarfareTweaksProjectileHitContext.TryPeek(out context);
    }
}

internal static class WarfareTweaksRuntimeFacade
{
    internal static bool TryGetProjectileHitAttackContext(
        out string weaponPrefabName,
        out bool secondaryAttack,
        out object? definition,
        out bool disableCurrentAttackFallback)
    {
        weaponPrefabName = "";
        secondaryAttack = false;
        definition = null;
        disableCurrentAttackFallback = false;
        return DirectWeaponHitContextSystem.TryGetCurrentProjectileWeaponPrefabName(out weaponPrefabName);
    }
}

internal static class WeaponEffectManager
{
    internal static bool IsApplyingGeneratedEffectDamage => WarfareTweaksBridge.IsExternalGeneratedDamageActive;

    internal static bool ShouldSuppressWarfareBuiltIn(string effectId)
    {
        return DirectWeaponHitContextSystem.TryGetCurrentProjectileWeaponPrefabName(out string prefabName) &&
               WarfareCompat.ShouldSuppressBuiltIn(prefabName, effectId);
    }
}
