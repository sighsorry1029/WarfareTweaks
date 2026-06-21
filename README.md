# WarfareTweaks

Standalone Warfare and WarfareFireAndIce compatibility/tuning module by sighsorry.

## Config

The mod creates and syncs:

- `BepInEx/config/WarfareTweaks/WarfareTweaks.Warfare.yml`

This YAML is synchronized with ServerSync and assigns/tunes Warfare-native effects for Warfare, WarfareFireAndIce, vanilla, or other modded weapon prefabs. Warfare must be installed for Warfare effects to run; WarfareFireAndIce is needed for its native effect classes.

## SecondaryAttacks Bridge

WarfareTweaks can run by itself. If SecondaryAttacks is also installed, WarfareTweaks reads the optional `SecondaryAttacks.WarfareTweaksBridge` reflection bridge to avoid counting SecondaryAttacks-generated projectile or follow-up damage as direct Warfare weapon-effect hits.
