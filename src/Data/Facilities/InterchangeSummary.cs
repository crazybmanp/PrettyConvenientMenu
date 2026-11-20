using Game;
using Model.Ops;

namespace PrettyConvenientMenu.Data.Facilities
{
    /// <summary>
    /// Snapshot metadata for an Interchange facility: next service time, disabled flag,
    /// and cost totals for captive freight orders on-track vs en-route.
    /// </summary>
    public sealed class InterchangeSummary
    {
        public GameDateTime NextServiceTime { get; set; }
        public Interchange.NextServiceStyle ServiceStyle { get; set; }
        public bool IsDisabled { get; set; }

        public int OnTrackOrderCount { get; set; }
        public int OnTrackGoodsCost { get; set; }
        public int OnTrackHandlingCost { get; set; }
        public int OnTrackTotalCost => OnTrackGoodsCost + OnTrackHandlingCost;

        public int EnRouteOrderCount { get; set; }
        public int EnRouteTotalCost { get; set; }
    }
}
