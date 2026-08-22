namespace WarfareTweaks;

internal static class WarfareTweaksProjectileHitContext
{
    [System.ThreadStatic]
    private static Projectile? _currentProjectile;

    [System.ThreadStatic]
    private static int _scopeDepth;

    internal static Scope Begin(Projectile projectile)
    {
        if (projectile == null)
        {
            return default;
        }

        Projectile? previous = _currentProjectile;
        _currentProjectile = projectile;
        _scopeDepth++;
        return new Scope(previous, _scopeDepth);
    }

    internal static void End(Scope scope)
    {
        if (scope.Depth == 0 || _scopeDepth != scope.Depth)
        {
            return;
        }

        _scopeDepth--;
        _currentProjectile = scope.Previous;
    }

    internal static bool TryPeek(out Projectile? projectile)
    {
        projectile = _currentProjectile;
        return projectile != null;
    }

    internal readonly struct Scope
    {
        internal Scope(Projectile? previous, int depth)
        {
            Previous = previous;
            Depth = depth;
        }

        internal Projectile? Previous { get; }

        internal int Depth { get; }
    }
}
