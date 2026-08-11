# AI_Improve

A Cities: Skylines (2015) mod that improves the game's traffic, citizen, and
service vehicle AI decision quality.

## Why this mod exists

Most existing "improved AI" mods for Cities: Skylines change one narrow slice
of behavior (a single Postfix on one method, a tweak to one constant) and the
effect is barely noticeable in an actual playthrough. This project targets a
gap those mods leave open: they all optimize a decision once, at the moment
it's made, and never revisit it as conditions change. `AI_Improve` focuses on
**continuous, dynamic adjustment** instead - vehicles and citizens that
reconsider their decisions as the city around them changes, not just once at
spawn time.

## Current status

Early development. See open issues / commit history for progress. The first
working feature is emergency vehicle path priority (ambulances and fire
trucks weight road congestion less heavily when TM:PE's Advanced Vehicle AI
is active).

## Requirements

- Cities: Skylines (base game)
- [Harmony (Mod Dependency)](https://steamcommunity.com/workshop/filedetails/?id=2040656402) by boformer, subscribed via Steam Workshop

## Compatibility

- **[TM:PE](https://github.com/CitiesSkylinesMods/TMPE)**: soft dependency, detected at runtime via reflection - no compile-time reference exists anywhere in this project. Some features (e.g. emergency vehicle priority) only activate when TM:PE is present and its Advanced Vehicle AI option is enabled; a TM:PE-independent fallback is planned but not yet implemented for all features.
- **[Transfer Manager CE](https://github.com/Sleepy334/TransferManagerCE)**: overlapping scope on service vehicle dispatch. This mod does not try to replace it; it targets problems Transfer Manager CE doesn't solve (multi-demand allocation, risk-weighted dispatch) rather than re-implementing its path-distance matching.
- **[Smarter Firefighters](https://github.com/themonthlydaily/Cities-Skylines---Smarter-Firefighters-Improved-AI)**: overlapping scope on fire truck behavior, likely safe to run alongside since it only retargets trucks that are already idle/returning.

Known overlaps are documented, not avoided - see the Compatibility section
above for what each mod actually does differently.

## Building from source

1. Install Visual Studio 2022 (Community edition is fine) with the ".NET desktop development" workload.
2. Open `AIImprove.csproj`.
3. Edit the `<CSInstallPath>` property in the `.csproj` if your Cities: Skylines install isn't at the default Steam library path.
4. Build. The compiled DLL is automatically copied to your local `Addons/Mods/AIImprove` folder.

## License

GPL-3.0. See [LICENSE](LICENSE).
