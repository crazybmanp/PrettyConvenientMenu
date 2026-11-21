using System;
using System.Collections.Generic;
using Game;
using Model;
using Model.Ops;
using Model.Ops.AutoDestinations;
using Model.Ops.Definition;
using Newtonsoft.Json;
using PrettyConvenientMenu.Data.Helpers;

namespace PrettyConvenientMenu.Data.SupplyFleet
{
    public enum WorkOrderDestinationKind
    {
        MowYard,
        EngineTerminal,
        WorkCar,
        PlannedRepair,
        PassiveWorkTrain
    }

    public enum WorkOrderRepeatMode
    {
        OneShot,
        Repeating
    }

    /// <summary>
    /// Candidate unassigned supply car evaluated for a Work Order dispatch.
    /// </summary>
    public sealed class EligibleCarCandidate
    {
        public Car Car { get; }
        public float Capacity { get; }
        public float CurrentQuantity { get; }
        public bool IsAlreadyLoaded { get; }
        public float DistanceMeters { get; }
        public string LocationName { get; }

        public EligibleCarCandidate(
            Car car,
            float capacity,
            float currentQuantity,
            bool isAlreadyLoaded,
            float distanceMeters,
            string locationName)
        {
            Car = car;
            Capacity = capacity;
            CurrentQuantity = currentQuantity;
            IsAlreadyLoaded = isAlreadyLoaded;
            DistanceMeters = distanceMeters;
            LocationName = locationName;
        }

        public float EffectiveQuantity => IsAlreadyLoaded ? CurrentQuantity : Capacity;
        public float DistanceMiles => DistanceMeters * 0.0006213712f;
    }

    /// <summary>
    /// Represents an explicit per-car supply Work Order managed by the Supply &amp; MOW Fleet Manager.
    /// Encapsulates its own creation, destination matching, and Railroader Waybill / Auto-Destination lifecycle.
    /// </summary>
    public sealed class WorkOrder
    {
        public string OrderId { get; set; } = Guid.NewGuid().ToString("N");
        public string CarId { get; set; } = string.Empty;
        public string LoadId { get; set; } = string.Empty;

        public string? SourceId { get; set; }
        public string SourceDisplayName { get; set; } = string.Empty;

        public string DestinationId { get; set; } = string.Empty;
        public string DestinationDisplayName { get; set; } = string.Empty;
        public string DestinationAreaName { get; set; } = string.Empty;

        public string? SinkId { get; set; }

        public WorkOrderDestinationKind DestinationKind { get; set; } = WorkOrderDestinationKind.MowYard;
        public WorkOrderRepeatMode RepeatMode { get; set; } = WorkOrderRepeatMode.OneShot;

        public bool SkipLoading { get; set; }
        public bool HasBeenLoaded { get; set; }
        public bool HasClearedEmptyAutoDest { get; set; }
        public float ExpectedQuantity { get; set; }
        public long CreatedUtcTicks { get; set; } = DateTime.UtcNow.Ticks;

        [JsonIgnore]
        public bool IsPassiveWorkTrain => DestinationKind == WorkOrderDestinationKind.PassiveWorkTrain;

        [JsonIgnore]
        public string ShortSummary
        {
            get
            {
                if (IsPassiveWorkTrain)
                {
                    Load? wtLoad = !string.IsNullOrEmpty(LoadId) ? CarLoadLibrary.GetLoad(LoadId) : null;
                    string wtPrefix = wtLoad != null
                        ? $"[{wtLoad.description}] "
                        : (!string.IsNullOrEmpty(LoadId) ? $"[{LoadId}] " : string.Empty);
                    return $"{wtPrefix}{DestinationDisplayName}";
                }

                Load? load = !string.IsNullOrEmpty(LoadId) ? CarLoadLibrary.GetLoad(LoadId) : null;
                string loadPrefix = load != null
                    ? $"[{load.description}] "
                    : (!string.IsNullOrEmpty(LoadId) ? $"[{LoadId}] " : string.Empty);

                return string.IsNullOrEmpty(SourceDisplayName) || SkipLoading
                    ? $"{loadPrefix}Direct -> {DestinationDisplayName}"
                    : $"{loadPrefix}{SourceDisplayName} -> {DestinationDisplayName}";
            }
        }

