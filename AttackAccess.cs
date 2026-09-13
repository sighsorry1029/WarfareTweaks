using System.Reflection;
using HarmonyLib;

namespace WarfareTweaks;

internal static class AttackAccess
{
    private static readonly AccessTools.FieldRef<Attack, Humanoid>? CharacterRef =
        CreateFieldRef<Attack, Humanoid>("m_character");
    private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData>? AmmoItemRef =
        CreateFieldRef<Attack, ItemDrop.ItemData>("m_ammoItem");
    private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData>? LastUsedAmmoRef =
        CreateFieldRef<Attack, ItemDrop.ItemData>("m_lastUsedAmmo");
    private static readonly AccessTools.FieldRef<Humanoid, Attack>? CurrentAttackRef =
        CreateFieldRef<Humanoid, Attack>("m_currentAttack");

    private static readonly FieldInfo? CharacterField = AccessTools.Field(typeof(Attack), "m_character");
    private static readonly FieldInfo? AmmoItemField = AccessTools.Field(typeof(Attack), "m_ammoItem");
    private static readonly FieldInfo? LastUsedAmmoField = AccessTools.Field(typeof(Attack), "m_lastUsedAmmo");
    private static readonly FieldInfo? CurrentAttackField = AccessTools.Field(typeof(Humanoid), "m_currentAttack");

    internal static Humanoid? GetCharacter(Attack attack)
    {
        return CharacterRef != null ? CharacterRef(attack) : CharacterField?.GetValue(attack) as Humanoid;
    }

    internal static Attack? GetCurrentAttack(Humanoid humanoid)
    {
        return CurrentAttackRef != null ? CurrentAttackRef(humanoid) : CurrentAttackField?.GetValue(humanoid) as Attack;
    }

    internal static void ClearAmmoState(Attack attack)
    {
        if (AmmoItemRef != null)
        {
            AmmoItemRef(attack) = null!;
        }
        else
        {
            AmmoItemField?.SetValue(attack, null);
        }

        if (LastUsedAmmoRef != null)
        {
            LastUsedAmmoRef(attack) = null!;
        }
        else
        {
            LastUsedAmmoField?.SetValue(attack, null);
        }
    }

    private static AccessTools.FieldRef<TInstance, TField>? CreateFieldRef<TInstance, TField>(string fieldName)
    {
        try
        {
            return AccessTools.FieldRefAccess<TInstance, TField>(fieldName);
        }
        catch
        {
            return null;
        }
    }
}
