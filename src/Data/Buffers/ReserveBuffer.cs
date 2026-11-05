using System;
using Model.Ops.Definition;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Storage buffer for non-factory facility reserves (Engine terminals and MOW yards).
    /// Tracks uncommitted available reserves.
    /// </summary>
    public class ReserveBuffer : BufferInfo
    {
        public virtual float AvailableAmount => Math.Max(0f, CurrentAmount - TotalOutbound);

        public bool IsRepairParts { get; set; }
        public virtual float ProgramDemand { get; set; }
        public virtual float SpotDemand { get; set; }
        public float TotalDemand => ProgramDemand + SpotDemand;
        public float Deficit => Math.Max(0f, TotalDemand - CurrentAmount);
        public float ProjectedInventory => CurrentAmount - ProgramDemand + TotalInbound - SpotDemand;

        public ReserveBuffer() { }

        public ReserveBuffer(
            Load load,
            BufferRole role,
            float currentAmount,
            float maxCapacity,
            string? storagePrefix = null,
            string? qualifier = null)
        {
            CargoType = load.id;
            CargoDisplayName = load.description ?? load.id;
            Units = load.units;
            Role = role;
            CurrentAmount = currentAmount;
            MaxCapacity = maxCapacity;
            StoragePrefix = storagePrefix;
            Qualifier = qualifier;
        }
    }
}
