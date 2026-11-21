using System;
using System.Collections.Generic;

namespace PrettyConvenientMenu.Data.SupplyFleet
{
    /// <summary>
    /// User-keyed planned track repair demand sink.
    /// Base-game staging anchors to either an MOW Work Car (native CoupledToDestination) or an MOW Yard.
    /// </summary>
    public sealed class PlannedRepairDemand
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; } = "Planned Track Repair";

        /// <summary>
        /// Optional base-game Work Car ID assigned as the destination for this repair project.
        /// </summary>
        public string? StagingWorkCarId { get; set; }

        /// <summary>
        /// Optional MOW Yard industry identifier used as the staging reference.
        /// </summary>
        public string? StagingMowYardId { get; set; }

        /// <summary>
        /// Material demands keyed by Load.id, stored in native game units (pounds for weight loads).
        /// </summary>
        public Dictionary<string, float> LoadDemands { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    }
}
