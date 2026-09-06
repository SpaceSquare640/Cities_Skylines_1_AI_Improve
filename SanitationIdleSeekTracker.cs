using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "垃圾車／殯儀車調度" (2026-08-17). Same idea as FireResponseTracker's idle-seek half, applied
    // to garbage trucks and hearses.
    //
    // dnSpy showed GarbageTruckAI/HearseAI already get mid-route congestion rerouting for free
    // (they're plain CarAI subtypes, covered by the shared FlexibleReroutePatch.Car registration) -
    // that part didn't need new work. The real gap is upstream of routing entirely: when a truck
    // finishes a job and has room for more, vanilla's own SetTarget doesn't look for nearby work
    // itself - it posts an AddIncomingOffer and waits for TransferManager.MatchOffers to pair it
    // with something, which (per the TransferManager research in
    // Cities_Skylines_1_AI_Improve_Document/10) is priority-bucket and distance-*weighted*, not a
    // literal nearest-first search. A truck can sit waiting for a match while a much closer
    // building with the same need goes unserved. This is the same class of problem the fire idle-
    // seek feature solves, using the same "search a self-maintained bucket, don't touch
    // MatchOffers" approach that avoided the risk of patching TransferManager directly.
    //
    // Generic over TransferManager.TransferReason (Garbage or Dead) rather than one tracker per
    // vehicle type, since the logic is identical - only the material differs.
    internal static class SanitationIdleSeekTracker
    {
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            KnownGarbageBuildings.Clear();
            KnownDeadBuildings.Clear();
            StaleBuildings.Clear();
            GarbageAssignedCount.Clear();
            GarbageAssignment.Clear();
            DeadAssignedCount.Clear();
            DeadAssignment.Clear();
        }

        private static readonly HashSet<ushort> KnownGarbageBuildings = new HashSet<ushort>();
        private static readonly HashSet<ushort> KnownDeadBuildings = new HashSet<ushort>();
        private static readonly List<ushort> StaleBuildings = new List<ushort>();

        // "Garbage Trucks and Hearses ... both seem to select one depot and start transfers from
        // it while other depots aren't. This can tie up a lot of vehicles better used elsewhere.
        // Oddly, the depot being transferred from isn't necessarily full - in many cases it's
        // empty." (Fey Warrior, Steam Workshop, 2026-09-05). Two distinct defects were behind that
        // report; both are addressed here.
        //
        // (A) NO PER-BUILDING CAP. TryFindNearby only ever asked "closest building with
        //     GetMaterialAmount > 0", with nothing tracking how many vehicles had already been
        //     sent there, so every idle vehicle running the search in the same window picked the
        //     same winner. FireResponseTracker has had a per-building responder cap since
        //     2026-08-12 for exactly this reason. The original note in SanitationIdleSeekPatch.cs
        //     argued sanitation needed no cap because "TransferManager's own Amount-based offer
        //     matching already prevents over-collection" - which is precisely the matching this
        //     feature bypasses. Same shape as FireResponseTracker: per-building count plus
        //     per-vehicle assignment, so a re-assignment moves the count rather than
        //     double-counting or leaking.
        //
        // (B) DISPOSAL FACILITIES WERE VALID CANDIDATES - see IsDisposalFacility for the
        //     dnSpy-confirmed mechanism. This is the "isn't necessarily full ... in many cases
        //     it's empty" half of the report.
        //
        // UNCALIBRATED STARTING VALUE, deliberately a const and not a slider: a slider means
        // settings-page layout plus nine language translations, and there is no player data yet to
        // calibrate against. 4 is "clearly more than one, clearly not a swarm" - one building's
        // garbage/corpse buffer is emptied by a handful of trucks, unlike a fire where 20
        // responders is reasonable. Revisit once a player log exists.
        private const int MaxVehiclesPerBuilding = 4;

        private static readonly Dictionary<ushort, int> GarbageAssignedCount = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> GarbageAssignment = new Dictionary<ushort, ushort>();
        private static readonly Dictionary<ushort, int> DeadAssignedCount = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> DeadAssignment = new Dictionary<ushort, ushort>();

        private static HashSet<ushort> SetFor(TransferManager.TransferReason material)
        {
            return material == TransferManager.TransferReason.Dead ? KnownDeadBuildings : KnownGarbageBuildings;
        }

        private static Dictionary<ushort, int> CountsFor(TransferManager.TransferReason material)
        {
            return material == TransferManager.TransferReason.Dead ? DeadAssignedCount : GarbageAssignedCount;
        }

        private static Dictionary<ushort, ushort> AssignmentsFor(TransferManager.TransferReason material)
        {
            return material == TransferManager.TransferReason.Dead ? DeadAssignment : GarbageAssignment;
        }

        // Call whenever vanilla itself assigns a real (non-zero) target for this material - the
        // building has just proven it has the need, so it becomes a candidate for the next truck
        // that goes looking.
        public static void Observe(TransferManager.TransferReason material, ushort vehicleId, ushort buildingId)
        {
            // Count vanilla's own assignments as well as our retargets - otherwise the cap would
            // only see vehicles this feature redirected, and would happily stack them on top of a
            // building TransferManager had already saturated.
            Assign(material, vehicleId, buildingId);

            if (buildingId != 0)
            {
                SetFor(material).Add(buildingId);
            }
        }

        // Records vehicleId as being on its way to buildingId, releasing whatever it was assigned
        // to before; buildingId == 0 just clears the old assignment. Never refuses - vanilla's own
        // dispatch decisions are not blocked by this mod. The cap only filters which buildings
        // this feature is willing to *retarget* an idle vehicle to (see TryFindNearby).
        public static void Assign(TransferManager.TransferReason material, ushort vehicleId, ushort buildingId)
        {
            Dictionary<ushort, int> counts = CountsFor(material);
            Dictionary<ushort, ushort> assignments = AssignmentsFor(material);

            ushort previous;
            if (assignments.TryGetValue(vehicleId, out previous) && previous != 0)
            {
                Decrement(counts, previous);
                assignments.Remove(vehicleId);
            }

            if (buildingId == 0)
            {
                return;
            }

            int current;
            counts.TryGetValue(buildingId, out current);
            counts[buildingId] = current + 1;
            assignments[vehicleId] = buildingId;
        }

        // Call when a vehicle despawns, so per-building counts don't leak upward forever and a
        // recycled vehicle ID doesn't inherit a dead vehicle's assignment - the same recycled-ID
        // hazard documented in AircraftReleasePatch.Postfix (2026-08-15).
        //
        // KNOWN LIMITATION (2026-09-05): the single cleanup point is that Postfix, which patches
        // VehicleAI.ReleaseVehicle. Vehicle.Unspawn() does NOT route through ReleaseVehicle, so a
        // vehicle removed by that path leaks one count against its last target until that building
        // is re-observed. That path is being handled separately; nothing here depends on it, and a
        // leaked count only makes this feature more conservative, never more aggressive.
        public static void ReleaseVehicle(ushort vehicleId)
        {
            ReleaseFrom(GarbageAssignedCount, GarbageAssignment, vehicleId);
            ReleaseFrom(DeadAssignedCount, DeadAssignment, vehicleId);
        }

        private static void ReleaseFrom(Dictionary<ushort, int> counts, Dictionary<ushort, ushort> assignments, ushort vehicleId)
        {
            ushort building;
            if (assignments.TryGetValue(vehicleId, out building))
            {
                Decrement(counts, building);
                assignments.Remove(vehicleId);
            }
        }

        private static void Decrement(Dictionary<ushort, int> counts, ushort buildingId)
        {
            int count;
            if (!counts.TryGetValue(buildingId, out count))
            {
                return;
            }

            if (count <= 1)
            {
                counts.Remove(buildingId);
            }
            else
            {
                counts[buildingId] = count - 1;
            }
        }

        // Is this building a *destination* for the material rather than a source of it?
        //
        // dnSpy, Assembly-CSharp.dll (2026-09-05) - why disposal facilities could be picked as
        // idle-seek targets at all:
        //   CemeteryAI.GetMaterialAmount(Dead) -> amount = m_customBuffer1 + GetDeadCount(),
        //                                        max    = m_graveCount (or m_corpseCapacity).
        //     That is the number of bodies the cemetery is STORING, not a number of bodies waiting
        //     to be collected. Any cemetery holding even one body reports amount > 0 permanently,
        //     so once it entered the pool it was a forever-valid, never-pruned candidate; being
        //     centrally placed it frequently won the nearest-building contest for every idle
        //     hearse in the city. That matches the report exactly, including "isn't necessarily
        //     full - in many cases it's empty": an almost empty cemetery still reports non-zero.
        //   LandfillSiteAI.GetMaterialAmount(Garbage) -> amount = 0, max = 1000000.
        //     Landfills/incinerators therefore always self-pruned, so the garbage side never had
        //     this second defect - only the missing cap (A).
        // A cemetery gets into the pool because Observe records whatever building vanilla assigned,
        // and HearseAI.SetTarget is also how vanilla dispatches the DeadMove "empty this cemetery"
        // transfers; the transfer reason is not visible at the SetTarget call site.
        private static bool IsDisposalFacility(BuildingAI ai, TransferManager.TransferReason material)
        {
            return material == TransferManager.TransferReason.Dead
                ? ai is CemeteryAI
                : ai is LandfillSiteAI;
        }

        // Searches the known-buildings pool for the closest one to fromPosition that still
        // genuinely has the need (live GetMaterialAmount check, not trusting the pool alone), is
        // not a disposal facility, is not already at the per-building cap, and isn't already the
        // vehicle's own current source/target. Returns 0 if nothing suitable is found.
        // Self-cleaning: buildings confirmed to no longer need the material are dropped during the
        // walk - see FireResponseTracker's TryFindAlternateBurningBuilding for the same pattern
        // and the reasoning ("skipping a stale entry is not the same as removing it").
        public static ushort TryFindNearby(TransferManager.TransferReason material, ushort excludeBuilding, Vector3 fromPosition)
        {
            HashSet<ushort> pool = SetFor(material);
            Dictionary<ushort, int> counts = CountsFor(material);
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            ushort best = 0;
            float bestDistSqr = float.MaxValue;

            StaleBuildings.Clear();

            foreach (ushort candidate in pool)
            {
                if (candidate == 0 || candidate == excludeBuilding)
                {
                    continue;
                }

                BuildingInfo info = buildings[candidate].Info;
                if (info == null || info.m_buildingAI == null)
                {
                    StaleBuildings.Add(candidate);
                    continue;
                }

                if (IsDisposalFacility(info.m_buildingAI, material))
                {
                    // Where the material goes, not where it comes from - see IsDisposalFacility.
                    // Dropped from the pool outright: a building's AI does not change, so this
                    // candidate can never become valid later.
                    StaleBuildings.Add(candidate);
                    continue;
                }

                int amount;
                int max;
                info.m_buildingAI.GetMaterialAmount(candidate, ref buildings[candidate], material, out amount, out max);
                if (amount <= 0)
                {
                    StaleBuildings.Add(candidate);
                    continue;
                }

                int assigned;
                counts.TryGetValue(candidate, out assigned);
                if (assigned >= MaxVehiclesPerBuilding)
                {
                    // At cap - not stale, just full for now, so it stays in the pool for later.
                    continue;
                }

                float distSqr = (buildings[candidate].m_position - fromPosition).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = candidate;
                }
            }

            for (int i = 0; i < StaleBuildings.Count; i++)
            {
                pool.Remove(StaleBuildings[i]);
            }

            StaleBuildings.Clear();

            if (best != 0 && Log.VerboseEnabled)
            {
                int assignedToBest;
                counts.TryGetValue(best, out assignedToBest);
                SanitationRetargetDiagnostics.Record(material, best, ref buildings[best], assignedToBest, pool.Count);
            }

            return best;
        }
    }

    // Verbose-only instrumentation for the retarget decision, added alongside the 2026-09-05 fix
    // above. The cap (A) stands on its own; this exists to let a player log confirm or refute what
    // the code reading says about (B) on a real city - specifically, which buildings idle-seek
    // actually keeps choosing, how many vehicles are already headed there, and what
    // GetMaterialAmount reports for them.
    //
    // Throttled the same way as RerouteFailureDiagnostics: one line per N retargets per material,
    // carrying the most recent sample plus aggregates, so a long session produces a readable
    // handful of lines instead of one per vehicle. Callers must guard with
    // `if (Log.VerboseEnabled)` - with verbose off, not even the counters are touched.
    internal static class SanitationRetargetDiagnostics
    {
        private const int ReportEveryRetargets = 25;

        private sealed class Counters
        {
            public int Retargets;
            public int AtOrAboveHalfCap;
            public ushort LastBuilding;
            public int LastAssigned;
            public int LastAmount;
            public int LastMax;
            public string LastAiTypeName = "?";
        }

        private static readonly Dictionary<TransferManager.TransferReason, Counters> ByMaterial =
            new Dictionary<TransferManager.TransferReason, Counters>();

        public static void Record(
            TransferManager.TransferReason material, ushort buildingId, ref Building building,
            int alreadyAssigned, int poolSize)
        {
            Counters counters;
            if (!ByMaterial.TryGetValue(material, out counters))
            {
                counters = new Counters();
                ByMaterial[material] = counters;
            }

            counters.Retargets++;

            BuildingInfo info = building.Info;
            BuildingAI ai = info != null ? info.m_buildingAI : null;

            int amount = 0;
            int max = 0;
            if (ai != null)
            {
                ai.GetMaterialAmount(buildingId, ref building, material, out amount, out max);
                counters.LastAiTypeName = ai.GetType().Name;
            }

            counters.LastBuilding = buildingId;
            counters.LastAssigned = alreadyAssigned;
            counters.LastAmount = amount;
            counters.LastMax = max;

            if (alreadyAssigned * 2 >= MaxVehiclesPerBuildingForReporting)
            {
                counters.AtOrAboveHalfCap++;
            }

            if (counters.Retargets % ReportEveryRetargets == 0)
            {
                Log.Verbose(
                    "[AIImprove] Sanitation idle-seek (" + material + "): " + counters.Retargets +
                    " retargets, " + counters.AtOrAboveHalfCap +
                    " of them to a building already at half the per-building cap or more; pool holds " +
                    poolSize + " buildings. Latest: building " + counters.LastBuilding + " (" +
                    counters.LastAiTypeName + "), " + counters.LastAssigned +
                    " vehicles already assigned, GetMaterialAmount amount=" + counters.LastAmount +
                    " max=" + counters.LastMax + ".");
            }
        }

        // Mirror of SanitationIdleSeekTracker's own const, kept local so the reporting threshold
        // does not force that const to become public. Update both together if it is ever tuned.
        private const int MaxVehiclesPerBuildingForReporting = 4;
    }
}
