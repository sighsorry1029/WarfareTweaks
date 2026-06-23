# WarfareTweaks

WarfareTweaks is a standalone compatibility and tuning module for Therzie's Warfare ecosystem. It lets servers assign and tune Warfare or WarfareFireAndIce native weapon effects from YAML while adding guardrails for effect duplication, generated projectile hits, and chain-lightning edge cases.

## Highlights

- Assigns Warfare-native effects to Warfare, WarfareFireAndIce, vanilla, or other modded weapon prefabs.
- Keeps the original Warfare effect style while making the mapping editable in one synced YAML file.
- Supports fixed-hit-count effects such as Adrenaline, Haste, Vampirism, Bleeding, Bash, Executioner, Decapitator, Smasher, Juggernaut, Lightning Burst, and Chain Lightning.
- Restores and protects Chain Lightning behavior so one activation does not repeatedly damage the same large-collider target.
- Avoids counting SecondaryAttacks or CaptainValheim generated projectile/follow-up damage as ordinary direct Warfare weapon-effect hits when their bridge data is available.
- Includes soft compatibility hooks for Warfare, WarfareFireAndIce, Jewelcrafting throwables, and ItemManager sync behavior.
- Syncs config through ServerSync and reloads the YAML when the local file changes.

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

## Installation

Install BepInExPack Valheim, install the Warfare-family mods you want to tune, then place `WarfareTweaks.dll` in a BepInEx plugin folder. Launch once to generate `WarfareTweaks.yml`, edit effect assignments, and restart or reload config as needed.
