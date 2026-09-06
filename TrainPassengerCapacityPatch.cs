namespace AIImprove
{
    // "城際列車客量增加" (2026-08-14), DISABLED 2026-08-14 at user request, and its capacity code
    // REMOVED 2026-09-06 under the standing instruction "全部都套用不可碰總載客量".
    //
    // What this used to do: multiply PassengerTrainAI.m_passengerCapacity by 2 on every
    // CreateVehicle, remembering each AI instance's original value so repeated calls recomputed
    // from a fixed baseline rather than compounding.
    //
    // WHY THE CODE IS GONE RATHER THAN JUST UNREGISTERED (12 - 開發準則, 準則 3 and 準則 11):
    // being unregistered is a property of one line in Patcher.cs, and this project already has a
    // category of "dormant but dangerous" code that is one accidental line away from doing harm.
    // The harm here is specific and was seen in early development: a minibus at 5000 seats, a
    // taxi at 500. The multiplier lands on whatever value is in the field, and that value is not
    // necessarily vanilla's - Advanced Vehicle Options had set a train to 15984, which this x2
    // turned into 31968. Deferring to AVO only covered the one other party we happened to know
    // about. Leaving the mutation in place, unregistered, would preserve exactly that risk for
    // whoever re-enables it later without reading this comment.
    //
    // IF FULLER TRAINS ARE WANTED, the supported shape is IntercityBusPreloadPatch: write
    // Vehicle.m_transferSize (已載客量 - how many are already aboard) as a percentage of the
    // vehicle's OWN real capacity. That cannot produce an absurd number, because its upper bound
    // is the vehicle's own capacity, and it composes with capacity mods like AVO instead of
    // overriding them. Ask before building it - this file is a record, not a stub waiting to be
    // filled in.
    //
    // The file is kept (not deleted) because the analysis above is the reason the decision holds.
    internal static class TrainPassengerCapacityPatch
    {
    }
}
