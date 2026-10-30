using System.Collections.Generic;
using System.Linq;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.RollingStock;

namespace PrettyConvenientMenu.Data.Facilities
{
    /// <summary>
    /// Top-level snapshot data model representing the complete operational state of an industry facility.
    /// </summary>
    public sealed class IndustrySnapshot
    {
        public string IndustryId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public IndustryFacilityType FacilityType { get; set; } = IndustryFacilityType.CommercialFreight;
        public int CurrentTier { get; set; }
        public string TierDescription { get; set; } = string.Empty;
        public ProductionState State { get; set; } = ProductionState.Idle;
        public string StatusMessage { get; set; } = string.Empty;
        public string DiagnosticAlert { get; set; } = string.Empty;

        public List<BufferInfo> Buffers { get; } = new List<BufferInfo>(8);
        public List<SpurCarGroup> SpurGroups { get; } = new List<SpurCarGroup>(8);

        // Read-only computed queries over SpurGroups (guarantees zero drift)
        public IEnumerable<SpottedCarInfo> InboundCars =>
            SpurGroups.Where(g => g.IsInbound && !g.IsEnRoute).SelectMany(g => g.Cars);

        public IEnumerable<SpottedCarInfo> OutboundCars =>
            SpurGroups.Where(g => !g.IsInbound && !g.IsEnRoute).SelectMany(g => g.Cars);

        public int TotalCarCount => SpurGroups.Sum(g => g.Cars.Count);

        public void Clear()
        {
            IndustryId = string.Empty;
            DisplayName = string.Empty;
            FacilityType = IndustryFacilityType.CommercialFreight;
            CurrentTier = 0;
            TierDescription = string.Empty;
            State = ProductionState.Idle;
            StatusMessage = string.Empty;
            DiagnosticAlert = string.Empty;

            Buffers.Clear();
            SpurGroups.Clear();
        }

        public void Reset(Model.Ops.Industry industry, IndustryFacilityType facilityType)
        {
            Clear();
            IndustryId = industry.identifier;
            DisplayName = industry.DisplayName;
            FacilityType = facilityType;
        }
    }
}
