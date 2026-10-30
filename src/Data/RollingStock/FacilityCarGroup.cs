using System.Collections.Generic;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Data.RollingStock
{
    /// <summary>
    /// Grouping of rolling stock cars associated with a facility (spotted on a physical track or inbound en route).
    /// </summary>
    public sealed class FacilityCarGroup
    {
        public string TrackName { get; set; } = string.Empty;
        public CarGroupRole Role { get; set; } = CarGroupRole.CommercialInbound;
        public List<SpottedCarInfo> Cars { get; } = new List<SpottedCarInfo>(8);

        public bool IsEnRoute => Role is CarGroupRole.EnRouteWaybill or CarGroupRole.EnRouteRepair;
        public bool IsInbound => Role is CarGroupRole.CommercialInbound or CarGroupRole.LocomotiveFueling;

        public void Clear()
        {
            TrackName = string.Empty;
            Role = CarGroupRole.CommercialInbound;
            Cars.Clear();
        }
    }
}
