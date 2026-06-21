using System.Collections.Generic;

namespace WarfareTweaks;

internal sealed class EffectBehaviorConfig
{
    public string Type { get; set; } = "";

    public int? Value { get; set; }

    public string Prefab { get; set; } = "";

    public string Trigger { get; set; } = "anyHit";

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

    public string DamageType { get; set; } = "";

    public string Modifier { get; set; } = "normal";

    public ScalarValueConfig Damage { get; set; } = new();

    public ScalarValueConfig Heal { get; set; } = new();

    public ScalarValueConfig StaminaRestore { get; set; } = new();

    public float MoveSpeedMultiplier { get; set; } = 1f;

    public float HealthThresholdPercent { get; set; } = 25f;

    public float DamageMultiplier { get; set; } = 1f;

    public bool ConsumeOnModify { get; set; } = false;

    public Dictionary<string, EffectBehaviorOverrideConfig>? Prefabs { get; set; }
}

internal sealed class EffectBehaviorOverrideConfig
{
    public string? Type { get; set; }

    public int? Value { get; set; }

    public string? Prefab { get; set; }

    public string? Trigger { get; set; }

    public int? StacksRequired { get; set; }

    public float? StackWindow { get; set; }

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? LightningDamage { get; set; }

    public float? Radius { get; set; }

    public float? Ttl { get; set; }

    public float? HitInterval { get; set; }

    public float? ProcChance { get; set; }

    public string? DamageType { get; set; }

    public string? Modifier { get; set; }

    public ScalarValueOverrideConfig? Damage { get; set; }

    public ScalarValueOverrideConfig? Heal { get; set; }

    public ScalarValueOverrideConfig? StaminaRestore { get; set; }

    public float? MoveSpeedMultiplier { get; set; }

    public float? HealthThresholdPercent { get; set; }

    public float? DamageMultiplier { get; set; }

    public bool? ConsumeOnModify { get; set; }
}

internal sealed class ScalarValueConfig
{
    public string Mode { get; set; } = "fixed";

    public float Value { get; set; }
}

internal sealed class ScalarValueOverrideConfig
{
    public string? Mode { get; set; }

    public float? Value { get; set; }
}
