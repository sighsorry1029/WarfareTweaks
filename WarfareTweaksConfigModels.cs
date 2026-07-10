using System.Collections.Generic;

namespace WarfareTweaks;

internal sealed class EffectBehaviorConfig
{
    public string Type { get; set; } = "";

    public int? Value { get; set; }

    public string Prefab { get; set; } = "";

    public int? StacksRequired { get; set; }

    public float StackWindow { get; set; } = 0f;

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? LightningDamage { get; set; }

    public float? Radius { get; set; }

    public float? Ttl { get; set; }

    public float? HitInterval { get; set; }

    public float ProcChance { get; set; } = 100f;

    public ScalarValueConfig StaminaRestore { get; set; } = new();

    public float MoveSpeedMultiplier { get; set; } = 1f;

    public Dictionary<string, EffectBehaviorOverrideConfig>? Prefabs { get; set; }
}

internal sealed class EffectBehaviorOverrideConfig
{
    public int? Value { get; set; }

    public int? StacksRequired { get; set; }

    public float? StackWindow { get; set; }

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? ProcChance { get; set; }

    public ScalarValueOverrideConfig? StaminaRestore { get; set; }

    public float? MoveSpeedMultiplier { get; set; }

}

internal sealed class ScalarValueConfig
{
    public float Value { get; set; }
}

internal sealed class ScalarValueOverrideConfig
{
    public float? Value { get; set; }
}
