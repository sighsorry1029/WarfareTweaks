using System.Reflection;
using HarmonyLib;

namespace WarfareTweaks;

internal static class ProjectileAccess
{
    private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData>? WeaponRef =
        CreateFieldRef<ItemDrop.ItemData>("m_weapon");
    private static readonly AccessTools.FieldRef<Projectile, Character>? OwnerRef =
        CreateFieldRef<Character>("m_owner");
    private static readonly AccessTools.FieldRef<Projectile, HitData>? OriginalHitDataRef =
        CreateFieldRef<HitData>("m_originalHitData");
    private static readonly FieldInfo? WeaponField = AccessTools.Field(typeof(Projectile), "m_weapon");
    private static readonly FieldInfo? OwnerField = AccessTools.Field(typeof(Projectile), "m_owner");
    private static readonly FieldInfo? OriginalHitDataField = AccessTools.Field(typeof(Projectile), "m_originalHitData");

    internal static ItemDrop.ItemData? GetWeapon(Projectile projectile)
    {
        return WeaponRef != null ? WeaponRef(projectile) : WeaponField?.GetValue(projectile) as ItemDrop.ItemData;
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
