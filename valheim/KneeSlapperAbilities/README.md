# Knee-Slapper’s Abilities and Gear 1.4.0

For Valheim **1.0.7**, character name exactly **Knee-Slapper**.

- Wooden arrows freeze enemy creatures for two seconds; repeated hits refresh the timer.
- Wooden arrows are never consumed. Better Archery recovery does not create extra copies.
- One-time gear delivery: crude bow; quality-4 Frostfire bow, Frostfire axe, and Black Metal pickaxe; quality-3 hammer, hoe, cultivator, and scythe; fishing rod; wooden arrows and bait.
- Unlimited stamina, minimum 10,000 carry capacity, and unlimited durability for the supplied bows/tools. Fishing bait is consumed normally.
- One additional airborne jump, recharged by landing.
- No fall damage; ordinary damage still applies.

## Install with r2modman

Install BepInExPack_Valheim 5.4.2350 and Jotunn 2.30.0 in your Valheim profile. Close Valheim, open the profile folder from r2modman Settings, and copy `dist/KneeSlapperFreeze.dll` into `BepInEx/plugins/Local-KneeSlapperAbilities/`. Use **Start modded**. A manually copied DLL loads but does not create an entry in r2modman’s installed-package list.

To add a listed local package, make a ZIP containing `manifest.json`, this `README.md`, a 256×256 `icon.png` of your choice, and `BepInEx/plugins/KneeSlapperFreeze/KneeSlapperFreeze.dll`; import it through r2modman’s local-mod import. The included DLL is the tested build.

The inventory compatibility bridge preserves Comfy’s equipment row separately from Better Archery’s two reserved rows. Keep this mod enabled when using those two together. The bridge applies to their inventories; character powers remain exclusive to Knee-Slapper.

## Build

Install .NET SDK 8. The default game directory is the standard Steam installation. BepInEx must be installed under the game directory and Jotunn available at `BepInEx/plugins/Jotunn/Jotunn.dll` for these build references.

```powershell
dotnet build KneeSlapperFreeze.csproj -c Release
# Alternate installation:
dotnet build KneeSlapperFreeze.csproj -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\Valheim"
```

## Local third-party compatibility fixes

These are unofficial local adaptations, not releases from the original authors:

| Original mod | Version | Changes |
| --- | --- | --- |
| Advize — Plant Everything | 1.20.0 | RPC constants and appended optional arguments |
| Azumatt — Recycle N Reclaim | 1.4.1 | InventoryElement rename, tooltip patch target, appended arguments, RPC constants, null/non-item ingredient lookup guard |
| ComfyMods — Comfy Quick Slots | 1.9.0 | InventoryElement rename, gameObject property, inventory overload targets and appended arguments |

The `compat-tools` directory contains the Cecil patcher, exact original/installed hashes, and transformation reports. Original and patched third-party DLLs are excluded; use your own installed copies. Run `apply-compatibility.ps1` with the profile directory after closing Valheim. The script checks exact input hashes, backs up originals, builds the patcher, validates all three outputs, then installs them. Unknown versions are rejected.

```powershell
.\apply-compatibility.ps1 -ProfilePath "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Default"
```

Reinstalling/updating a third-party mod can overwrite a local fix. New versions require review before reapplying. No multiplayer compatibility testing was performed; everyone including the server needs the custom mod and dependencies for the freezing effect.

## Validation

On September 10, 2026, all **61 integration assertions passed** with the full local r2modman profile loaded on Valheim 1.0.7. Tests covered gear, ammunition, freeze timing, jump behavior, an actual 30-meter fall, ordinary damage, visible Comfy equipment slots alongside quiver rows, raspberry bush registration/spawning, tooltip execution, and recycling a disposable club into wood. See `test-results/results-r2-profile.txt` and the temporary integration plugin source in `tests`.

Tests ran without graphics using an isolated save directory. They do not establish that every feature of each third-party mod works. Do not install the test plugin for regular play; it creates a test character/world and exits when explicitly launched with its test flag.