        public static WorkOrder CreatePassiveWorkTrain(Car car, Car workCar, string workCarAreaName, string? loadId = null)
        {
            if (car == null) throw new ArgumentNullException(nameof(car));
            if (workCar == null) throw new ArgumentNullException(nameof(workCar));

            return new WorkOrder
            {
                CarId = car.id,
                LoadId = loadId ?? string.Empty,
                SourceId = null,
                SourceDisplayName = string.Empty,
                DestinationId = CoupledToDestination.GetIdentifierForCar(workCar),
                DestinationDisplayName = $"Work Train {workCar.DisplayName}",
                DestinationAreaName = workCarAreaName,
                SinkId = null,
                DestinationKind = WorkOrderDestinationKind.PassiveWorkTrain,
                RepeatMode = WorkOrderRepeatMode.OneShot,
                SkipLoading = true,
                HasBeenLoaded = false,
                ExpectedQuantity = 0f
            };
        }

        public bool IsForComponentAndLoad(IndustryComponent component, Load load)
        {
            if (component == null || load == null)
            {
                return false;
            }

            return string.Equals(DestinationId, component.Identifier, StringComparison.OrdinalIgnoreCase)
                && string.Equals(LoadId, load.id, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the target unloader component on <paramref name="industry"/> if this Work Order is
        /// scheduled to deliver a commodity to that facility (excluding passive work train assignments).
        /// </summary>
        public IndustryComponent? FindInboundDestinationComponent(Industry? industry)
        {
            if (industry?.Components == null || IsPassiveWorkTrain)
            {
                return null;
            }

            if (DestinationKind != WorkOrderDestinationKind.MowYard &&
                DestinationKind != WorkOrderDestinationKind.EngineTerminal)
            {
                return null;
            }

            if (string.IsNullOrEmpty(DestinationId) || string.IsNullOrEmpty(LoadId))
            {
                return null;
            }

            foreach (IndustryComponent comp in industry.Components)
            {
                if (comp is (IndustryUnloader or StorageUnloader) &&
                    string.Equals(comp.Identifier, DestinationId, StringComparison.OrdinalIgnoreCase))
                {
                    return comp;
                }
            }

            return null;
        }

        public bool IsForSinkAndLoad(string sinkId, Load load)
        {
            if (string.IsNullOrEmpty(sinkId) || load == null)
            {
                return false;
            }

            return string.Equals(SinkId, sinkId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(LoadId, load.id, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if this is a passive work train assignment belonging to the same root Work Car
        /// hierarchy as <paramref name="assignedWorkCarId"/>.
        /// </summary>
        public bool IsPassiveForWorkCar(string? assignedWorkCarId, SupplyFleetStore? store)
        {
            if (!IsPassiveWorkTrain || string.IsNullOrEmpty(assignedWorkCarId))
            {
                return false;
            }

            if (!CoupledToDestination.TryGetCoupledToDestination(DestinationId, out string targetWcId, out Car targetWc))
            {
                return false;
            }

            string orderRootId = targetWc != null && store != null
                ? store.ResolveRootWorkCar(targetWc).id
                : targetWcId;

            string sinkRootId = assignedWorkCarId!;
            if (store != null && TrainController.Shared != null &&
                TrainController.Shared.TryGetCarForId(assignedWorkCarId, out Car assignedWc) && assignedWc != null)
            {
                sinkRootId = store.ResolveRootWorkCar(assignedWc).id;
            }

            return string.Equals(orderRootId, sinkRootId, StringComparison.Ordinal);
        }

        public static WorkOrder CreateForCandidate(
            EligibleCarCandidate candidate,
            SupplyCommodityBar destination,
            SupplyOriginInfo? source)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (!destination.DestinationPosition.HasValue)
            {
                throw new InvalidOperationException($"Destination '{destination.FacilityDisplayName}' has no valid OpsCarPosition.");
            }

            bool skipLoading = candidate.IsAlreadyLoaded || source == null;
            return new WorkOrder
            {
                CarId = candidate.Car.id,
                LoadId = destination.Load.id,
                SourceId = skipLoading ? null : source!.Position.Identifier,
                SourceDisplayName = skipLoading ? string.Empty : source!.DisplayName,
                DestinationId = destination.DestinationPosition.Value.Identifier,
                DestinationDisplayName = destination.FacilityDisplayName,
                DestinationAreaName = destination.AreaName,
                SinkId = destination.SinkId,
                DestinationKind = destination.DestinationKind,
                RepeatMode = WorkOrderRepeatMode.OneShot,
                SkipLoading = skipLoading,
                HasBeenLoaded = candidate.IsAlreadyLoaded,
                ExpectedQuantity = candidate.EffectiveQuantity
            };
        }

        public static WorkOrder? CreateDirectUnload(Car car, SupplyCommodityBar destination)
        {
            if (car == null || destination == null || !destination.DestinationPosition.HasValue)
            {
                return null;
            }

            (Load? load, float qty, _) = CarTrackingHelper.GetCarCargo(car, null);
            if (load == null || qty <= 0.001f)
            {
                return null;
            }

            return new WorkOrder
            {
                CarId = car.id,
                LoadId = load.id,
                SourceId = null,
                SourceDisplayName = string.Empty,
                DestinationId = destination.DestinationPosition.Value.Identifier,
                DestinationDisplayName = destination.FacilityDisplayName,
                DestinationAreaName = destination.AreaName,
                SinkId = destination.SinkId,
                DestinationKind = destination.DestinationKind,
                RepeatMode = WorkOrderRepeatMode.OneShot,
                SkipLoading = true,
                HasBeenLoaded = true,
                ExpectedQuantity = qty
            };
        }

        /// <summary>
        /// Applies Railroader's Waybill and WhenLoaded Auto-Destination for this Work Order.
        /// </summary>
        public void ApplyToCar(Car car, SupplyOriginInfo? source, SupplyCommodityBar destination)
        {
            if (car == null || destination == null || !destination.DestinationPosition.HasValue || OpsController.Shared == null)
            {
                return;
            }

            OpsCarPosition destPos = destination.DestinationPosition.Value;

            try
            {
                if (SkipLoading || source == null)
                {
                    car.SetAutoDestination(AutoDestinationType.Load, null, null);
                    car.SetAutoDestination(AutoDestinationType.Empty, null, null);
                    car.SetWaybill(new Waybill(TimeWeather.Now, null, destPos, 0, completed: false, null, 0));
                }
                else
                {
                    // StorageLoader (MOW Yards) requires waybill.Tag == load.id to know which commodity to load.
                    // Keep WhenEmpty pointing to the source while at the producer so Facility Monitor shows Ready
                    // instead of Misspotted after loading; TickOrderLifecycle clears WhenEmpty once the car departs.
                    string? sourceTag = source.Component is StorageLoader ? LoadId : null;
                    car.SetAutoDestination(AutoDestinationType.Empty, source.Position, null);
                    car.SetAutoDestination(AutoDestinationType.Load, destPos, null);
                    car.SetWaybill(new Waybill(TimeWeather.Now, null, source.Position, 0, completed: false, sourceTag, 0));
                }
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to apply waybill for {car.DisplayName} on WorkOrder {OrderId}.");
            }
        }

        /// <summary>
        /// Clears only the WhenEmpty Auto-Destination once a loaded One-Shot car leaves the source spur
        /// so it will not auto-return after unloading.
        /// </summary>
        public static void ClearEmptyAutoDestination(Car? car)
        {
            if (car == null || OpsController.Shared == null)
            {
                return;
            }

            try
            {
                car.SetAutoDestination(AutoDestinationType.Empty, null, null);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Warning($"Failed to clear WhenEmpty auto-dest for {car.DisplayName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears active Waybill and Auto-Destinations on a car when its Work Order completes or is cancelled.
        /// </summary>
        public static void ClearWaybillFromCar(Car? car)
        {
            if (car == null || OpsController.Shared == null)
            {
                return;
            }

            try
            {
                car.SetAutoDestination(AutoDestinationType.Load, null, null);
                car.SetAutoDestination(AutoDestinationType.Empty, null, null);
                car.SetWaybill(null);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Warning($"Failed to clear waybill/auto-dest for {car.DisplayName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Auto-detects the product Load.id a car should stock for Work Train Restock mode
        /// from its current cargo, active Waybill tag, or WhenEmpty auto-destination.
        /// </summary>
        public static string DetectCarProductId(Car? car, OpsController? ops)
        {
            if (car == null)
            {
                return string.Empty;
            }

            (Load? currentLoad, float qty, _) = CarTrackingHelper.GetCarCargo(car, null);
            if (currentLoad != null && qty > 0.001f)
            {
                return currentLoad.id;
            }

            if (car.Waybill.HasValue && !string.IsNullOrEmpty(car.Waybill.Value.Tag))
            {
                Load? wbLoad = CarLoadLibrary.GetLoad(car.Waybill.Value.Tag);
                if (wbLoad != null)
                {
                    return wbLoad.id;
                }
            }

            if (ops != null)
            {
                ResolvedAutoDestination? emptyDest = car.GetAutoDestination(AutoDestinationType.Empty, ops);
                if (emptyDest.HasValue)
                {
                    if (!string.IsNullOrEmpty(emptyDest.Value.Tag))
                    {
                        Load? tagLoad = CarLoadLibrary.GetLoad(emptyDest.Value.Tag);
                        if (tagLoad != null)
                        {
                            return tagLoad.id;
                        }
                    }

                    IndustryComponent? comp = ResolveComponentById(ops, emptyDest.Value.Position.Identifier);
                    if (comp is IndustryLoaderBase loader && loader.load != null)
                    {
                        return loader.load.id;
                    }
                    if (comp is IndustrySeller seller && !string.IsNullOrEmpty(seller.loadId))
                    {
                        return seller.loadId;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns all MOW StorageLoader loads compatible with every car in the given collection, sorted by description.
        /// </summary>
        public static List<Load> GetCompatibleLoads(IEnumerable<Car> cars)
        {
            List<Car> carList = new List<Car>();
            foreach (Car c in cars)
            {
                if (c != null) carList.Add(c);
            }

            List<Load> result = new List<Load>();
            if (carList.Count == 0)
            {
                return result;
            }

            HashSet<string> mowLoadIds = CollectMowStorageLoadIds();

            foreach (Load load in CarLoadLibrary.AllLoads)
            {
                if (load == null) continue;
                if (mowLoadIds.Count > 0 && !mowLoadIds.Contains(load.id)) continue;

                bool allCompatible = true;
                for (int i = 0; i < carList.Count; i++)
                {
                    if (!SupplyFleetStore.TryGetCarCapacityForLoad(carList[i], load, out float cap) || cap <= 0.001f)
                    {
                        allCompatible = false;
                        break;
                    }
                }
                if (allCompatible)
                {
                    result.Add(load);
                }
            }

            result.Sort((a, b) => string.Compare(a.description, b.description, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static HashSet<string> CollectMowStorageLoadIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            OpsController? ops = OpsController.Shared;
            if (ops == null || ops.Areas == null)
            {
                return ids;
            }

            foreach (Area area in ops.Areas)
            {
                if (area?.Industries == null) continue;
                foreach (Industry ind in area.Industries)
                {
                    if (ind?.Components == null) continue;
                    foreach (IndustryComponent comp in ind.Components)
                    {
                        if (comp is not StorageLoader loader || loader.loads == null) continue;
                        for (int i = 0; i < loader.loads.Count; i++)
                        {
                            Load? l = loader.loads[i]?.load;
                            if (l != null && !string.IsNullOrEmpty(l.id))
                            {
                                ids.Add(l.id);
                            }
                        }
                    }
                }
            }

            return ids;
        }

        /// <summary>
        /// Work Mode: waybills an assigned Work Train car back to Coupled to WorkCar.
        /// </summary>
        public static void ApplyWorkTrainWorkMode(Car? car, Car? workCar)
        {
            if (car == null || workCar == null || OpsController.Shared == null)
            {
                return;
            }

            try
            {
                OpsCarPosition workCarPos = CoupledToDestination.CreatePositionForCar(workCar);
                string targetId = workCarPos.Identifier;

                ResolvedAutoDestination? emptyDest = car.GetAutoDestination(AutoDestinationType.Empty, OpsController.Shared);
                if (emptyDest.HasValue)
                {
                    car.SetAutoDestination(AutoDestinationType.Empty, null, null);
                }

                ResolvedAutoDestination? loadDest = car.GetAutoDestination(AutoDestinationType.Load, OpsController.Shared);
                if (!loadDest.HasValue || !string.Equals(loadDest.Value.Position.Identifier, targetId, StringComparison.Ordinal))
                {
                    car.SetAutoDestination(AutoDestinationType.Load, workCarPos, null);
                }

                bool alreadyWaybilledToWorkCar = car.Waybill.HasValue &&
                    string.Equals(car.Waybill.Value.Destination.Identifier, targetId, StringComparison.Ordinal);

                if (!alreadyWaybilledToWorkCar)
                {
                    car.SetWaybill(new Waybill(TimeWeather.Now, null, workCarPos, 0, completed: false, null, 0));
                }
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Warning($"Failed to apply Work mode for {car.DisplayName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Restock Mode: waybills empty/partial Work Train cars to the nearest stocked MOW StorageLoader
        /// (or nearby IndustryLoader/IndustrySeller) for their assigned product, with When Loaded returning
        /// to Coupled to WorkCar. Loaded cars are waybilled back to Coupled to WorkCar.
        /// </summary>
        public static void ApplyWorkTrainRestockMode(Car? car, Car? workCar, IndustryComponent? loader, Load? load)
        {
            if (car == null || workCar == null || OpsController.Shared == null)
            {
                return;
            }

            try
            {
                OpsCarPosition workCarPos = CoupledToDestination.CreatePositionForCar(workCar);
                string workCarDestId = workCarPos.Identifier;

                if (load == null || loader == null)
                {
                    return;
                }

                (Load? curLoad, float curQty, float curCap) = CarTrackingHelper.GetCarCargo(car, null);
                bool isFull = curLoad != null &&
                              string.Equals(curLoad.id, load.id, StringComparison.OrdinalIgnoreCase) &&
                              curCap > 0.001f &&
                              curQty >= curCap * 0.99f;

                OpsCarPosition loaderPos = (OpsCarPosition)loader;
                string loaderDestId = loaderPos.Identifier;
                string? loaderTag = loader is StorageLoader ? load.id : null;

                ResolvedAutoDestination? emptyDest = car.GetAutoDestination(AutoDestinationType.Empty, OpsController.Shared);
                if (!emptyDest.HasValue ||
                    !string.Equals(emptyDest.Value.Position.Identifier, loaderDestId, StringComparison.Ordinal) ||
                    !string.Equals(emptyDest.Value.Tag ?? string.Empty, loaderTag ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    car.SetAutoDestination(AutoDestinationType.Empty, loaderPos, loaderTag);
                }

                ResolvedAutoDestination? loadDest = car.GetAutoDestination(AutoDestinationType.Load, OpsController.Shared);
                if (!loadDest.HasValue || !string.Equals(loadDest.Value.Position.Identifier, workCarDestId, StringComparison.Ordinal))
                {
                    car.SetAutoDestination(AutoDestinationType.Load, workCarPos, null);
                }

                if (isFull)
                {
                    bool waybilledToWorkCar = car.Waybill.HasValue &&
                        string.Equals(car.Waybill.Value.Destination.Identifier, workCarDestId, StringComparison.Ordinal);
                    if (!waybilledToWorkCar)
                    {
                        car.SetWaybill(new Waybill(TimeWeather.Now, null, workCarPos, 0, completed: false, null, 0));
                    }
                }
                else
                {
                    bool waybilledToLoader = car.Waybill.HasValue &&
                        string.Equals(car.Waybill.Value.Destination.Identifier, loaderDestId, StringComparison.Ordinal) &&
                        string.Equals(car.Waybill.Value.Tag ?? string.Empty, loaderTag ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    if (!waybilledToLoader)
                    {
                        car.SetWaybill(new Waybill(TimeWeather.Now, null, loaderPos, 0, completed: false, loaderTag, 0));
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Warning($"Failed to apply Restock mode for {car.DisplayName}: {ex.Message}");
            }
        }

        private static IndustryComponent? ResolveComponentById(OpsController ops, string? identifier)
        {
            if (ops == null || string.IsNullOrEmpty(identifier) || ops.Areas == null)
            {
                return null;
            }

            foreach (Area area in ops.Areas)
            {
                if (area?.Industries == null) continue;
                foreach (Industry ind in area.Industries)
                {
                    if (ind?.Components == null) continue;
                    foreach (IndustryComponent comp in ind.Components)
                    {
                        if (string.Equals(comp.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            return comp;
                        }
                    }
                }
            }

            return null;
        }
    }
}
