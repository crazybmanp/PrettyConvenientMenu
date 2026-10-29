using System;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Storage buffer for non-factory facility reserves (Engine terminals and MOW yards).
    /// Tracks uncommitted available reserves.
    /// </summary>
    public sealed class ReserveBuffer : BufferInfo
    {
        public float AvailableAmount => Math.Max(0f, CurrentAmount - TotalOutbound);

        public bool IsRepairParts { get; set; }
        public float ProgramDemand { get; set; }
        public float SpotDemand { get; set; }
        public float TotalDemand => ProgramDemand + SpotDemand;
        public float Deficit => Math.Max(0f, TotalDemand - CurrentAmount);
        public float ProjectedInventory => CurrentAmount - ProgramDemand + TotalInbound - SpotDemand;
    }
}
