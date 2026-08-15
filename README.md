# AI_Improve

A Cities: Skylines (2015) mod that improves the game's traffic, citizen, and
service vehicle AI decision quality - by SpaceSquare.

## Why this mod exists

Most existing "improved AI" mods for Cities: Skylines change one narrow slice
of behavior (a single Postfix on one method, a tweak to one constant) and the
effect is barely noticeable in an actual playthrough. This project targets a
gap those mods leave open: they all optimize a decision once, at the moment
it's made, and never revisit it as conditions change. `AI_Improve` focuses on
**continuous, dynamic adjustment** instead - vehicles and citizens that
reconsider their decisions as the city around them changes, not just once at
spawn time.

## What it improves

- **Emergency vehicles** - path-cost-ignoring dispatch, a real per-building
  responder cap, idle vehicles that check for nearby fires first.
- **Trains, intercity trains, and metro** - occupancy-aware platform
  assignment, mid-journey rerouting, throughput-aware spawn throttling.
- **Aircraft** - occupancy-aware gate assignment, mid-flight rerouting,
  thunderstorm response (airports refuse landings and departures,
  emergency helicopters stay grounded, for the duration of the storm).
- **Buses, passenger helicopters** - dynamic rerouting, boosted
  helicopter capacity.
- **Ordinary city traffic** - every road vehicle reroutes around real-time
  congestion mid-journey, not just once at trip start.
- **Citizens** - less likely to drive into already-congested destinations;
  taxi usage boosted as the alternative.
- **Race cars** - one consistent top-speed ceiling for every racer,
  with vanilla's cornering behavior left intact.

**See the [Wiki](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve/wiki)
for the full, detailed feature list and compatibility notes** (soft
dependencies/integrations with TM:PE, Transfer Manager CE, Smarter
Firefighters, SingleTrainTrackAI, Reversible Tram AI, Advanced Vehicle
Options).

## Settings

Every feature category above can be switched on or off individually, and
several values are adjustable. There are two places to configure the mod:

- **Full settings** - `ESC` → Options → Content Manager → Mods → **AI_Improve**.
  Tabbed page with all nine category toggles, tuning sliders (race car speed
  cap, fire responders per building, intercity train throttle threshold), a
  language selector, and links.
- **Quick panel** - the **AI_Improve** button on the
  [UnifiedUI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700)
  toolbar, in-game. Same toggles plus the empty-vehicle cleanup tools, without
  leaving your city. Optional - without UnifiedUI everything is still fully
  configurable from Content Manager.

Interface language follows the game automatically, and can be overridden to
English, 繁體中文, or 简体中文.

Turning a category off makes it behave exactly as if the feature had never
been written - the game's own original code runs untouched.

**See the [Settings wiki page](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve/wiki/Settings)
for the full reference**, including the empty-vehicle cleanup tools and the
verbose-logging switch used when collecting a bug report.

## Requirements

- Cities: Skylines (base game)
- [Harmony (Mod Dependency)](https://steamcommunity.com/workshop/filedetails/?id=2040656402) by boformer, subscribed via Steam Workshop
- Optional: [UnifiedUI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700) - for the in-game settings button

## Reporting a problem

Open an [issue](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve/issues/new/choose) -
there are templates for bug reports, feature requests, and mod conflicts.

Please attach your full `Cities_Data/output_log.txt`. Almost nothing can be diagnosed without it,
and logs from other mods (`TMPE.log`, `GameAnarchy.log`, …) don't contain any AI_Improve information.

Before reproducing the problem, turn on **Verbose logging** (settings → Tuning tab) so the log
actually contains per-vehicle detail - it's off by default because it's expensive. Also include the
version string shown in the settings header (e.g. `1.0.59+ee242bb`); it identifies the exact commit
your build came from.

## Building from source

1. Install Visual Studio 2022 (Community edition is fine) with the ".NET desktop development" workload.
2. Open `AIImprove.csproj`.
3. Edit the `<CSInstallPath>` property in the `.csproj` if your Cities: Skylines install isn't at the default Steam library path.
4. Build. The compiled DLL is automatically copied to your local `Addons/Mods/AIImprove` folder.

See the [Wiki](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve/wiki/Building-from-Source) for more detail.

## License

GPL-3.0. See [LICENSE](LICENSE).

## Links

[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/aaUQVJeCgC)
[![GitHub](https://img.shields.io/badge/GitHub-Repository-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve)
[![Wiki](https://img.shields.io/badge/Wiki-Docs-blue?style=for-the-badge&logo=github&logoColor=white)](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve/wiki)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?style=for-the-badge&logo=paypal&logoColor=white)](https://paypal.me/SpaceSquare640)
