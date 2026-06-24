using System;
using System.Globalization;
using LocalizationManager;

namespace WarfareTweaks;

internal static class WarfareTweaksLocalization
{
    internal const string EffectAdrenaline = "$wt_effect_adrenaline";
    internal const string EffectBash = "$wt_effect_bash";
    internal const string EffectBleeding = "$wt_effect_bleeding";
    internal const string EffectBleedingSecondary = "$wt_effect_bleeding_secondary";
    internal const string EffectBurningSecondary = "$wt_effect_burning_secondary";
    internal const string EffectDecapitator = "$wt_effect_decapitator";
    internal const string EffectExecutioner = "$wt_effect_executioner";
    internal const string EffectHackAndSlash = "$wt_effect_hack_and_slash";
    internal const string EffectHaste = "$wt_effect_haste";
    internal const string EffectImpale = "$wt_effect_impale";
    internal const string EffectPiercer = "$wt_effect_piercer";
    internal const string EffectLightningBurst = "$wt_effect_lightning_burst";
    internal const string EffectPinning = "$wt_effect_pinning";
    internal const string EffectPiercingGreatbow = "$wt_effect_piercing_greatbow";
    internal const string EffectSmasher = "$wt_effect_smasher";
    internal const string EffectSmashAndBash = "$wt_effect_smash_and_bash";
    internal const string EffectBludgeoner = "$wt_effect_bludgeoner";
    internal const string EffectVampirism = "$wt_effect_vampirism";
    internal const string EffectFallback = "$wt_effect_fallback";

    internal const string DamageBlunt = "$wt_damage_blunt";
    internal const string DamageFire = "$wt_damage_fire";
    internal const string DamageFrost = "$wt_damage_frost";
    internal const string DamageLightning = "$wt_damage_lightning";
    internal const string DamagePierce = "$wt_damage_pierce";
    internal const string DamageSlash = "$wt_damage_slash";

    internal const string TooltipAdrenalineValue = "$wt_tooltip_adrenaline_value";
    internal const string TooltipAdrenaline = "$wt_tooltip_adrenaline";
    internal const string TooltipHaste = "$wt_tooltip_haste";
    internal const string TooltipVampirismValue = "$wt_tooltip_vampirism_value";
    internal const string TooltipVampirism = "$wt_tooltip_vampirism";
    internal const string TooltipDotValue = "$wt_tooltip_dot_value";
    internal const string TooltipDot = "$wt_tooltip_dot";
    internal const string TooltipBashValue = "$wt_tooltip_bash_value";
    internal const string TooltipBash = "$wt_tooltip_bash";
    internal const string TooltipExecutionerValue = "$wt_tooltip_executioner_value";
    internal const string TooltipExecutioner = "$wt_tooltip_executioner";
    internal const string TooltipResistanceWeakness = "$wt_tooltip_resistance_weakness";
    internal const string TooltipFixedExtraDamageValue = "$wt_tooltip_fixed_extra_damage_value";
    internal const string TooltipFixedExtraDamage = "$wt_tooltip_fixed_extra_damage";
    internal const string TooltipPinningValue = "$wt_tooltip_pinning_value";
    internal const string TooltipPinning = "$wt_tooltip_pinning";
    internal const string TooltipFallbackValue = "$wt_tooltip_fallback_value";
    internal const string TooltipFallback = "$wt_tooltip_fallback";

    internal static void Load()
    {
        Localizer.Load();
    }

    internal static string Localize(string token, string fallback)
    {
        if (Localization.instance == null)
        {
            return fallback;
        }

        string localized = Localization.instance.Localize(token);
        return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, token, StringComparison.Ordinal)
            ? fallback
            : localized;
    }

    internal static string Format(string token, string fallback, params object[] args)
    {
        string format = Localize(token, fallback);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
    }
}
