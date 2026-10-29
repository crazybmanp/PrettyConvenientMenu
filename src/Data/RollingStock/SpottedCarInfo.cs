using System;
using Model.Definition.Data;

namespace PrettyConvenientMenu.Data.RollingStock
{
    /// <summary>
    /// Snapshot of a single rolling stock car spotted on an industry spur or en route.
    /// </summary>
    public sealed class SpottedCarInfo
    {
        public string? CarId { get; set; }
        public string ReportingMark { get; set; } = string.Empty;
        public string CarType { get; set; } = string.Empty;
        public string TrackName { get; set; } = string.Empty;
        public string? CargoName { get; set; }
        public LoadUnits Units { get; set; } = LoadUnits.Pounds;

        public float CurrentAmount { get; set; }
        public float CapacityAmount { get; set; }
        public float CurrentPercent => CapacityAmount > 0f ? (CurrentAmount / CapacityAmount * 100f) : 0f;

        public float TransferRatePerMinute { get; set; }
        public TimeSpan? EstimatedTimeToCompletion { get; set; }

        public CarStatusState StatusState { get; set; } = CarStatusState.Idle;
        public string StatusDetail { get; set; } = string.Empty;
        public string? DestinationTrack { get; set; }
        public string? CustomLoadString { get; set; }

        public bool IsActiveTransfer => StatusState == CarStatusState.ActiveTransfer;

        // Legacy compatibility properties
        public float CurrentWeightPounds
        {
            get => CurrentAmount;
            set => CurrentAmount = value;
        }

        public float CapacityWeightPounds
        {
            get => CapacityAmount;
            set => CapacityAmount = value;
        }

        public string StatusNote
        {
            get => !string.IsNullOrEmpty(StatusDetail) ? StatusDetail : StatusState.ToString();
            set => StatusDetail = value;
        }
    }
}
