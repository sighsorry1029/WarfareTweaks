# Regression checks

This independent console harness adds no test framework or production abstractions. Three files replace repeated manual parser, tooltip, and scope checks. It is not included in the mod solution or release packages.

With the .NET 10 SDK, first build the current mod with deployment disabled, then run:

```powershell
dotnet build .\Tests\WarfareTweaks.RegressionTests.csproj -c Debug
dotnet .\Tests\bin\Debug\net10.0\WarfareTweaks.RegressionTests.dll
```

The default runtime target is `bin/Debug/WarfareTweaks.dll`. To check another current build, pass its absolute DLL path as the first argument. The DLL's directory must also contain its build-time dependencies, including the publicized Valheim assemblies. A missing method or dependency fails the run; stale DLLs are not silently skipped. No game is started, configured, or deployed by this harness.

The mod targets `net48` but compiles against Unity's newer Mono libraries. Desktop .NET Framework 4.8 cannot execute several resulting methods (`String.Replace` with comparison and newer interface bodies). This harness uses .NET 10 to execute those managed methods without rewriting the mod or mocking Unity. Passing here does not prove compatibility with the game's Mono/native runtime.

## What executes

- The actual config loader, config models, warning helper and default YAML are source-linked. Only the plugin logging/path boundary is replaced with an in-memory logger and paths that throw on access. This tests parsing, rejection without partial results, scalar policy differences, legacy-field handling and culture-independent numeric input. It does **not** test Plugin application, local file watching, ServerSync authority, or preservation of live world state.
- The built mod DLL is loaded by reflection, and its actual managed methods run for prefab normalization, Haste precedence, tooltip matching, nested scope cleanup and the bounded broken-inventory-item guard. The guard uses real managed Valheim Inventory/ItemData types. SharedData/Attack fixture constructors are bypassed because they create native AnimationCurves; only the fields read by the managed guard are initialized. No Unity GameObject/scene is fabricated, and there is no Unity mock framework.
- Scope tests exercise the underlying Begin/End methods, including repeated End and out-of-order cleanup. The direct-hit case also manually invokes the actual Postfix/Finalizer methods and checks exception identity. They do **not** run Harmony's dispatcher or prove native patch ordering. Chain scope tests use null AoE arguments; damage, target selection, and the HashSet guard at actual collision time need game tests.
- A metadata check resolves the exact `Humanoid.DrainEquipedItemDurability(ItemData, float)` method in the loaded Valheim assembly. This confirms target availability, not successful Harmony patch installation or invocation.

The executable exits nonzero on a failure. Tests are method-level regression evidence, not a substitute for the integration checks below.

## Required game checks (manual; not run by the harness)

- Test solo, host plus a remote client, and a dedicated server plus two clients. With locked configuration, edit client YAML and confirm server values remain active; edit server YAML and confirm all clients converge; disconnect and confirm the last valid local YAML becomes active. Invalid/empty edits must preserve the last valid configuration; `{}` must intentionally clear assignments. Repeat reconnect and return-to-menu/world creation.
- Test Warfare alone, WarfareFireAndIce present/absent, Jewelcrafting present/absent, and the supported SecondaryAttacks/CaptainValheim bridges. Inspect actual Harmony target signatures, priority/order, hook counts and cleanup after an injected exception, including nested direct and generated damage.
- Throw a stack of one and a legacy multi-stack item until it breaks. Confirm automatic break cleanup retains the intended item; then immediately drop, transfer, sell, consume, and unequip that broken item. Count the total items across both inventories and the world before/after each operation: it must neither increase nor decrease unexpectedly. Test unrelated items and nested removals while the guard is active. Verify one durability charge per throw, no projectile pickup copy, metadata retention, and normal unrelated ammunition consumption.
- Open craft/upgrade lists with full and partially damaged throwables, repeated tabs, localization changes, configuration reload, and copied ItemData. Check recipe visibility, cost/amount/quality, duplicate recipe rows, and no cumulative mutation across ObjectDB copies.
- Hit creatures with GreatbowModer and verify valid Pinned effects, missing-VFX safety, full movement stop, and Haste interaction. Check tooltips for each effect with CR/LF and rich text, including repeated tooltip calls and localization.
- Test one chain against multiple colliders of one creature, parent/child chains, simultaneous independent activations, ownership transfer, and disconnect during a chain. Check actual damage counts on all peers; local instance-ID dedup does not prove network authority.
- Profile representative combat and inventory UI before/after. Inspect per-frame allocations, AoE name/component searches, tooltip normalization frequency, and recipe/UI regeneration. Check Unity initialization/destruction and watcher/event cleanup for repeated world loads.
