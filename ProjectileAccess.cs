using System.Reflection;
using HarmonyLib;

namespace WarfareTweaks;

internal static class ProjectileAccess
{
    private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData>? WeaponRef =
        CreateFieldRef<ItemDrop.ItemData>("m_weapon");
    private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData>? AmmoRef =
        CreateFieldRef<ItemDrop.ItemData>("m_ammo");
    private static readonly AccessTools.FieldRef<Projectile, Character>? OwnerRef =
        CreateFieldRef<Character>("m_owner");
    private static readonly AccessTools.FieldRef<Projectile, HitData>? OriginalHitDataRef =
        CreateFieldRef<HitData>("m_originalHitData");
    private static readonly AccessTools.FieldRef<Projectile, int>? StatusEffectHashRef =
        CreateFieldRef<int>("m_statusEffectHash");
    private static readonly AccessTools.FieldRef<Projectile, UnityEngine.Vector3>? VelocityRef =
        CreateFieldRef<UnityEngine.Vector3>("m_vel");
    private static readonly AccessTools.FieldRef<Projectile, bool>? DidHitRef =
        CreateFieldRef<bool>("m_didHit");
    private static readonly FieldInfo? WeaponField = AccessTools.Field(typeof(Projectile), "m_weapon");
    private static readonly FieldInfo? AmmoField = AccessTools.Field(typeof(Projectile), "m_ammo");
    private static readonly FieldInfo? OwnerField = AccessTools.Field(typeof(Projectile), "m_owner");
    private static readonly FieldInfo? OriginalHitDataField = AccessTools.Field(typeof(Projectile), "m_originalHitData");
    private static readonly FieldInfo? StatusEffectHashField = AccessTools.Field(typeof(Projectile), "m_statusEffectHash");
    private static readonly FieldInfo? VelocityField = AccessTools.Field(typeof(Projectile), "m_vel");
    private static readonly FieldInfo? DidHitField = AccessTools.Field(typeof(Projectile), "m_didHit");

    internal static ItemDrop.ItemData? GetWeapon(Projectile projectile)
    {
        return WeaponRef != null ? WeaponRef(projectile) : WeaponField?.GetValue(projectile) as ItemDrop.ItemData;
    }

    internal static ItemDrop.ItemData? GetAmmo(Projectile projectile)
    {
        return AmmoRef != null ? AmmoRef(projectile) : AmmoField?.GetValue(projectile) as ItemDrop.ItemData;
    }

    internal static Character? GetOwner(Projectile projectile)
    {
        return OwnerRef != null ? OwnerRef(projectile) : OwnerField?.GetValue(projectile) as Character;
    }

    internal static HitData? GetOriginalHitData(Projectile projectile)
    {
        return OriginalHitDataRef != null
            ? OriginalHitDataRef(projectile)
            : OriginalHitDataField?.GetValue(projectile) as HitData;
    }

    internal static int GetStatusEffectHash(Projectile projectile)
    {
        return StatusEffectHashRef != null
            ? StatusEffectHashRef(projectile)
            : StatusEffectHashField?.GetValue(projectile) is int hash ? hash : 0;
    }

    internal static UnityEngine.Vector3 GetVelocity(Projectile projectile)
    {
        return VelocityRef != null
            ? VelocityRef(projectile)
            : VelocityField?.GetValue(projectile) is UnityEngine.Vector3 velocity
                ? velocity
                : UnityEngine.Vector3.zero;
    }

    internal static void SetVelocity(Projectile projectile, UnityEngine.Vector3 velocity)
    {
        if (VelocityRef != null)
        {
            VelocityRef(projectile) = velocity;
            return;
        }

        VelocityField?.SetValue(projectile, velocity);
    }

    internal static void SetDidHit(Projectile projectile, bool didHit)
    {
        if (DidHitRef != null)
        {
            DidHitRef(projectile) = didHit;
            return;
        }

        DidHitField?.SetValue(projectile, didHit);
    }

    private static AccessTools.FieldRef<Projectile, T>? CreateFieldRef<T>(string fieldName)
    {
        try
        {
            return AccessTools.FieldRefAccess<Projectile, T>(fieldName);
        }
        catch
        {
            return null;
        }
    }
}
