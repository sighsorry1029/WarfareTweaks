namespace WarfareTweaks;

internal static class WarfareTweaksProjectileHitContext
{
    [System.ThreadStatic]
    private static ProjectileHitContext? _current;

    internal static Scope Begin(Projectile projectile)
    {
        if (projectile == null)
        {
            return default;
        }

        ProjectileHitContext previous = _current!;
        _current = new ProjectileHitContext(projectile);
        return new Scope(previous);
    }

    internal static void End(Scope scope)
    {
        _current = scope.Previous;
    }

    internal static bool TryPeek(out ProjectileHitContext? context)
    {
        context = _current;
        return context != null;
    }

    internal readonly struct Scope
    {
        internal Scope(ProjectileHitContext? previous)
        {
            Previous = previous;
        }

        internal ProjectileHitContext? Previous { get; }
    }
}

internal sealed class ProjectileHitContext
{
    public ProjectileHitContext(Projectile projectile)
    {
        Projectile = projectile;
    }

    public Projectile Projectile { get; }
}

internal static class WarfareTweaksRuntimeContext
{
    internal static bool TryPeekProjectileHitContext(out ProjectileHitContext? context)
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

internal static class LaunchSlamSystem
{
    internal static bool IsApplyingLandingDamage => false;
}

internal static class KnockbackChainSystem
{
    internal static bool IsApplyingChainDamage => false;
}

internal static class MeleeProjectileHitCascadeSystem
{
    internal static bool IsApplyingImpactBurstDamage => false;
}
