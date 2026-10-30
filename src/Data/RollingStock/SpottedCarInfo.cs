using System;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Helpers;

namespace PrettyConvenientMenu.Data.RollingStock
{
    /// <summary>
    /// Snapshot of a single rolling stock car spotted on an industry spur or en route.
    /// Tracks a car object end-to-end through spur evaluation.
    /// </summary>
    public sealed class SpottedCarInfo
    {
        public Car? Car { get; }
        public IOpsCar OpsCar { get; }

        public string CarId { get; set; } = string.Empty;
        public string ReportingMark { get; set; } = string.Empty;
        public string CarType { get; set; } = "Car";
        public int TrackIndex { get; set; }

        public string? CargoName { get; set; }
        public LoadUnits Units { get; set; } = LoadUnits.Pounds;

        public float CurrentAmount { get; set; }
        public float CapacityAmount { get; set; }
        public float CurrentPercent => CapacityAmount > 0f ? (CurrentAmount / CapacityAmount * 100f) : 0f;

        public float TransferRatePerMinute { get; set; }
        public TimeSpan? EstimatedTimeToCompletion { get; set; }

        public CarStatusState StatusState { get; set; } = CarStatusState.Unassigned;
        public string StatusDetail { get; set; } = string.Empty;
        public string? DestinationTrack { get; set; }
        public string? CustomLoadString { get; set; }

        /// <summary>
        /// True if this car has been evaluated and assigned an operational status by a component or passive check.
        /// </summary>
        public bool IsClassified => StatusState != CarStatusState.Unassigned;

        public bool IsActiveTransfer => StatusState == CarStatusState.ActiveTransfer;

        /// <summary>
        /// Creates a new tracked SpottedCarInfo for a car spotted on a spur track, initialized to Unassigned.
        /// </summary>
        public SpottedCarInfo(Car? car, IOpsCar opsCar, int trackIndex = 0, string? customLoadString = null)
        {
            Car = car;
            OpsCar = opsCar;
            TrackIndex = trackIndex;
            CustomLoadString = customLoadString;

            CarId = car?.id ?? opsCar.Id;
            ReportingMark = opsCar.DisplayName ?? car?.DisplayName ?? opsCar.Id ?? string.Empty;
            CarType = !string.IsNullOrEmpty(opsCar.CarType) ? opsCar.CarType : (car?.Definition?.CarType ?? "Car");

            (Load? cargo, float cQty, float cCap) = CarTrackingHelper.GetCarCargo(car, opsCar);
            CargoName = cargo?.description ?? (cQty > 0.001f ? "Cargo" : "Empty");
            Units = cargo?.units ?? LoadUnits.Pounds;
            CurrentAmount = cQty;
            CapacityAmount = cCap;
            StatusState = CarStatusState.Unassigned;
            StatusDetail = string.Empty;
        }

        /// <summary>
        /// Default constructor for mocks or specialized construction.
        /// </summary>
        public SpottedCarInfo()
        {
            OpsCar = null!;
        }

        // --- In-place Lifecycle State Mutations ---

        public void MarkTransfer(Load load, float currentAmount, float capacityAmount, float ratePerMin, TimeSpan? eta, string actionName)
        {
            CargoName = load.description ?? "Cargo";
            Units = load.units;
            CurrentAmount = currentAmount;
            CapacityAmount = capacityAmount;
            TransferRatePerMinute = ratePerMin;
            EstimatedTimeToCompletion = eta;
            StatusState = CarStatusState.ActiveTransfer;
            StatusDetail = actionName;
        }

        public void MarkQueued(Load load, float currentAmount, float capacityAmount, TimeSpan? eta, string detail = "Queued")
        {
            CargoName = load.description ?? "Cargo";
            Units = load.units;
            CurrentAmount = currentAmount;
            CapacityAmount = capacityAmount;
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = eta;
            StatusState = CarStatusState.Queued;
            StatusDetail = detail;
        }

        public void MarkBlocked(Load load, float currentAmount, float capacityAmount, string detail)
        {
            CargoName = load.description ?? "Cargo";
            Units = load.units;
            CurrentAmount = currentAmount;
            CapacityAmount = capacityAmount;
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Blocked;
            StatusDetail = detail;
        }

        public void MarkReady(Load? load = null, float? currentAmount = null, float? capacityAmount = null)
        {
            if (load != null)
            {
                CargoName = load.description ?? "Cargo";
                Units = load.units;
            }
            if (currentAmount.HasValue) CurrentAmount = currentAmount.Value;
            if (capacityAmount.HasValue) CapacityAmount = capacityAmount.Value;
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Completed;
            StatusDetail = "Ready";
        }

        public void MarkRepair(float partsDemand, CarStatusState statusState, string statusDetail, float condition)
        {
            CargoName = "Parts";
            Units = LoadUnits.Pounds;
            CurrentAmount = partsDemand;
            CapacityAmount = partsDemand;
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = statusState;
            StatusDetail = statusDetail;
            CustomLoadString = $"Cond: {condition * 100f:F0}%";
        }

        public void MarkMisspotted(string destinationName, Load? load = null)
        {
            if (load != null)
            {
                CargoName = load.description ?? "Cargo";
                Units = load.units;
            }
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.WrongDestination;
            StatusDetail = "Misspotted";
            DestinationTrack = destinationName;
        }

        public void MarkPassive(string statusDetail, Load? overrideLoad = null, float? currentAmount = null, float? capacityAmount = null)
        {
            if (overrideLoad != null)
            {
                CargoName = overrideLoad.description ?? (CurrentAmount > 0.001f ? "Material" : "Empty");
                Units = overrideLoad.units;
            }
            if (currentAmount.HasValue) CurrentAmount = currentAmount.Value;
            if (capacityAmount.HasValue) CapacityAmount = capacityAmount.Value;
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Idle;
            StatusDetail = statusDetail;
        }

        /// <summary>
        /// Instantiates a SpottedCarInfo for rolling stock discovered in transit on an active train.
        /// Used strictly by en-route traffic scanners; cars physically spotted on facility spurs
        /// are discovered and bulk-created upfront by SpottedCarClassifier.
        /// </summary>
        public static SpottedCarInfo CreateEnRoute(
            Car? car,
            IOpsCar? opsCar,
            Load? load,
            float currentAmount,
            float capacityAmount,
            string actionName = "En Route")
        {
            return new SpottedCarInfo
            {
                CarId = car?.id ?? opsCar?.Id ?? string.Empty,
                ReportingMark = opsCar?.DisplayName ?? car?.DisplayName ?? opsCar?.Id ?? string.Empty,
                CarType = opsCar?.CarType ?? car?.Definition?.CarType ?? "Car",
                CargoName = load?.description ?? (currentAmount > 0.001f ? "Cargo" : "Empty"),
                Units = load?.units ?? LoadUnits.Pounds,
                CurrentAmount = currentAmount,
                CapacityAmount = capacityAmount,
                TransferRatePerMinute = 0f,
                EstimatedTimeToCompletion = null,
                StatusState = CarStatusState.EnRoute,
                StatusDetail = actionName
            };
        }
    }
}
