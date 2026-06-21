using System;
using System.Reflection;

namespace WarfareTweaks;

internal static class WarfareTweaksBridge
{
    private const string SecondaryAttacksBridgeTypeName = "SecondaryAttacks.WarfareTweaksBridge, SecondaryAttacks";
    private static Type? _secondaryBridgeType;
    private static MethodInfo? _shouldSuppressProjectile;
    private static PropertyInfo? _isGeneratedDamageActive;
    private static bool _resolved;

    internal static bool IsExternalGeneratedDamageActive
    {
        get
        {
            EnsureResolved();
            return _isGeneratedDamageActive?.GetValue(null) is true;
        }
    }

    internal static bool ShouldSuppressProjectile(Projectile projectile)
    {
        EnsureResolved();
        return _shouldSuppressProjectile?.Invoke(null, new object[] { projectile }) is true;
    }

    private static void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        _secondaryBridgeType = Type.GetType(SecondaryAttacksBridgeTypeName, throwOnError: false);
        if (_secondaryBridgeType == null)
        {
            return;
        }

        _shouldSuppressProjectile = _secondaryBridgeType.GetMethod(
            "ShouldSuppressProjectile",
            BindingFlags.Public | BindingFlags.Static);
        _isGeneratedDamageActive = _secondaryBridgeType.GetProperty(
            "IsGeneratedDamageActive",
            BindingFlags.Public | BindingFlags.Static);
    }
}
