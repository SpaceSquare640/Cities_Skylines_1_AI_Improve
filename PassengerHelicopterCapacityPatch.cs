namespace AIImprove
{
    // "客運直升機客量增加", DISABLED and its capacity code REMOVED 2026-09-06 under the standing
    // instruction "全部都套用不可碰總載客量". Unlike the train and intercity bus versions this one
    // was REGISTERED AND ON BY DEFAULT, so every subscriber's passenger helicopters were running
    // at double capacity until this change.
    //
    // What this used to do: multiply PassengerHelicopterAI.m_passengerCapacity by a slider value
    // on every CreateVehicle, remembering each AI instance's original so repeated calls did not
    // compound, with a restore path added the same day for the prefab damage it caused (Medium
    // #5: m_passengerCapacity is on the shared VehicleInfo AI instance, so the boosted value
    // survived turning the feature off, and survived the mod being disabled, until the game was
    // restarted).
    //
    // WHY THE CODE IS GONE RATHER THAN JUST UNREGISTERED - see TrainPassengerCapacityPatch.cs for
    // the full reasoning. Short version: the multiplier lands on whatever is already in the
    // field, which is not necessarily vanilla's value, and the observed results include a minibus
    // at 5000 seats and a taxi at 500. Unregistered code is one line away from running again.
    //
    // IF FULLER HELICOPTERS ARE WANTED: the supported shape is IntercityBusPreloadPatch - write
    // Vehicle.m_transferSize as a percentage of the vehicle's own real capacity. Ask first.
    //
    // Its two settings keys are left in ModSettings so an existing AIImprove.cgs still loads
    // cleanly; nothing reads them any more.
    internal static class PassengerHelicopterCapacityPatch
    {
    }
}
