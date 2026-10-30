using System.Collections.Generic;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Data.RollingStock
{
    /// <summary>
    /// Grouping of spotted or en-route cars sharing the same physical track or waybill.
    /// </summary>
    public sealed class SpurCarGroup
    {
        public string TrackName { get; set; } = string.Empty;
        public SpurRole Role { get; set; } = SpurRole.CommercialInbound;
        public List<SpottedCarInfo> Cars { get; } = new List<SpottedCarInfo>(8);

        public bool IsEnRoute => Role is SpurRole.EnRouteWaybill or SpurRole.EnRouteRepair;
        public bool IsInbound => Role is SpurRole.CommercialInbound or SpurRole.LocomotiveFueling;

        public void Clear()
        {
            TrackName = string.Empty;
            Role = SpurRole.CommercialInbound;
            Cars.Clear();
        }
    }
}
