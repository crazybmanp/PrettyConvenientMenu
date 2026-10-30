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
        public Car? Car { get; private set; }
        public IOpsCar OpsCar { get; private set; }

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

        public void SetLoad(Load load, float currentAmount, float capacityAmount)
        {
            CargoName = load.description ?? "Cargo";
            Units = load.units;
            CurrentAmount = currentAmount;
            CapacityAmount = capacityAmount > 0.001f ? capacityAmount : load.NominalQuantityPerCarLoad;
        }

        public void MarkTransfer(float ratePerMin, TimeSpan? eta, string actionName)
        {
            TransferRatePerMinute = ratePerMin;
            EstimatedTimeToCompletion = eta;
            StatusState = CarStatusState.ActiveTransfer;
            StatusDetail = actionName;
        }

        public void MarkQueued(TimeSpan? eta, string detail = "Queued")
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = eta;
            StatusState = CarStatusState.Queued;
            StatusDetail = detail;
        }

        public void MarkBlocked(string detail)
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Blocked;
            StatusDetail = detail;
        }

        public void MarkReady()
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Completed;
            StatusDetail = "Ready";
        }

        public void MarkServiced(float condition, bool hasWaybill = false)
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Serviced;
            StatusDetail = hasWaybill ? "Serviced (Waybilled)" : "Serviced (Ready)";
            CustomLoadString = $"{condition * 100f:F0}%";
        }

        public void MarkRepair(float partsDemand, CarStatusState statusState, string statusDetail, float condition, TimeSpan? eta = null)
        {
            CargoName = "Parts";
            Units = LoadUnits.Pounds;
            CurrentAmount = partsDemand;
            CapacityAmount = partsDemand;
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = eta;
            StatusState = statusState;
            StatusDetail = statusDetail;
            CustomLoadString = $"{condition * 100f:F0}%";
        }

        public void MarkMisspotted(string destinationName)
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.WrongDestination;
            StatusDetail = "Misspotted";
            DestinationTrack = destinationName;
        }

        public void MarkPassive(string statusDetail)
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.Idle;
            StatusDetail = statusDetail;
        }

        public void MarkEnRoute(string statusDetail = "En Route")
        {
            TransferRatePerMinute = 0f;
            EstimatedTimeToCompletion = null;
            StatusState = CarStatusState.EnRoute;
            StatusDetail = statusDetail;
        }
    }
}
