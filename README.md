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
  assignment, mid-journey rerouting, throughput-aware spawn throttling,
  boosted capacity, stop-skipping for congested/empty stops.
- **Aircraft** - occupancy-aware gate assignment, mid-flight rerouting,
  full thunderstorm response (airports/heliports/helicopter depots close and
  reopen automatically).
- **Buses, passenger helicopters** - dynamic rerouting, stop-skipping,
  boosted capacity.
- **Ordinary city traffic** - every road vehicle reroutes around real-time
  congestion mid-journey, not just once at trip start.
- **Citizens** - less likely to drive into already-congested destinations;
  taxi usage boosted as the alternative.
- **Race cars** - no artificial top-speed cap on straights.

**See the [Wiki](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve/wiki)
for the full, detailed feature list and compatibility notes** (soft
dependencies/integrations with TM:PE, Transfer Manager CE, Smarter
Firefighters, SingleTrainTrackAI, Reversible Tram AI, Advanced Vehicle
Options).

## Requirements

- Cities: Skylines (base game)
- [Harmony (Mod Dependency)](https://steamcommunity.com/workshop/filedetails/?id=2040656402) by boformer, subscribed via Steam Workshop

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
