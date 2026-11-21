using System;
using System.Collections.Generic;

namespace PrettyConvenientMenu.Data.SupplyFleet
{
    public enum WorkTrainMode
    {
        Passive = 0,
        Work = 1,
        Restock = 2
    }

    /// <summary>
    /// Serializable root state for the Supply &amp; MOW Fleet Manager.
    /// Persisted as a plain JSON string inside a registered KeyValueObject so it syncs in multiplayer
    /// and can be safely ignored if the mod is ever removed.
    /// </summary>
    public sealed class SupplyFleetData
    {
        public int Version { get; set; } = 1;

        /// <summary>
        /// Explicitly tracked supply fleet car IDs (Car.id).
        /// </summary>
        public List<string> TrackedCarIds { get; set; } = new List<string>();

        /// <summary>
        /// Active per-car Work Orders.
        /// </summary>
        public List<WorkOrder> ActiveOrders { get; set; } = new List<WorkOrder>();

        /// <summary>
        /// User-configured target stock levels keyed by "{facilityId}|{loadId}" in native load units.
        /// </summary>
        public Dictionary<string, float> TargetStockLevels { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// User-keyed planned track repair demand sinks.
        /// </summary>
        public List<PlannedRepairDemand> PlannedRepairs { get; set; } = new List<PlannedRepairDemand>();

        /// <summary>
        /// Optional Work Car assignments for active TrackConstructionMilestones, keyed by milestone identifier.
        /// </summary>
        public Dictionary<string, string> MilestoneWorkCarAssignments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Active Work Train operating mode per root Work Car ID (Work, Restock, or Passive).
        /// </summary>
        public Dictionary<string, WorkTrainMode> WorkTrainModes { get; set; } = new Dictionary<string, WorkTrainMode>(StringComparer.Ordinal);

        /// <summary>
        /// Root Work Car IDs that have Smart Restock enabled (supply-aware depot + seller fallback).
        /// </summary>
        public HashSet<string> WorkTrainSmartRestockIds { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        public static string MakeTargetKey(string facilityId, string loadId)
        {
            return $"{facilityId}|{loadId}";
        }
    }
}
