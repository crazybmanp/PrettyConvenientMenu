using System.Collections.Generic;
using System.Linq;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Data.Facilities
{
    /// <summary>
    /// Top-level snapshot data model representing the complete operational state of a facility.
    /// </summary>
    public sealed class FacilitySnapshot
    {
        public string IndustryId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public FacilityType FacilityType { get; set; } = FacilityType.CommercialFreight;
        public int CurrentTier { get; set; }
        public string TierDescription { get; set; } = string.Empty;
        public ProductionState State { get; set; } = ProductionState.Idle;
        public string StatusMessage { get; set; } = string.Empty;
        public string DiagnosticAlert { get; set; } = string.Empty;

        public List<BufferInfo> Buffers { get; } = new List<BufferInfo>(8);
        public List<FacilityCarGroup> CarGroups { get; } = new List<FacilityCarGroup>(8);

        // Read-only computed queries over CarGroups (guarantees zero drift)
        public IEnumerable<SpottedCarInfo> InboundCars =>
            CarGroups.Where(g => g.IsInbound && !g.IsEnRoute).SelectMany(g => g.Cars);

        public IEnumerable<SpottedCarInfo> OutboundCars =>
            CarGroups.Where(g => !g.IsInbound && !g.IsEnRoute).SelectMany(g => g.Cars);

        public int TotalCarCount => CarGroups.Sum(g => g.Cars.Count);

        public FacilityCarGroup GetOrAddCarGroup(string trackName, CarGroupRole role)
        {
            foreach (FacilityCarGroup group in CarGroups)
            {
                if (group.TrackName == trackName && group.Role == role)
                {
                    return group;
                }
            }

            FacilityCarGroup newGroup = new FacilityCarGroup
            {
                TrackName = trackName,
                Role = role
            };
            CarGroups.Add(newGroup);
            return newGroup;
        }

        public void Clear()
        {
            IndustryId = string.Empty;
            DisplayName = string.Empty;
            FacilityType = FacilityType.CommercialFreight;
            CurrentTier = 0;
            TierDescription = string.Empty;
            State = ProductionState.Idle;
            StatusMessage = string.Empty;
            DiagnosticAlert = string.Empty;

            Buffers.Clear();
            CarGroups.Clear();
        }

        public void Reset(Model.Ops.Industry industry, FacilityType facilityType)
        {
            Clear();
            IndustryId = industry.identifier;
            DisplayName = industry.DisplayName;
            FacilityType = facilityType;
        }
    }
}
