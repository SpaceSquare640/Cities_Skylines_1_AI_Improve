using ColossalFramework;

namespace AIImprove
{
    // "根據城市中的吞吐量動態調整城際火車...入城流量" (2026-08-14): reads the same
    // smoothed passenger-count average the vanilla Public Transport info panel graph is built
    // from - TransportManager.instance.m_passengers[transportType] is updated once per
    // TransportLine.SimulationStep across every line of that type (see TransportLine.cs's own
    // "this.m_passengers.Update(); ... .Add(ref this.m_passengers);" in the decompile), so this
    // is real, already-computed-by-vanilla city-wide ridership, not something this project needs
    // to derive itself. Pure data read, no patching.
    internal static class TransportThroughputTracker
    {
        public static uint GetAverageRidership(TransportInfo.TransportType type)
        {
            TransportPassengerData data = Singleton<TransportManager>.instance.m_passengers[(int)type];
            return data.m_residentPassengers.m_averageCount + data.m_touristPassengers.m_averageCount;
        }
    }
}
