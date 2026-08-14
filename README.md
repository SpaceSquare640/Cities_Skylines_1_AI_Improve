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

## Features

### Emergency vehicles (ambulance / fire truck / police car + helicopters)
- Emergency vehicles ignore path costs when actively dispatched, so they take
  the fastest route instead of the "safest/cheapest" one vanilla picks.
- Dispatch-to-arrival timing tracked for all six emergency vehicle/helicopter
  types.
- Fire trucks and fire helicopters are capped at 20 responders per burning
  building (independently) - vanilla has no such cap and a severe fire can
  pull in an unbounded number of vehicles. Vehicles blocked by the cap are
  force-redirected to another building that's still burning and still has
  room, instead of idling at the station. The cap lifts entirely for any
  building that's been burning continuously for 15+ minutes.
- When [Transfer Manager CE](https://github.com/Sleepy334/TransferManagerCE)'s
  own fire dispatch is enabled, target *selection* is left entirely to it
  (it already does real nearest-fire matching) - this mod only still
  enforces its own responder cap, instead of both mods fighting over the
  same `SetTarget` call and producing wrong-truck/not-nearest dispatch.
- Idle/returning fire trucks and helicopters also check for nearby still-
  burning buildings before accepting idle, prioritizing the closest fire -
  this incorporates and extends the core idea of the Steam Workshop mod
  [Smarter Firefighters: Improved AI](https://steamcommunity.com/sharedfiles/filedetails/?id=2346565561),
  combined with this mod's own 10-responder cap and 15-minute-uncap system.
  **You do not need to subscribe to Smarter Firefighters separately if
  you're using AI_Improve** - its behavior is already built in.
- Not gated behind any DLC - police, medical, and fire helicopters work
  whether or not you own the DLC that introduced them.

### Trains, intercity trains, and metro
- Occupancy-aware platform assignment - trains actively spread across a
  station's available tracks instead of all piling onto the same platform.
- Real-time congestion-density-based dynamic rerouting mid-journey, not just
  a one-time route chosen at departure.
- Correctly distinguishes departing trains from arriving ones, so leaving
  trains are never mistakenly funneled into platform-assignment logic meant
  for landing.
- Stations approaching saturation aren't forced onto the least-bad already-
  jammed platform.
- New incoming intercity trains are throttled when their destination station
  is already saturated, **and now also when city-wide train ridership itself
  is low** - reads the same smoothed passenger-throughput number the vanilla
  Public Transport info panel graph uses, so fewer trains spawn when real
  demand doesn't justify them, not just when a platform looks physically busy.
- Intercity train (and metro's shared `PassengerTrainAI`) passenger capacity
  boosted, and newly-spawned intercity trains start with a realistic
  non-zero "already boarded" passenger count instead of showing 0 - they're
  arriving from outside the map, so it's unrealistic for them to be empty.
- Ordinary (non-intercity) buses, passenger helicopters, and metro now fly
  past a stop instead of dwelling there when it already has 3+ other
  vehicles assigned to it, or when nobody boards that leg.
- Capacity boosts (train/intercity bus/passenger helicopter) defer entirely
  to [Advanced Vehicle Options](https://steamcommunity.com/sharedfiles/filedetails/?id=1548831935)
  when it's installed, instead of doubling whatever custom capacity it
  already set on a vehicle asset.

### Aircraft
- Occupancy-aware gate assignment across a wide, two-ring candidate search,
  so planes spread across an airport's real gate capacity instead of piling
  onto a handful of segments.
- Real-time congestion-based mid-flight rerouting.
- Correctly distinguishes departing flights (heading to an outside
  connection) from arriving ones, so departures are never mistaken for gate-
  seeking landings.
- Airports refuse new landings once saturated rather than piling planes onto
  an already-jammed taxiway.
- **Thunderstorm response**: airports and heliports are flipped directly to
  "Not Operating" (the same status the game itself uses for e.g. an
  unpowered building), and emergency helicopter depots too, for the
  duration of an active thunderstorm disaster - reopened automatically the
  moment the storm ends.

### Intercity and city buses
- Real-time congestion-density-based dynamic rerouting.

### Ordinary city traffic (cars, taxis, cargo trucks)
- Every ordinary road vehicle in the city (private cars, taxis, cargo trucks,
  buses) dynamically reroutes around real-time congestion mid-journey,
  not just once at trip start.
- Rate-limited to a small number of actual reroute computations per
  simulation frame, so a burst of vehicles crossing the congestion threshold
  at once doesn't cause a visible stutter.

### Citizens
- Citizens are less likely to drive their own car into an already-congested
  destination, nudging more trips toward walking or public transit instead.
- Taxi usage increased on trips where a citizen has already decided not to
  drive themselves.

### Race cars
- Removed the per-racer top-speed cap - corners still slow cars down
  realistically, but straights are no longer artificially capped.

## Requirements

- Cities: Skylines (base game)
- [Harmony (Mod Dependency)](https://steamcommunity.com/workshop/filedetails/?id=2040656402) by boformer, subscribed via Steam Workshop

## Compatibility

- **[TM:PE](https://github.com/CitiesSkylinesMods/TMPE)**: soft dependency, detected at runtime via reflection - no compile-time reference exists anywhere in this project. Some features (e.g. emergency vehicle priority) only activate when TM:PE is present and its Advanced Vehicle AI option is enabled; a TM:PE-independent fallback is planned but not yet implemented for all features.
- **[Transfer Manager CE](https://github.com/Sleepy334/TransferManagerCE)**: overlapping scope on service vehicle dispatch. This mod does not try to replace it; it targets problems Transfer Manager CE doesn't solve (multi-demand allocation, risk-weighted dispatch) rather than re-implementing its path-distance matching.
- **[Smarter Firefighters](https://steamcommunity.com/sharedfiles/filedetails/?id=2346565561)**: its core AI feature (redirecting idle/returning fire trucks and helicopters to nearby still-burning buildings) has been integrated into and extended by AI_Improve - see the Emergency vehicles section above. **No need to subscribe to it separately if you're running AI_Improve**; safe to also keep it subscribed if you want, but redundant.
- **[SingleTrainTrackAI](https://steamcommunity.com/sharedfiles/filedetails/?id=949504539)**: soft dependency, detected at runtime via reflection. Its own reservation system already redirects `TrainAI.UpdatePathTargetPositions` to solve single-track collision/deadlock avoidance properly (braking trains to a stop via speed control, not refusing pathfinds), and its own page states it's incompatible with any other mod touching that same method. AI_Improve detects it and stays passive instead of reimplementing or conflicting with it - no action needed either way.
- **[Reversible Tram AI](https://steamcommunity.com/sharedfiles/filedetails/?id=2740907672)**: soft dependency, detected at runtime via reflection, same reasoning as above - it already Harmony-patches tram simulation directly.
- **[Advanced Vehicle Options](https://steamcommunity.com/sharedfiles/filedetails/?id=1548831935)**: soft dependency, detected at runtime via reflection. This mod's capacity boosts defer entirely to AVO's own explicit per-vehicle capacity settings when it's installed, instead of doubling whatever it already set.

Known overlaps are documented, not avoided - see the Compatibility section
above for what each mod actually does differently.

## Building from source

1. Install Visual Studio 2022 (Community edition is fine) with the ".NET desktop development" workload.
2. Open `AIImprove.csproj`.
3. Edit the `<CSInstallPath>` property in the `.csproj` if your Cities: Skylines install isn't at the default Steam library path.
4. Build. The compiled DLL is automatically copied to your local `Addons/Mods/AIImprove` folder.

## License

GPL-3.0. See [LICENSE](LICENSE).

## Links

[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/aaUQVJeCgC)
[![GitHub](https://img.shields.io/badge/GitHub-Repository-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?style=for-the-badge&logo=paypal&logoColor=white)](https://paypal.me/SpaceSquare640)
