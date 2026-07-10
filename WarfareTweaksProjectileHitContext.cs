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
