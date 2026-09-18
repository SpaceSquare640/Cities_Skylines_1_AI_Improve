using ColossalFramework;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace AIImprove
{
    // Bounded v1 of "flexible mid-route rerouting" for trains/metro (both are TrainAI - metro is
    // just a VehicleInfo.VehicleType.Metro flavor of the same class) and aircraft taxiing - see
    // Cities_Skylines_1_AI_Improve_Document/01, this was the project's original core
    // differentiation thesis ("持續動態調整" vs every existing mod's "一次性最佳化"), deferred
    // from project start for complexity.
    //
    // Mechanism: Postfixes <VehicleAI>.SimulationStep(ushort, ref Vehicle, Vector3) - called
    // every tick, single `ref Vehicle` parameter, same safe shape used throughout this project.
    // When StuckRerouteTracker judges a vehicle has been essentially stationary for too long
    // (and isn't intentionally stopped - see the Flags check below), re-invokes the vehicle's own
    // 6-arg StartPathFind from its CURRENT position (vehicleData.m_targetPos3, which the game
    // itself already uses as "current position" for path recalculation - see VehicleAI's own
    // 2-arg StartPathFind) toward its existing destination, letting the pathfinder pick a
    // different route around whatever is blocking it.
    //
    // StartPathFind is protected, so it can't be called by name from this assembly - reflection
    // via a cached MethodInfo. Only invoked in the rare "actually stuck" branch (gated by
    // StuckRerouteTracker's cooldown), so reflection's per-call overhead is a non-issue - this
    // patch's hot path (the speed check on every tick) never touches reflection at all.
    internal static class FlexibleReroutePatch
    {
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            LoggedFirstCall.Clear();
        }

        private static readonly System.Collections.Generic.HashSet<string> LoggedFirstCall =
            new System.Collections.Generic.HashSet<string>();

        private static void TryReroute(
            string ownerTypeName,
            MethodInfo startPathFind,
            object aiInstance,
            ushort vehicleID,
            ref Vehicle vehicleData,
            float densityThreshold)
        {
            if (startPathFind == null)
            {
                return;
            }

            // Stations/gates intentionally stop vehicles (Vehicle.Flags.Stopped) - that is not
            // "stuck", and a fresh stop is a reasonable point to reset stuck-tracking state.
            if ((vehicleData.m_flags & Vehicle.Flags.Stopped) != 0)
            {
                StuckRerouteTracker.Clear(vehicleID);
                return;
            }

            // WaitingPath means a path request is already in flight - including, critically, the
            // one THIS reroute itself just started: StartPathFind sets WaitingPath on success, so
            // the very next tick after a successful reroute always sees this flag set. Root cause
            // of a live-tested rapid-reroute loop (2026-08-14): this used to be folded into the
            // Stopped check above and called StuckRerouteTracker.Clear() here too, which wiped out
            // the cooldown this same reroute had just set moments earlier - a vehicle whose
            // density was still high got to reroute again on literally the next opportunity
            // instead of waiting out the cooldown (confirmed: one cargo truck rerouted 52 times in
            // 44 seconds, roughly once a second, against a 40-second cooldown). Skipping without
            // clearing preserves the cooldown that was just set, while still correctly not
            // double-rerouting a vehicle that's already mid-repath for any reason.
            if ((vehicleData.m_flags & Vehicle.Flags.WaitingPath) != 0)
            {
                return;
            }

            // MEASUREMENT GAP CLOSED (2026-09-12): this file has two reroute paths and the
            // "did the route actually change" diagnostic was wired into only the other one
            // (TryRerouteViaSelf - road vehicles, buses, passenger helicopters). TryReroute is
            // what TrainAI and AircraftAI go through, which is precisely the A3 subject, so for
            // trains and aircraft the project could still only report that a request was
            // accepted - the exact metric 14 - 現況總表 (A00) says has never been an answer.
            //
            // Deliberately ahead of the cooldown check below: a vehicle that has just been
            // rerouted is ON cooldown, so anything placed after that gate would never see the
            // answer to the request it is waiting for. Staggered like everything else here.
            if (SimulationStagger.ShouldRunThisFrame(vehicleID))
            {
                RerouteEffectDiagnostics.CheckAfter(ownerTypeName, vehicleID, ref vehicleData);
            }

            // REVISED (2026-08-13): used to pick source vs target based on Vehicle.Flags.GoingBack,
            // mirroring a pattern borrowed from the emergency-vehicle dispatch code. dnSpy showed
            // that's not how the base VehicleAI.StartPathFind(ushort, ref Vehicle) - which TrainAI
            // doesn't override, and which many vehicle AIs including AircraftAI's own equivalent
            // built-in logic ultimately mirror - actually decides where to go: it unconditionally
            // trusts vehicleData.m_targetBuilding, GoingBack or not. That flag toggling was never
            // the right signal for "where is this vehicle currently headed."
            ushort targetBuilding = vehicleData.m_targetBuilding;

            // PERF (2026-08-15 audit): stagger (an integer modulo) now gates before the cooldown
            // dictionary lookup, and the first-call logging bookkeeping below moved down past all
            // of these - it was doing a string hash + HashSet probe for every vehicle in the city
            // every single tick, ahead of the very stagger check meant to make this path cheap.
            if (targetBuilding == 0 || !SimulationStagger.ShouldRunThisFrame(vehicleID) ||
                StuckRerouteTracker.IsOnCooldown(vehicleID))
            {
                return;
            }

            if (LoggedFirstCall.Add(ownerTypeName))
            {
                Debug.Log("[AIImprove] FlexibleReroutePatch (" + ownerTypeName + ") is executing.");
            }

            float aheadDensity = SegmentCongestionQuery.GetAverageAheadDensity(ref vehicleData);
            DensityDistributionDiagnostics.Record(ownerTypeName, aheadDensity, densityThreshold);
            if (aheadDensity < 0f || !RerouteRateLimiter.TryConsumeBudget() ||
                !StuckRerouteTracker.ShouldReroute(vehicleID, aheadDensity, densityThreshold))
            {
                return;
            }

            // Fingerprint the route we are about to replace, so a later tick can tell whether the
            // pathfinder actually returned a different one - see RerouteEffectDiagnostics for why
            // "the request was accepted" has never been an answer to that.
            RerouteEffectDiagnostics.RecordBefore(vehicleID, ref vehicleData);

            Vector3 endPos = Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding].m_position;
            Vector3 startPos = vehicleData.m_targetPos3;

            object[] args = { vehicleID, vehicleData, startPos, endPos, true, true };
            bool success = (bool)startPathFind.Invoke(aiInstance, args);
            vehicleData = (Vehicle)args[1]; // reflection writes ref/out results back into the array

            if (Log.VerboseEnabled)
            {
                // Aggregate first: the per-event line below says what happened to one vehicle,
                // this says what is happening overall. See RerouteFailureDiagnostics for why the
                // failure rate needed measuring rather than another round of reasoning about it.
                RerouteFailureDiagnostics.Record(ownerTypeName, ref vehicleData, success);

                Log.Verbose(
                    "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " ahead segment density " +
                    aheadDensity.ToString("F0") + " too high, requested reroute from current position: " +
                    (success ? "accepted" : "failed"));
            }
        }

        private static MethodInfo FindStartPathFind(Type vehicleAiType)
        {
            MethodInfo method = AccessTools.Method(
                vehicleAiType,
                "StartPathFind",
                new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) });

            if (method == null)
            {
                Debug.LogWarning(
                    "[AIImprove] " + vehicleAiType.Name + ".StartPathFind(6-arg) not found - game " +
                    "version may have changed. Flexible reroute disabled for " + vehicleAiType.Name + ".");
            }

            return method;
        }

        // m_targetBuilding does NOT consistently mean "a Building ID" - dnSpy showed BusAI's own
        // StartPathFind(ushort, ref Vehicle) picks between three different interpretations of that
        // field (a real Building when GoingBack or DummyTraffic, but a NetManager node ID for a
        // normal transport-line-following bus, which is what a real intercity bus is).
        //
        // CORRECTED 2026-09-16. This note used to say "the way it does for TrainAI/AircraftAI",
        // implying those two were safe to resolve by hand. They are not: PassengerTrainAI and
        // PassengerPlaneAI branch exactly the same three ways, node id included. That aside was
        // never checked against either class, and because it read as settled it kept trains and
        // aircraft on the hand-resolved path for a month - where half of every train's reroute
        // requests failed on a destination position read out of the wrong array. See Train.Postfix.
        //
        // The same quirk then turned up a third time in PassengerHelicopterAI and was written up
        // there too, still without anyone revisiting the claim about trains. 12 - 開發準則 準則 4
        // ("推論不是證據") applies to comments: an unverified aside gets quoted back as fact.
        //
        // Re-deriving that branching ourselves here would
        // risk resolving the wrong position and rerouting the bus somewhere nonsensical. Instead,
        // this reflectively calls BusAI's own 2-arg StartPathFind(ushort, ref Vehicle) - the exact
        // same convenience method vanilla itself uses to restart pathfinding toward "wherever this
        // vehicle is currently supposed to be going" - so vanilla's own branching handles target
        // resolution and we only ever have to decide *whether* to trigger a reroute, not *where to*.
        private static MethodInfo FindSelfStartPathFind(Type vehicleAiType)
        {
            MethodInfo method = AccessTools.Method(
                vehicleAiType,
                "StartPathFind",
                new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

            if (method == null)
            {
                Debug.LogWarning(
                    "[AIImprove] " + vehicleAiType.Name + ".StartPathFind(ushort, ref Vehicle) not " +
                    "found - game version may have changed. Flexible reroute disabled for " +
                    vehicleAiType.Name + ".");
            }

            return method;
        }

        private static void TryRerouteViaSelf(
            string ownerTypeName,
            MethodInfo selfStartPathFind,
            object aiInstance,
            ushort vehicleID,
            ref Vehicle vehicleData,
            float densityThreshold = StuckRerouteTracker.DensityThreshold)
        {
            if (selfStartPathFind == null)
            {
                return;
            }

            // See TryReroute's notes on why Stopped and WaitingPath are handled differently -
            // clearing on WaitingPath here too caused the exact same rapid-reroute-loop bug for
            // vehicles that go through this self-StartPathFind path (buses, passenger
            // helicopters).
            if ((vehicleData.m_flags & Vehicle.Flags.Stopped) != 0)
            {
                StuckRerouteTracker.Clear(vehicleID);
                return;
            }

            if ((vehicleData.m_flags & Vehicle.Flags.WaitingPath) != 0)
            {
                return;
            }

            // Deliberately ahead of the cooldown check below: a vehicle that has just been
            // rerouted is ON cooldown, so anything placed after that gate would never see the
            // answer to the request it is waiting for. Staggered like everything else here.
            if (SimulationStagger.ShouldRunThisFrame(vehicleID))
            {
                RerouteEffectDiagnostics.CheckAfter(ownerTypeName, vehicleID, ref vehicleData);
            }

            // Same ordering rationale as TryReroute (2026-08-15 audit): cheap modulo before the
            // dictionary probe, first-call logging bookkeeping after both.
            if ((vehicleData.m_targetBuilding == 0 && vehicleData.m_sourceBuilding == 0) ||
                !SimulationStagger.ShouldRunThisFrame(vehicleID) || StuckRerouteTracker.IsOnCooldown(vehicleID))
            {
                return;
            }

            if (LoggedFirstCall.Add(ownerTypeName))
            {
                Debug.Log("[AIImprove] FlexibleReroutePatch (" + ownerTypeName + ") is executing.");
            }

            float aheadDensity = SegmentCongestionQuery.GetAverageAheadDensity(ref vehicleData);
            DensityDistributionDiagnostics.Record(ownerTypeName, aheadDensity, densityThreshold);
            if (aheadDensity < 0f || !RerouteRateLimiter.TryConsumeBudget() || !StuckRerouteTracker.ShouldReroute(vehicleID, aheadDensity, densityThreshold))
            {
                return;
            }

            // Fingerprint the route we are about to replace, so a later tick can tell whether the
            // pathfinder actually returned a different one - see RerouteEffectDiagnostics for why
            // "the request was accepted" has never been an answer to that.
            RerouteEffectDiagnostics.RecordBefore(vehicleID, ref vehicleData);

            object[] args = { vehicleID, vehicleData };
            bool success = (bool)selfStartPathFind.Invoke(aiInstance, args);
            vehicleData = (Vehicle)args[1];

            if (Log.VerboseEnabled)
            {
                // ADDED (2026-09-05) after the first live capture: this project has two reroute
                // paths and the diagnostics only covered the other one. That session logged zero
                // TrainAI/AircraftAI reroutes and 89 through here (cargo trucks, one post van), so
                // the instrumented path never fired once while the uninstrumented one carried
                // every sample. Worth noting what those 89 already showed: 26 accepted of 89, a
                // 29% acceptance rate - nothing like the 9% recorded for trains in
                // Cities_Skylines_1_AI_Improve_Document/01, which supports that figure being about
                // rail specifically rather than rerouting in general.
                RerouteFailureDiagnostics.Record(ownerTypeName, ref vehicleData, success);

                Log.Verbose(
                    "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " ahead segment density " +
                    aheadDensity.ToString("F0") + " too high, requested reroute (via self StartPathFind): " +
                    (success ? "accepted" : "failed"));
            }
        }

        // SWITCHED TO THE SELF-StartPathFind PATH (2026-09-16), after a live session finally
        // measured what trains were doing: 99 reroute attempts, 50 of them failing outright,
        // against 688 attempts and ZERO failures for road vehicles going through
        // TryRerouteViaSelf. That asymmetry was the clue.
        //
        // TryReroute resolves the destination itself:
        //
        //     Vector3 endPos = BuildingManager.m_buildings.m_buffer[targetBuilding].m_position;
        //
        // and PassengerTrainAI.StartPathFind(ushort, ref Vehicle) shows why that is wrong. It
        // branches three ways on the same field:
        //
        //     GoingBack     -> m_buildings[m_targetBuilding]   (a Building)
        //     DummyTraffic  -> m_buildings[m_targetBuilding]   (a Building)
        //     otherwise     -> m_nodes[m_targetBuilding]       (a NetManager NODE)
        //
        // A train following a transport line takes the third branch, so m_targetBuilding is a
        // node id. Indexing the building array with it yields an unrelated building, or an empty
        // slot at (0,0,0) - and FindPathPosition then finds no track within 32m of that point, so
        // StartPathFind returns false. That is the 50%. PassengerPlaneAI is identical.
        //
        // THE PART WORTH REMEMBERING: this project already knew about this quirk. It found it in
        // BusAI, wrote it up in FindSelfStartPathFind, and fixed buses by calling vanilla's own
        // 2-arg overload. It found it again in PassengerHelicopterAI and fixed that the same way.
        // But that first note also asserted m_targetBuilding means a Building "the way it does for
        // TrainAI/AircraftAI" - an aside nobody checked, which is the entire reason trains and
        // aircraft were left on the broken path for a month. An unverified claim in a comment gets
        // quoted back as established fact; 12 - 開發準則 準則 4 applies to comments too.
        //
        // Platform assignment is unaffected: the 2-arg overload calls the 4-arg, which calls the
        // 6-arg that TrainPlatformAssignmentPatch hooks, so that patch still runs - as does
        // TM:PE's, which the shared-patch log shows on the same method. The only thing that
        // changes is who computes endPos, and vanilla computes it correctly.
        //
        // Resolved per runtime type rather than once for TrainAI, because the overrides differ:
        // PassengerTrainAI has its own, CargoTrainAI has its own, MetroTrainAI inherits
        // PassengerTrainAI's, and TrainAI itself declares none. Same shape as Car.Postfix.
        internal static class Train
        {
            // NOT LOCKED, AND THAT IS THE CORRECTION (2026-09-18). A lock was added here earlier
            // the same day, citing CompanionModCompat.FindType's note that a concurrently written
            // Dictionary can spin forever inside a resize instead of throwing. That failure mode is
            // real; the premise that it could happen here was not. Verified against the installed
            // assemblies: Assembly-CSharp has m_simulationThread (singular, no plural form),
            // PathFindThread is a separate pool we do not patch into, and ICities offers
            // OnBeforeSimulationTick/OnAfterSimulationTick precisely because OnUpdate is a
            // different thread. Vehicle AI is stepped by exactly ONE thread, this cache is reached
            // only from this Postfix, and so it can only ever be touched by that thread.
            //
            // The cost was not theoretical: the lock was taken on EVERY call, not just the cold
            // one. See the threading note in FireResponseCapPatch.cs for the evidence and for the
            // cases where a lock IS needed (main thread reading tracker state, e.g.
            // EmergencyDispatchTracker and AirTrafficControlManager - those keep theirs).
            private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo> SelfStartPathFindCache =
                new System.Collections.Generic.Dictionary<Type, MethodInfo>();

            private static MethodInfo GetSelfStartPathFind(Type vehicleAiType)
            {
                MethodInfo method;
                if (!SelfStartPathFindCache.TryGetValue(vehicleAiType, out method))
                {
                    method = FindSelfStartPathFind(vehicleAiType);
                    SelfStartPathFindCache[vehicleAiType] = method; // cache null too
                }

                return method;
            }

            public static void Postfix(ushort vehicleID, TrainAI __instance, ref Vehicle data)
            {
                // Metro vs intercity train split, each with its own toggle and density threshold
                // (2026-08-15, per user request to split every feature apart).
                bool isMetro = __instance is MetroTrainAI;
                bool enabled = isMetro
                    ? ModSettings.MetroRerouteEnabled.value
                    : (__instance is PassengerTrainAI
                        ? ModSettings.IntercityTrainRerouteEnabled.value
                        : ModSettings.MetroRerouteEnabled.value);
                if (!enabled)
                {
                    return;
                }

                float densityThreshold = isMetro
                    ? ModSettings.MetroRerouteDensityThreshold.value
                    : ModSettings.IntercityTrainRerouteDensityThreshold.value;

                TryRerouteViaSelf(
                    nameof(TrainAI), GetSelfStartPathFind(__instance.GetType()), __instance,
                    vehicleID, ref data, densityThreshold);
            }
        }

        // STILL ON THE HAND-RESOLVED PATH, KNOWINGLY (2026-09-16). PassengerPlaneAI has the exact
        // same three-way m_targetBuilding branching PassengerTrainAI does - decompiled, node id
        // and all - so every word of Train.Postfix's note applies here too. Aircraft were not
        // switched over with the trains for two reasons:
        //
        //   1. It would change nothing observable yet. Measured taxiway congestion peaks at 15.8
        //      against a threshold of 30 - 99.8% of 62,500 samples sat in the 0-9 bucket - so this
        //      reroute path does not fire at all. Fixing destination resolution for a feature that
        //      never triggers buys no evidence, and 準則 5 wants evidence.
        //   2. HoldingPatternPatch.TryUpdateHolding shares StartPathFindMethod with this call and
        //      drives the holding-pattern behaviour off the same 6-arg overload. Rerouting aircraft
        //      through the 2-arg overload would need that interaction worked out first, and there
        //      is no reason to spend that risk on a path with no measurable output.
        //
        // So: a known defect, deliberately unfixed, recorded rather than left to be rediscovered.
        // If the threshold question is ever solved, fix this at the same time - not before.
        internal static class Aircraft
        {
            private static readonly MethodInfo StartPathFindMethod = FindStartPathFind(typeof(AircraftAI));

            public static void Postfix(ushort vehicleID, AircraftAI __instance, ref Vehicle data)
            {
                if (!ModSettings.AircraftRerouteEnabled.value)
                {
                    return;
                }

                if (HoldingPatternPatch.TryUpdateHolding(StartPathFindMethod, __instance, vehicleID, ref data))
                {
                    return;
                }

                TryReroute(
                    nameof(AircraftAI), StartPathFindMethod, __instance, vehicleID, ref data,
                    ModSettings.AircraftRerouteDensityThreshold.value);
            }
        }

        // Patched on CarAI (the declaring type of SimulationStep(ushort, ref Vehicle, Vector3) -
        // several subtypes, e.g. BusAI, only override the ref-Frame overload) and covers every
        // ordinary road vehicle: private cars, taxis, cargo trucks, service vehicles, and both
        // in-city and intercity buses. Started out intercity-bus-only (2026-08-12) then widened to
        // "all ordinary city traffic" per explicit request (2026-08-13) - "跟巴士/火車一樣的邏輯"
        // applied to normal CarAI traffic, only excluding emergency vehicles (Ambulance/FireTruck/
        // PoliceCar), which already have their own ignore-costs dispatch handling and are out of
        // scope here.
        //
        // Different concrete CarAI subtypes override StartPathFind(ushort, ref Vehicle)
        // differently (e.g. BusAI's own override branches on GoingBack/DummyTraffic/transport-line
        // in ways a plain CarAI never would - see FindSelfStartPathFind's notes) - so the method to
        // invoke must be resolved per the vehicle's *actual* runtime type, not just typeof(CarAI).
        // Results are cached per type since AccessTools.Method itself isn't especially cheap and
        // this covers every car in the city.
        internal static class Car
        {
            // LOCK ADDED 2026-09-16 (準則 2, same shape as Train.GetSelfStartPathFind). This cache
            // had none, and it is written from the hottest Postfix in the project - every road
            // vehicle in the city. CompanionModCompat.FindType documents the failure mode: a
            // concurrently written Dictionary can loop forever inside a resize rather than
            // throwing, which presents as a frozen game with nothing in the log. Found while
            // adding the equivalent cache for trains; fixing only the new one would have left the
            // busier instance of the same hazard in place.
            // NOT LOCKED, AND THAT IS THE CORRECTION (2026-09-18). A lock was added here earlier
            // the same day, citing CompanionModCompat.FindType's note that a concurrently written
            // Dictionary can spin forever inside a resize instead of throwing. That failure mode is
            // real; the premise that it could happen here was not. Verified against the installed
            // assemblies: Assembly-CSharp has m_simulationThread (singular, no plural form),
            // PathFindThread is a separate pool we do not patch into, and ICities offers
            // OnBeforeSimulationTick/OnAfterSimulationTick precisely because OnUpdate is a
            // different thread. Vehicle AI is stepped by exactly ONE thread, this cache is reached
            // only from this Postfix, and so it can only ever be touched by that thread.
            //
            // The cost was not theoretical: the lock was taken on EVERY call, not just the cold
            // one. See the threading note in FireResponseCapPatch.cs for the evidence and for the
            // cases where a lock IS needed (main thread reading tracker state, e.g.
            // EmergencyDispatchTracker and AirTrafficControlManager - those keep theirs).
            private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo> StartPathFindCache =
                new System.Collections.Generic.Dictionary<Type, MethodInfo>();

            private static MethodInfo GetSelfStartPathFind(Type vehicleAiType)
            {
                MethodInfo method;
                if (!StartPathFindCache.TryGetValue(vehicleAiType, out method))
                {
                    method = FindSelfStartPathFind(vehicleAiType);
                    StartPathFindCache[vehicleAiType] = method; // cache null too - avoid re-resolving every call
                }

                return method;
            }

            // Emergency vehicles reroute more eagerly than ordinary traffic: an ambulance sitting
            // in a jam costs more than a delivery van doing the same, so it should be willing to
            // take a longer route sooner. Uncalibrated starting value, deliberately below the 80
            // the other categories default to; no slider, to avoid another four translated strings
            // before there is any evidence about what the right number is.
            // Calibrated 2026-09-07 alongside the settings thresholds: AmbulanceAI's measured
        // ahead-density distribution (46000 samples) puts its top 10% at 50. See ModSettings.cs
        // for the full percentile table. Not a slider, because emergency behaviour should not be
        // something a player can quietly detune.
        private const float EmergencyDensityThreshold = 50f;

            public static void Postfix(ushort vehicleID, CarAI __instance, ref Vehicle data)
            {
                // WAS AN UNCONDITIONAL RETURN until 2026-09-06. The comment in Patcher.cs said
                // emergency vehicles were "handled separately"; what handled them was
                // EmergencyIgnoreCostsPatch, and that turned out to skip only NetLane.m_ticketCost
                // - the toll on toll roads (dnSpy: PathFind.m_ignoreCost has three read sites and
                // all three guard nothing else). So the vehicles with the strongest reason to
                // route around a jam were the only ones excluded from the feature that does it,
                // and a player's screenshots of dozens of ambulances queued bumper to bumper are
                // what made that visible.
                bool isEmergency = __instance is AmbulanceAI || __instance is FireTruckAI ||
                                   __instance is PoliceCarAI;

                // PERF (2026-09-05): the stagger test moved ahead of the settings reads below.
                // This is the hottest Postfix in the project - every road vehicle in the city, on
                // every tick - and each ModSettings read is a lock plus a string-keyed dictionary
                // lookup inside ColossalFramework's SettingsFile (dnSpy-confirmed: SavedInt.value
                // with autoUpdate re-syncs through SettingsFile.GetValue, which is in-memory but
                // takes a lock). Two of those were being paid for every vehicle every tick, ahead
                // of the very check meant to skip 31 ticks out of 32.
                //
                // Third time this exact shape has appeared: LoggedFirstCall's string hashing ahead
                // of the stagger (2026-08-15), CompanionModCompat.FindType's assembly scan ahead
                // of it (fixed alongside this), and now these. The optimization keeps being right
                // and placed behind the thing it was meant to make cheap.
                //
                // TryRerouteViaSelf still runs its own stagger check - same formula, same answer,
                // and it is one integer modulo. Leaving it there keeps the other callers (trains,
                // aircraft, passenger helicopters) correct without threading a flag through.
                //
                // The one behavioural nuance: TryRerouteViaSelf clears StuckRerouteTracker state
                // for vehicles flagged Stopped, and that clear is now staggered too. A stopped
                // vehicle stays stopped for far longer than the stagger interval, so it still gets
                // cleared - just up to 31 ticks later.
                if (!SimulationStagger.ShouldRunThisFrame(vehicleID))
                {
                    return;
                }

                // This wrapper covers "一般市內交通" (private cars, taxis, cargo trucks), local
                // buses, and intercity buses (BusAI is a CarAI subtype) - each is its own toggle
                // and density threshold now (2026-08-15, per user request to split every feature
                // apart, not just categories).
                var busAi = __instance as BusAI;
                bool isIntercityBus = busAi != null && TransportStationAI.IsIntercity(busAi.m_info.m_class);

                bool enabled;
                float densityThreshold;
                if (isEmergency)
                {
                    enabled = ModSettings.EmergencyRerouteEnabled.value;
                    densityThreshold = EmergencyDensityThreshold;
                }
                else if (busAi == null)
                {
                    enabled = ModSettings.OrdinaryTrafficRerouteEnabled.value;
                    densityThreshold = ModSettings.OrdinaryTrafficRerouteDensityThreshold.value;
                }
                else if (isIntercityBus)
                {
                    enabled = ModSettings.IntercityBusRerouteEnabled.value;
                    densityThreshold = ModSettings.IntercityBusRerouteDensityThreshold.value;
                }
                else
                {
                    enabled = ModSettings.LocalBusRerouteEnabled.value;
                    densityThreshold = ModSettings.LocalBusRerouteDensityThreshold.value;
                }

                if (!enabled)
                {
                    return;
                }

                Type actualType = __instance.GetType();
                MethodInfo startPathFind = GetSelfStartPathFind(actualType);

                // Local and intercity buses are the SAME class (BusAI), so reporting them under
                // actualType.Name merged them in every log line and every diagnostic bucket -
                // which is exactly the question we could not answer about intercity buses
                // (2026-09-06): whether they were being covered at all, or whether every sample
                // attributed to "BusAI" was a city route. They have separate toggles and separate
                // thresholds, so they need separate labels. See 12 - 開發準則, 準則 10.
                string reportedType = busAi == null
                    ? actualType.Name
                    : (isIntercityBus ? "BusAI(Intercity)" : "BusAI(Local)");

                TryRerouteViaSelf(reportedType, startPathFind, __instance, vehicleID, ref data, densityThreshold);
            }
        }

        // PassengerHelicopterAI is a genuinely different vehicle class from the emergency copters
        // (AmbulanceCopterAI/FireCopterAI/PoliceCopterAI, which are HelicopterAI and never touch
        // PathManager/CreatePath at all) - it inherits VehicleAI directly and does real
        // PathManager.FindPathPosition/CreatePath pathfinding, confirmed via dnSpy, so real-time
        // congestion-based rerouting applies the same way it does for buses/cargo trucks. Its own
        // StartPathFind(ushort, ref Vehicle) has the same "m_targetBuilding is a NetManager node
        // ID, not a Building ID" quirk BusAI has, so this reuses the same self-StartPathFind
        // reflection trick rather than resolving a destination position itself.
        internal static class PassengerHelicopter
        {
            private static readonly MethodInfo StartPathFindMethod = FindSelfStartPathFind(typeof(PassengerHelicopterAI));

            public static void Postfix(ushort vehicleID, PassengerHelicopterAI __instance, ref Vehicle data)
            {
                if (!ModSettings.PassengerHelicopterRerouteEnabled.value)
                {
                    return;
                }

                TryRerouteViaSelf(nameof(PassengerHelicopterAI), StartPathFindMethod, __instance, vehicleID, ref data,
                    StuckRerouteTracker.DensityThreshold);
            }
        }
    }
}
