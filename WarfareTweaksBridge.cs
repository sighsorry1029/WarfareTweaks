using System;
using System.Reflection;

namespace WarfareTweaks;

internal static class WarfareTweaksBridge
{
    private const string SecondaryAttacksBridgeTypeName = "SecondaryAttacks.WarfareTweaksBridge, SecondaryAttacks";
    private const string CaptainValheimBridgeTypeName = "CaptainValheim.WarfareTweaksBridge, CaptainValheim";
    private static ShouldSuppressProjectileDelegate? _shouldSuppressProjectile;
    private static IsGeneratedDamageActiveDelegate? _isGeneratedDamageActive;
    private static TryGetWeaponPrefabNameDelegate? _tryGetCaptainValheimShieldHitWeaponPrefabName;
    private static bool _resolved;
    private delegate bool ShouldSuppressProjectileDelegate(Projectile projectile);
    private delegate bool IsGeneratedDamageActiveDelegate();
    private delegate bool TryGetWeaponPrefabNameDelegate(out string weaponPrefabName);

    internal static bool IsExternalGeneratedDamageActive
    {
        get
        {
            EnsureResolved();
            return _isGeneratedDamageActive?.Invoke() is true;
        }
    }

    internal static bool ShouldSuppressProjectile(Projectile projectile)
    {
        EnsureResolved();
        return _shouldSuppressProjectile?.Invoke(projectile) is true;
    }

    internal static bool TryGetCaptainValheimShieldHitWeaponPrefabName(out string weaponPrefabName)
    {
        weaponPrefabName = "";
        EnsureResolved();
        if (_tryGetCaptainValheimShieldHitWeaponPrefabName == null)
        {
            return false;
        }

        if (!_tryGetCaptainValheimShieldHitWeaponPrefabName(out string contextWeaponPrefabName) ||
            string.IsNullOrWhiteSpace(contextWeaponPrefabName))
        {
            return false;
        }

        weaponPrefabName = contextWeaponPrefabName;
        return true;
    }

    private static void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        Type? secondaryBridgeType = Type.GetType(SecondaryAttacksBridgeTypeName, throwOnError: false);
        Type? captainValheimBridgeType = Type.GetType(CaptainValheimBridgeTypeName, throwOnError: false);

        if (secondaryBridgeType != null)
        {
            _shouldSuppressProjectile = CreateStaticDelegate<ShouldSuppressProjectileDelegate>(
                secondaryBridgeType.GetMethod(
                    "ShouldSuppressProjectile",
                    BindingFlags.Public | BindingFlags.Static));
            _isGeneratedDamageActive = CreateStaticDelegate<IsGeneratedDamageActiveDelegate>(
                secondaryBridgeType.GetProperty(
                    "IsGeneratedDamageActive",
                    BindingFlags.Public | BindingFlags.Static)?.GetGetMethod());
        }

        if (captainValheimBridgeType != null)
        {
            _tryGetCaptainValheimShieldHitWeaponPrefabName = CreateStaticDelegate<TryGetWeaponPrefabNameDelegate>(
                captainValheimBridgeType.GetMethod(
                    "TryGetShieldHitWeaponPrefabName",
                    BindingFlags.Public | BindingFlags.Static));
        }
    }

    private static TDelegate? CreateStaticDelegate<TDelegate>(MethodInfo? method)
        where TDelegate : class
    {
        if (method == null)
        {
            return null;
        }

        try
        {
            return Delegate.CreateDelegate(typeof(TDelegate), method, throwOnBindFailure: false) as TDelegate;
        }
        catch
        {
            return null;
        }
    }
}
