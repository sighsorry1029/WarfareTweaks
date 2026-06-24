# WarfareTweaks
Tunes Therzie Warfare and WarfareFireAndIce weapon effects through server-synced YAML, assigning native effects to any prefab. Adds durability-based throwing axes, health-scaled damage fixes, and safer Chain Lightning.

![](https://i.ibb.co/N6ZLy8Y7/Screenshot-2026-06-24-184847.png) <br>
![](https://i.ibb.co/kgmDQSVG/Screenshot-2026-06-24-185713.png) <br>
Made some warfare effects have tooltips too.

![](https://i.ibb.co/SbnSmMk/Screenshot-2026-06-24-183426.png) <br>
Warfare effects can be assigned to other weqpons.

![](https://i.ibb.co/gLxtDJCw/Screenshot-2026-06-24-183644.png) <br>
Throwing axes is treated like normal weapons with durability. So it can be enchnated with EpicLoot and Jewelcrafting.

## Highlights

- Assigns Warfare-native effects to Warfare, WarfareFireAndIce, vanilla, or other modded weapon prefabs.
- Supports fixed-hit-count effects such as Adrenaline, Haste, Vampirism, Bleeding, Bash, Executioner, Decapitator, Smasher, Juggernaut, Lightning Burst, and Chain Lightning.
- Restores and protects Chain Lightning behavior so one activation does not repeatedly damage the same large-collider target.
- Fixes health-scaled damage handling so percent-health effects use safer, consistent damage values.
- Adds durability-based throwing axe handling for Warfare throw axes.
- Includes ItemManager sync fixes for four Warfare weapons that need updated item data handling.
- Avoids counting SecondaryAttacks or CaptainValheim generated projectile/follow-up damage as ordinary direct Warfare weapon-effect hits when their bridge data is available.
- Includes soft compatibility hooks for Warfare, WarfareFireAndIce, Jewelcrafting throwables, and ItemManager sync behavior.

## Configuration

WarfareTweaks creates:

- `BepInEx/config/WarfareTweaks.yml`

Each effect block owns a `prefabs:` map. Add any weapon prefab name there to assign that effect to the weapon. Numeric values are interpreted by the effect block's comment, for example proc chance, damage, duration, multiplier, or percent restored.

Example:

```yml
adrenaline:
  prefabs:
    KnifeViper_TW: 2.857

ChainLightningMistlands_TW:
  lightningDamage: 32.0
  radius: 8.0
  ttl: 3.0
  hitInterval: 0.0
  prefabs:
    GreatbowDvergr_TW: 35
```

Warfare or WarfareFireAndIce must be installed for their native effect classes and prefabs to exist at runtime.

## Compatibility

- SecondaryAttacks can expose generated-hit context to WarfareTweaks through reflection.
- CaptainValheim can expose shield-hit context to WarfareTweaks through reflection.
- Jewelcrafting throwable compatibility is installed only when the matching plugin is present.
- Warfare and WarfareFireAndIce are soft dependencies, so the mod can load safely while waiting for the relevant plugin to be installed.
