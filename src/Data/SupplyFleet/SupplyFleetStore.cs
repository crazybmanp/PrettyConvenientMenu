using System;
using System.Collections.Generic;
using System.Linq;
using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Model.Ops.AutoDestinations;
using Model.Ops.Definition;
using Newtonsoft.Json;
using PrettyConvenientMenu.Data.Helpers;
using UnityEngine;

namespace PrettyConvenientMenu.Data.SupplyFleet
{
    /// <summary>
    /// Owns per-savegame persistence, multiplayer replication, explicit fleet membership,
    /// Work Order dispatching &amp; lifecycle monitoring, and target stock levels for the Supply &amp; MOW Fleet Manager.
    /// </summary>
    public sealed class SupplyFleetStore : MonoBehaviour
    {
        public const string PropertyObjectId = "pcm.supplyFleet";
        private const string StateJsonKey = "stateJson";
        private const float LifecyclePollIntervalSeconds = 5.0f;
        private const float HeavyPollTickIntervalSeconds = 1.0f;
        private const float HeavyOrderCooldownSeconds = 30.0f;
        private const float IndustryLoaderProximityMeters = 100.0f;

        public static SupplyFleetStore? Shared { get; private set; }

        public event Action? OnStateChanged;

        private KeyValueObject? keyValueObject;
        private IDisposable? keyObserver;
        private SupplyFleetData data = new SupplyFleetData();
        private readonly HashSet<string> trackedCarSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkOrder> ordersByCarId = new Dictionary<string, WorkOrder>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> nextHeavyCheckByCarId = new Dictionary<string, float>(StringComparer.Ordinal);
        private bool suppressObserver;
        private float nextLifecyclePollTime;
        private float nextHeavyPollTime;

        public IReadOnlyCollection<string> TrackedCarIds => trackedCarSet;
        public IReadOnlyCollection<WorkOrder> ActiveOrders => ordersByCarId.Values;
        public IReadOnlyList<PlannedRepairDemand> PlannedRepairs => data.PlannedRepairs;
        internal KeyValueObject? StoreKeyValueObject => keyValueObject;
        internal const string StateKey = StateJsonKey;

        private void Awake()
        {
            Shared = this;
            InitializePropertyObject();
        }

        private void OnDestroy()
        {
            keyObserver?.Dispose();
            keyObserver = null;

            if (StateManager.Shared != null)
            {
                try
                {
                    StateManager.Shared.UnregisterPropertyObject(PropertyObjectId);
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Warning($"Failed to unregister '{PropertyObjectId}': {ex.Message}");
                }
            }

            if (Shared == this)
            {
                Shared = null;
            }
        }

        private void Update()
        {
            if (!StateManager.IsHost)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now >= nextLifecyclePollTime)
            {
                nextLifecyclePollTime = now + LifecyclePollIntervalSeconds;
                TickOrderLifecycle();
            }

            if (now >= nextHeavyPollTime)
            {
                nextHeavyPollTime = now + HeavyPollTickIntervalSeconds;
                TickStaggeredHeavyOrders(now);
            }
        }

        private void InitializePropertyObject()
        {
            try
            {
                keyValueObject = gameObject.AddComponent<KeyValueObject>();
                if (StateManager.Shared != null)
                {
                    StateManager.Shared.RegisterPropertyObject(
                        PropertyObjectId,
                        keyValueObject,
                        AuthorizationRequirement.MinimumLevelTrainmaster);
                }

                LoadFromKeyValueObject();

                keyObserver = keyValueObject.Observe(StateJsonKey, _ =>
                {
                    if (!suppressObserver)
                    {
                        LoadFromKeyValueObject();
                        OnStateChanged?.Invoke();
                    }
                }, callInitial: false);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to initialize SupplyFleetStore KeyValueObject.");
            }
        }

        private void LoadFromKeyValueObject()
        {
            string? json = keyValueObject?[StateJsonKey].StringValueOrDefault(null);
            if (string.IsNullOrWhiteSpace(json))
            {
                data = new SupplyFleetData();
            }
            else
            {
                try
                {
                    data = JsonConvert.DeserializeObject<SupplyFleetData>(json!) ?? new SupplyFleetData();
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Error(ex, "Failed to deserialize SupplyFleetData; resetting to empty state.");
                    data = new SupplyFleetData();
                }
            }

            RebuildIndices();
        }

        private void SaveToKeyValueObject()
        {
            data.TrackedCarIds = trackedCarSet.ToList();
            data.ActiveOrders = ordersByCarId.Values.ToList();

            if (keyValueObject != null)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(data, Formatting.None);
                    suppressObserver = true;
                    keyValueObject[StateJsonKey] = Value.String(json);
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Error(ex, "Failed to persist SupplyFleetData.");
                }
                finally
                {
                    suppressObserver = false;
                }
            }

            OnStateChanged?.Invoke();
        }

        private void RebuildIndices()
        {
            trackedCarSet.Clear();
            if (data.TrackedCarIds != null)
            {
                for (int i = 0; i < data.TrackedCarIds.Count; i++)
                {
                    string id = data.TrackedCarIds[i];
                    if (!string.IsNullOrEmpty(id))
                    {
                        trackedCarSet.Add(id);
                    }
                }
            }

            ordersByCarId.Clear();
            if (data.ActiveOrders != null)
            {
                for (int i = 0; i < data.ActiveOrders.Count; i++)
                {
                    WorkOrder order = data.ActiveOrders[i];
                    if (order != null && !string.IsNullOrEmpty(order.CarId))
                    {
                        ordersByCarId[order.CarId] = order;
                    }
                }
            }

            data.TargetStockLevels ??= new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            data.PlannedRepairs ??= new List<PlannedRepairDemand>();
            data.MilestoneWorkCarAssignments ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // =========================================================================
        // Fleet Membership (Explicit Static Add / Remove + Safe Pruning)
        // =========================================================================

        public bool IsTracked(Car? car)
        {
            return car != null && !string.IsNullOrEmpty(car.id) && trackedCarSet.Contains(car.id);
        }

        public int AddCars(IEnumerable<Car> cars)
        {
            int added = 0;
            foreach (Car car in cars)
            {
                if (car != null && !string.IsNullOrEmpty(car.id) && trackedCarSet.Add(car.id))
                {
                    added++;
                }
            }

            if (added > 0)
            {
                SaveToKeyValueObject();
            }

            return added;
        }

        public int AddCarsByNumberRange(string? reportingMark, int startNumber, int endNumber)
        {
            if (TrainController.Shared == null)
            {
                return 0;
            }

            if (startNumber > endNumber)
            {
                int tmp = startNumber;
                startNumber = endNumber;
                endNumber = tmp;
            }

            string normalizedMark = (reportingMark ?? string.Empty).Trim();
            List<Car> matches = new List<Car>();

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (!IsEligibleSupplyFleetCar(car))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(normalizedMark) &&
                    !string.Equals(car.Ident.ReportingMark, normalizedMark, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(car.Ident.RoadNumber, out int roadNum) &&
                    roadNum >= startNumber && roadNum <= endNumber)
                {
                    matches.Add(car);
                }
            }

            return AddCars(matches);
        }

        public int AddCarsByNumberList(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || TrainController.Shared == null)
            {
                return 0;
            }

            string[] tokens = input.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            List<Car> matches = new List<Car>();

            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                foreach (Car car in TrainController.Shared.Cars)
                {
                    if (!IsEligibleSupplyFleetCar(car))
                    {
                        continue;
                    }

                    string display = car.DisplayName ?? string.Empty;
                    string roadNum = car.Ident.RoadNumber ?? string.Empty;
                    if (string.Equals(display, token, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(roadNum, token, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(car);
                    }
                }
            }

            return AddCars(matches);
        }

        public bool RemoveCar(Car? car)
        {
            if (car == null || string.IsNullOrEmpty(car.id))
            {
                return false;
            }

            bool removed = trackedCarSet.Remove(car.id);
            if (ordersByCarId.Remove(car.id))
            {
                WorkOrder.ClearWaybillFromCar(car);
                removed = true;
            }

            if (removed)
            {
                SaveToKeyValueObject();
            }

            return removed;
        }

        /// <summary>
        /// Prunes cars that have been sold or deleted from the world while preserving off-map (IsInBardo) cars.
        /// </summary>
        public int PruneDeletedCars()
        {
            if (TrainController.Shared == null)
            {
                return 0;
            }

            List<string>? toRemove = null;
            foreach (string carId in trackedCarSet)
            {
                if (!TrainController.Shared.TryGetCarForId(carId, out Car car) || car == null)
                {
                    toRemove ??= new List<string>();
                    toRemove.Add(carId);
                }
            }

            if (toRemove == null || toRemove.Count == 0)
            {
                return 0;
            }

            foreach (string carId in toRemove)
            {
                trackedCarSet.Remove(carId);
                ordersByCarId.Remove(carId);
            }

            SaveToKeyValueObject();
            return toRemove.Count;
        }

        // =========================================================================
        // Work Orders, Candidate Selection & Dispatch
        // =========================================================================

        public WorkOrder? GetOrderForCar(Car? car)
        {
            if (car == null || string.IsNullOrEmpty(car.id))
            {
                return null;
            }

            return ordersByCarId.TryGetValue(car.id, out WorkOrder order) ? order : null;
        }

        public bool HasActiveOrder(Car? car)
        {
            return GetOrderForCar(car) != null;
        }

        /// <summary>
        /// Finds all unassigned tracked fleet cars eligible to carry the bar's commodity,
        /// sorted with already-loaded unassigned cars at the top, followed by closest empty cars.
        /// </summary>
        public List<EligibleCarCandidate> FindEligibleUnassignedCars(
            SupplyCommodityBar destinationBar,
            SupplyOriginInfo? chosenSource)
        {
            List<EligibleCarCandidate> result = new List<EligibleCarCandidate>();
            if (destinationBar?.Load == null || TrainController.Shared == null)
            {
                return result;
            }

            Load load = destinationBar.Load;
            Vector3? refPos = TryGetReferencePoint(chosenSource, destinationBar);

            foreach (string carId in trackedCarSet)
            {
                if (!TrainController.Shared.TryGetCarForId(carId, out Car car) || car == null)
                {
                    continue;
                }

                if (HasActiveOrder(car) || car.IsInBardo)
                {
                    continue;
                }

                if (!TryGetCarCapacityForLoad(car, load, out float maxCapacity))
                {
                    continue;
                }

                (Load? currentLoad, float currentQty, _) = CarTrackingHelper.GetCarCargo(car, null);
                bool hasCargo = currentLoad != null && currentQty > 0.001f;
                bool hasTargetLoad = hasCargo &&
                    string.Equals(currentLoad!.id, load.id, StringComparison.OrdinalIgnoreCase);

                if (hasCargo && !hasTargetLoad)
                {
                    continue;
                }

                // Only skip loading when the car is actually FULL; partially loaded cars stay on the loading leg
                bool isFullyLoadedWithTarget = hasTargetLoad && maxCapacity > 0.001f && currentQty >= maxCapacity * 0.99f;

                float dist = ComputeDistance(car, refPos);
                string locName = ResolveCarAreaName(car);
                result.Add(new EligibleCarCandidate(car, maxCapacity, currentQty, isFullyLoadedWithTarget, dist, locName));
            }

            result.Sort((a, b) =>
            {
                if (a.IsAlreadyLoaded != b.IsAlreadyLoaded)
                {
                    return a.IsAlreadyLoaded ? -1 : 1;
                }

                int distCmp = a.DistanceMeters.CompareTo(b.DistanceMeters);
                if (distCmp != 0)
                {
                    return distCmp;
                }

                int capCmp = b.EffectiveQuantity.CompareTo(a.EffectiveQuantity);
                if (capCmp != 0)
                {
                    return capCmp;
                }

                return string.Compare(a.Car.DisplayName, b.Car.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        public static HashSet<string> AutoSelectCandidatesForTarget(
            IReadOnlyList<EligibleCarCandidate> sortedCandidates,
            float targetQuantity)
        {
            HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
            if (sortedCandidates == null || sortedCandidates.Count == 0 || targetQuantity <= 0.001f)
            {
                return selected;
            }

            // Allow a small 0.5% rounding tolerance (e.g. 2990 lbs displayed as 1.5T satisfies a 1.5T / 3000 lb target)
            float threshold = targetQuantity - Mathf.Max(0.001f, targetQuantity * 0.005f);
            float accumulated = 0f;
            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                EligibleCarCandidate candidate = sortedCandidates[i];
                selected.Add(candidate.Car.id);
                accumulated += candidate.EffectiveQuantity;

                if (accumulated >= threshold)
                {
                    break;
                }
            }

            return selected;
        }

        public int DispatchOrders(
            IReadOnlyList<EligibleCarCandidate> selectedCandidates,
            SupplyCommodityBar destination,
            SupplyOriginInfo? source)
        {
            if (selectedCandidates == null || selectedCandidates.Count == 0 || destination == null || !destination.DestinationPosition.HasValue)
            {
                return 0;
            }

            int dispatched = 0;
            for (int i = 0; i < selectedCandidates.Count; i++)
            {
                EligibleCarCandidate candidate = selectedCandidates[i];
                if (!candidate.IsAlreadyLoaded && source == null)
                {
                    continue;
                }

                WorkOrder order = WorkOrder.CreateForCandidate(candidate, destination, source);
                order.ApplyToCar(candidate.Car, source, destination);
                trackedCarSet.Add(order.CarId);
                ordersByCarId[order.CarId] = order;
                dispatched++;
            }

            if (dispatched > 0)
            {
                SaveToKeyValueObject();
            }

            return dispatched;
        }

        public int DispatchDirectUnload(IReadOnlyList<SupplyCarInfo> loadedCars, SupplyCommodityBar destination)
        {
            if (loadedCars == null || loadedCars.Count == 0 || destination == null || !destination.DestinationPosition.HasValue)
            {
                return 0;
            }

            int dispatched = 0;
            for (int i = 0; i < loadedCars.Count; i++)
            {
                Car car = loadedCars[i].Car;
                WorkOrder? order = WorkOrder.CreateDirectUnload(car, destination);
                if (order == null)
                {
                    continue;
                }

                order.ApplyToCar(car, null, destination);
                trackedCarSet.Add(order.CarId);
                ordersByCarId[order.CarId] = order;
                dispatched++;
            }

            if (dispatched > 0)
            {
                SaveToKeyValueObject();
            }

            return dispatched;
        }

        /// <summary>
        /// Follows passive Work Train cross-links to the top/root Work Car (with cycle protection),
        /// so cross-linked work cars pool all of their assigned cars under the top work train.
        /// </summary>
        public Car ResolveRootWorkCar(Car workCar)
        {
            if (workCar == null || TrainController.Shared == null)
            {
                return workCar!;
            }

            Car current = workCar;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal) { current.id };

            while (ordersByCarId.TryGetValue(current.id, out WorkOrder order) &&
                   order.IsPassiveWorkTrain &&
                   CoupledToDestination.TryGetCoupledToDestination(order.DestinationId, out string parentId, out Car parentCar) &&
                   parentCar != null &&
                   visited.Add(parentId))
            {
                current = parentCar;
            }

            return current;
        }

        public WorkTrainMode GetWorkTrainMode(Car? workCar)
        {
            if (workCar == null || string.IsNullOrEmpty(workCar.id))
            {
                return WorkTrainMode.Passive;
            }

            Car root = ResolveRootWorkCar(workCar);
            if (data.WorkTrainModes != null &&
                data.WorkTrainModes.TryGetValue(root.id, out WorkTrainMode mode))
            {
                return mode;
            }

            return WorkTrainMode.Passive;
        }

        public int CountCarsAssignedToWorkTrain(Car? workCar)
        {
            if (workCar == null)
            {
                return 0;
            }

            Car root = ResolveRootWorkCar(workCar);
            string targetId = CoupledToDestination.GetIdentifierForCar(root);
            int count = 0;

            foreach (WorkOrder order in ordersByCarId.Values)
            {
                if (order.IsPassiveWorkTrain && string.Equals(order.DestinationId, targetId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        public bool GetWorkTrainSmartMode(Car? workCar)
        {
            if (workCar == null || string.IsNullOrEmpty(workCar.id))
            {
                return false;
            }

            Car root = ResolveRootWorkCar(workCar);
            return data.WorkTrainSmartRestockIds != null && data.WorkTrainSmartRestockIds.Contains(root.id);
        }

        public void SetWorkTrainSmartMode(Car? workCar, bool enabled)
        {
            if (workCar == null || string.IsNullOrEmpty(workCar.id))
            {
                return;
            }

            Car root = ResolveRootWorkCar(workCar);
            data.WorkTrainSmartRestockIds ??= new HashSet<string>(StringComparer.Ordinal);
            if (enabled)
            {
                data.WorkTrainSmartRestockIds.Add(root.id);
            }
            else
            {
                data.WorkTrainSmartRestockIds.Remove(root.id);
            }

            WorkTrainMode activeMode = GetWorkTrainMode(root);
            if (activeMode == WorkTrainMode.Restock)
            {
                ApplyModeToAllWorkTrainCars(root, activeMode, forceReevaluate: false);
            }

            SaveToKeyValueObject();
        }

        public void ReevaluateSmartRestock(Car? workCar)
        {
            if (workCar == null || string.IsNullOrEmpty(workCar.id))
            {
                return;
            }

            Car root = ResolveRootWorkCar(workCar);
            if (GetWorkTrainMode(root) != WorkTrainMode.Restock || !GetWorkTrainSmartMode(root))
            {
                return;
            }

            string targetId = CoupledToDestination.GetIdentifierForCar(root);
            List<(Car car, WorkOrder order)> targets = new List<(Car, WorkOrder)>();
            foreach (WorkOrder order in ordersByCarId.Values)
            {
                if (order.IsPassiveWorkTrain &&
                    string.Equals(order.DestinationId, targetId, StringComparison.Ordinal) &&
                    TrainController.Shared != null &&
                    TrainController.Shared.TryGetCarForId(order.CarId, out Car car) &&
                    car != null)
                {
                    targets.Add((car, order));
                    WorkOrder.ClearWaybillFromCar(car);
                }
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ApplyWorkTrainModeToCar(targets[i].car, targets[i].order, root, WorkTrainMode.Restock, forceReevaluate: true);
                ScheduleStaggeredWorkTrainCheck(targets[i].car.id);
            }

            SaveToKeyValueObject();
        }

        private void ApplyModeToAllWorkTrainCars(Car root, WorkTrainMode mode, bool forceReevaluate)
        {
            string targetId = CoupledToDestination.GetIdentifierForCar(root);
            foreach (WorkOrder order in ordersByCarId.Values)
            {
                if (order.IsPassiveWorkTrain &&
                    string.Equals(order.DestinationId, targetId, StringComparison.Ordinal) &&
                    TrainController.Shared != null &&
                    TrainController.Shared.TryGetCarForId(order.CarId, out Car car) &&
                    car != null)
                {
                    ApplyWorkTrainModeToCar(car, order, root, mode, forceReevaluate);
                    ScheduleStaggeredWorkTrainCheck(car.id);
                }
            }
        }

        public void SetWorkTrainMode(Car? workCar, WorkTrainMode mode)
        {
            if (workCar == null || string.IsNullOrEmpty(workCar.id))
            {
                return;
            }

            Car root = ResolveRootWorkCar(workCar);
            data.WorkTrainModes ??= new Dictionary<string, WorkTrainMode>(StringComparer.Ordinal);
            data.WorkTrainModes[root.id] = mode;

            if (mode != WorkTrainMode.Passive)
            {
                string targetId = CoupledToDestination.GetIdentifierForCar(root);
                foreach (WorkOrder order in ordersByCarId.Values)
                {
                    if (order.IsPassiveWorkTrain &&
                        string.Equals(order.DestinationId, targetId, StringComparison.Ordinal) &&
                        TrainController.Shared != null &&
                        TrainController.Shared.TryGetCarForId(order.CarId, out Car car) &&
                        car != null)
                    {
                        ApplyWorkTrainModeToCar(car, order, root, mode);
                        ScheduleStaggeredWorkTrainCheck(car.id);
                    }
                }
            }

            SaveToKeyValueObject();
        }

        public void SetWorkOrderProduct(Car? car, string? loadId)
        {
            if (car == null || string.IsNullOrEmpty(car.id))
            {
                return;
            }

            if (!ordersByCarId.TryGetValue(car.id, out WorkOrder order) || !order.IsPassiveWorkTrain)
            {
                return;
            }

            order.LoadId = loadId ?? string.Empty;
            if (CoupledToDestination.TryGetCoupledToDestination(order.DestinationId, out _, out Car workCar) && workCar != null)
            {
                WorkTrainMode mode = GetWorkTrainMode(workCar);
                if (mode != WorkTrainMode.Passive)
                {
                    ApplyWorkTrainModeToCar(car, order, workCar, mode);
                    ScheduleStaggeredWorkTrainCheck(car.id);
                }
            }

            SaveToKeyValueObject();
        }

        /// <summary>
        /// Assigns cars (including other Work Cars for cross-linking) to a Work Car / Work Train.
        /// </summary>
        public int AssignPassiveWorkTrain(
            IReadOnlyList<SupplyCarInfo> cars,
            Car workCar,
            string? overrideLoadId = null,
            bool autoDetectLoad = true)
        {
            if (cars == null || cars.Count == 0 || workCar == null)
            {
                return 0;
            }

            Car rootWorkCar = ResolveRootWorkCar(workCar);
            string workCarArea = ResolveCarAreaName(rootWorkCar);
            WorkTrainMode activeMode = GetWorkTrainMode(rootWorkCar);
            int assigned = 0;

            for (int i = 0; i < cars.Count; i++)
            {
                Car car = cars[i].Car;
                if (car == null ||
                    string.Equals(car.id, workCar.id, StringComparison.Ordinal) ||
                    string.Equals(car.id, rootWorkCar.id, StringComparison.Ordinal))
                {
                    continue;
                }

                string resolvedLoadId = overrideLoadId != null
                    ? overrideLoadId
                    : (autoDetectLoad ? WorkOrder.DetectCarProductId(car, OpsController.Shared) : string.Empty);

                WorkOrder order = WorkOrder.CreatePassiveWorkTrain(car, rootWorkCar, workCarArea, resolvedLoadId);
                trackedCarSet.Add(order.CarId);
                ordersByCarId[order.CarId] = order;

                if (activeMode != WorkTrainMode.Passive)
                {
                    ApplyWorkTrainModeToCar(car, order, rootWorkCar, activeMode);
                    ScheduleStaggeredWorkTrainCheck(car.id);
                }

                assigned++;
            }

            if (assigned > 0)
            {
                SaveToKeyValueObject();
            }

            return assigned;
        }

        private static float GetCarStaggerOffset(string carId)
        {
            return Math.Abs(carId.GetHashCode()) % (int)HeavyOrderCooldownSeconds;
        }

        private void ScheduleStaggeredWorkTrainCheck(string carId)
        {
            nextHeavyCheckByCarId[carId] = Time.unscaledTime + HeavyOrderCooldownSeconds + GetCarStaggerOffset(carId);
        }

        private void ApplyWorkTrainModeToCar(Car car, WorkOrder order, Car rootWorkCar, WorkTrainMode mode, bool forceReevaluate = false)
        {
            if (mode == WorkTrainMode.Work)
            {
                WorkOrder.ApplyWorkTrainWorkMode(car, rootWorkCar);
                return;
            }

            if (mode == WorkTrainMode.Restock)
            {
                Load? load = !string.IsNullOrEmpty(order.LoadId) ? CarLoadLibrary.GetLoad(order.LoadId) : null;
                if (load == null)
                {
                    return;
                }

                bool smartMode = GetWorkTrainSmartMode(rootWorkCar);
                if (smartMode && !forceReevaluate && HasRestockSourceAssigned(car, rootWorkCar))
                {
                    return;
                }

                IndustryComponent? loader = FindRestockLoaderForCar(car, load, smartMode);
                WorkOrder.ApplyWorkTrainRestockMode(car, rootWorkCar, loader, load);
            }
        }

        private static bool HasRestockSourceAssigned(Car car, Car rootWorkCar)
        {
            OpsController? ops = OpsController.Shared;
            if (ops == null)
            {
                return false;
            }

            ResolvedAutoDestination? emptyDest = car.GetAutoDestination(AutoDestinationType.Empty, ops);
            if (emptyDest.HasValue)
            {
                return true;
            }

            if (car.Waybill.HasValue)
            {
                string workCarDestId = CoupledToDestination.GetIdentifierForCar(rootWorkCar);
                if (!string.Equals(car.Waybill.Value.Destination.Identifier, workCarDestId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private IndustryComponent? FindRestockLoaderForCar(Car car, Load load, bool smartMode)
        {
            Vector3 carPos = car.GetCenterPosition();
            IndustryComponent? nearbyIndustryLoader = FindNearbyIndustryLoaderInArea(car, carPos, load);
            if (nearbyIndustryLoader != null)
            {
                return nearbyIndustryLoader;
            }

            if (!smartMode)
            {
                return FindNearestMowStorageLoader(carPos, load);
            }

            StorageLoader? smartDepot = FindNearestAvailableMowStorageLoader(car, carPos, load);
            if (smartDepot != null)
            {
                return smartDepot;
            }

            IndustryComponent? seller = FindNearestIndustryProducer(carPos, load);
            return seller ?? FindNearestMowStorageLoader(carPos, load);
        }

        private static IndustryComponent? FindNearbyIndustryLoaderInArea(Car car, Vector3 carPos, Load load)
        {
            OpsController? ops = OpsController.Shared;
            if (ops == null)
            {
                return null;
            }

            Area? localArea;
            try
            {
                localArea = ops.ClosestArea(car);
            }
            catch
            {
                return null;
            }

            if (localArea?.Industries == null)
            {
                return null;
            }

            IndustryComponent? best = null;
            float bestDist = IndustryLoaderProximityMeters;

            foreach (Industry ind in localArea.Industries)
            {
                if (ind == null || !ind.IsEnabledReachable || ind.Components == null) continue;
                foreach (IndustryComponent comp in ind.Components)
                {
                    if (comp is IndustryLoaderBase loader)
                    {
                        if (loader.load == null || !string.Equals(loader.load.id, load.id, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    else if (comp is IndustrySeller seller)
                    {
                        if (string.IsNullOrEmpty(seller.loadId) || !string.Equals(seller.loadId, load.id, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    float dist = Vector3.Distance(carPos, comp.CenterPoint);
                    if (dist <= bestDist)
                    {
                        bestDist = dist;
                        best = comp;
                    }
                }
            }

            return best;
        }

        private static StorageLoader? FindNearestMowStorageLoader(Vector3 referencePos, Load load)
        {
            OpsController? ops = OpsController.Shared;
            if (ops == null || ops.Areas == null || load == null)
            {
                return null;
            }

            StorageLoader? best = null;
            float bestDist = float.MaxValue;

            foreach (Area area in ops.Areas)
            {
                if (area?.Industries == null) continue;
                foreach (Industry ind in area.Industries)
                {
                    if (ind == null || !ind.IsEnabledReachable || ind.Components == null) continue;
                    foreach (IndustryComponent comp in ind.Components)
                    {
                        if (comp is not StorageLoader loader || !StorageLoaderSupportsLoad(loader, load)) continue;

                        float dist = Vector3.Distance(referencePos, loader.CenterPoint);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = loader;
                        }
                    }
                }
            }

            return best;
        }

        private StorageLoader? FindNearestAvailableMowStorageLoader(Car evaluatingCar, Vector3 referencePos, Load load)
        {
            OpsController? ops = OpsController.Shared;
            if (ops == null || ops.Areas == null || load == null)
            {
                return null;
            }

            StorageLoader? best = null;
            float bestDist = float.MaxValue;

            foreach (Area area in ops.Areas)
            {
                if (area?.Industries == null) continue;
                foreach (Industry ind in area.Industries)
                {
                    if (ind == null || !ind.IsEnabledReachable || ind.Components == null) continue;
                    foreach (IndustryComponent comp in ind.Components)
                    {
                        if (comp is not StorageLoader loader || !StorageLoaderSupportsLoad(loader, load)) continue;

                        float available = ComputeAvailableMowDepotSupply(evaluatingCar, ind, loader, load);
                        if (available <= 0.001f) continue;

                        float dist = Vector3.Distance(referencePos, loader.CenterPoint);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = loader;
                        }
                    }
                }
            }

            return best;
        }

        private float ComputeAvailableMowDepotSupply(Car evaluatingCar, Industry ind, StorageLoader loader, Load load)
        {
            float stored = ind.Storage != null ? ind.Storage.QuantityInStorage(load) : 0f;
            float spottedUnloading = 0f;
            float outbound = 0f;

            if (TrainController.Shared?.Cars == null)
            {
                return stored;
            }

            List<OpsCarPosition> unloaderPositions = new List<OpsCarPosition>();
            foreach (IndustryComponent c in ind.Components)
            {
                if (c is StorageUnloader or IndustryUnloader)
                {
                    unloaderPositions.Add((OpsCarPosition)c);
                }
            }

            foreach (Car other in TrainController.Shared.Cars)
            {
                if (other == null || string.Equals(other.id, evaluatingCar.id, StringComparison.Ordinal) || other.IsInBardo)
                {
                    continue;
                }

                (Load? otherLoad, float otherQty, _) = CarTrackingHelper.GetCarCargo(other, null);
                bool matchesLoad = otherLoad != null && string.Equals(otherLoad.id, load.id, StringComparison.OrdinalIgnoreCase);

                // Count only loaded cars physically spotted on this depot's unloader track(s) (exclude en-route cars)
                if (matchesLoad && otherQty > 0.001f && IsCarSpottedAtAnyPosition(other, unloaderPositions))
                {
                    spottedUnloading += otherQty;
                }

                // Outbound: cars waybilled to pick up this load from this StorageLoader (or pending Supply Fleet order)
                bool waybilledToLoader = other.Waybill.HasValue &&
                    string.Equals(other.Waybill.Value.Destination.Identifier, loader.Identifier, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(other.Waybill.Value.Tag) || string.Equals(other.Waybill.Value.Tag, load.id, StringComparison.OrdinalIgnoreCase));

                bool orderedFromLoader = !waybilledToLoader &&
                    ordersByCarId.TryGetValue(other.id, out WorkOrder otherOrder) &&
                    !otherOrder.IsPassiveWorkTrain &&
                    !otherOrder.HasBeenLoaded &&
                    string.Equals(otherOrder.SourceId, loader.Identifier, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(otherOrder.LoadId, load.id, StringComparison.OrdinalIgnoreCase);

                if (waybilledToLoader || orderedFromLoader)
                {
                    if (TryGetCarCapacityForLoad(other, load, out float cap))
                    {
                        float alreadyLoaded = matchesLoad ? otherQty : 0f;
                        outbound += Mathf.Max(0f, cap - alreadyLoaded);
                    }
                }
            }

            return stored + spottedUnloading - outbound;
        }

        private static bool IsCarSpottedAtAnyPosition(Car car, List<OpsCarPosition> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (IsCarSpottedAtPosition(car, positions[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static IndustryComponent? FindNearestIndustryProducer(Vector3 referencePos, Load load)
        {
            OpsController? ops = OpsController.Shared;
            if (ops == null || ops.Areas == null || load == null)
            {
                return null;
            }

            IndustryComponent? best = null;
            float bestDist = float.MaxValue;

            foreach (Area area in ops.Areas)
            {
                if (area?.Industries == null) continue;
                foreach (Industry ind in area.Industries)
                {
                    if (ind == null || !ind.IsEnabledReachable || ind.Components == null) continue;
                    foreach (IndustryComponent comp in ind.Components)
                    {
                        bool matches = comp is IndustryLoaderBase loader
                            ? (loader.load != null && string.Equals(loader.load.id, load.id, StringComparison.OrdinalIgnoreCase))
                            : (comp is IndustrySeller seller && !string.IsNullOrEmpty(seller.loadId) && string.Equals(seller.loadId, load.id, StringComparison.OrdinalIgnoreCase));

                        if (!matches) continue;

                        float dist = Vector3.Distance(referencePos, comp.CenterPoint);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = comp;
                        }
                    }
                }
            }

            return best;
        }

        private static bool StorageLoaderSupportsLoad(StorageLoader loader, Load load)
        {
            if (loader.loads == null) return false;
            for (int i = 0; i < loader.loads.Count; i++)
            {
                if (loader.loads[i]?.load != null &&
                    string.Equals(loader.loads[i].load.id, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public int CancelWorkOrders(IEnumerable<Car> cars)
        {
            int count = 0;
            foreach (Car car in cars)
            {
                if (car == null || string.IsNullOrEmpty(car.id))
                {
                    continue;
                }

                bool wasPassive = false;
                if (ordersByCarId.TryGetValue(car.id, out WorkOrder existing))
                {
                    wasPassive = existing.IsPassiveWorkTrain;
                    ordersByCarId.Remove(car.id);
                    count++;
                }

                if (!wasPassive)
                {
                    WorkOrder.ClearWaybillFromCar(car);
                }
            }

            if (count > 0)
            {
                SaveToKeyValueObject();
            }

            return count;
        }

        public void TickOrderLifecycle()
        {
            if (TrainController.Shared == null)
            {
                return;
            }

            bool changed = false;
            List<string>? completedCarIds = null;

            foreach (KeyValuePair<string, WorkOrder> kvp in ordersByCarId)
            {
                string carId = kvp.Key;
                WorkOrder order = kvp.Value;

                if (!TrainController.Shared.TryGetCarForId(carId, out Car car) || car == null)
                {
                    completedCarIds ??= new List<string>();
                    completedCarIds.Add(carId);
                    continue;
                }

                // Work Train orders are evaluated on the 1s staggered heavy tick;
                // here we only check whether the parent Work Car was sold/deleted.
                if (order.IsPassiveWorkTrain)
                {
                    if (!CoupledToDestination.TryGetCoupledToDestination(order.DestinationId, out _, out Car targetWorkCar) ||
                        targetWorkCar == null)
                    {
                        completedCarIds ??= new List<string>();
                        completedCarIds.Add(carId);
                        nextHeavyCheckByCarId.Remove(carId);
                    }
                    continue;
                }

                (Load? currentLoad, float currentQty, float currentCap) = CarTrackingHelper.GetCarCargo(car, null);
                bool hasOrderedLoad = currentLoad != null &&
                                      string.Equals(currentLoad.id, order.LoadId, StringComparison.OrdinalIgnoreCase) &&
                                      currentQty > 0.001f;
                bool isFullyLoaded = hasOrderedLoad && currentCap > 0.001f && currentQty >= currentCap * 0.99f;
                bool waybilledToDest = car.Waybill.HasValue &&
                                       string.Equals(car.Waybill.Value.Destination.Identifier, order.DestinationId, StringComparison.OrdinalIgnoreCase);

                if (!order.HasBeenLoaded && (isFullyLoaded || (hasOrderedLoad && waybilledToDest)))
                {
                    order.HasBeenLoaded = true;
                    changed = true;
                }

                // Clear WhenEmpty once a loaded One-Shot car leaves the producer spur (or arrives/goes off-map)
                if (order.HasBeenLoaded && !order.SkipLoading && !order.HasClearedEmptyAutoDest && OpsController.Shared != null)
                {
                    ResolvedAutoDestination? emptyDest = car.GetAutoDestination(AutoDestinationType.Empty, OpsController.Shared);
                    if (!emptyDest.HasValue)
                    {
                        order.HasClearedEmptyAutoDest = true;
                        changed = true;
                    }
                    else if (car.IsInBardo || !IsCarSpottedAtPosition(car, emptyDest.Value.Position))
                    {
                        WorkOrder.ClearEmptyAutoDestination(car);
                        order.HasClearedEmptyAutoDest = true;
                        changed = true;
                    }
                }

                if (order.HasBeenLoaded && currentQty <= 0.001f && !car.IsInBardo)
                {
                    completedCarIds ??= new List<string>();
                    completedCarIds.Add(carId);
                    WorkOrder.ClearWaybillFromCar(car);
                }
            }

            if (completedCarIds != null)
            {
                foreach (string carId in completedCarIds)
                {
                    ordersByCarId.Remove(carId);
                }
                changed = true;
            }

            if (PruneDeletedCars() > 0)
            {
                changed = false;
            }

            if (changed)
            {
                SaveToKeyValueObject();
            }
        }

        /// <summary>
        /// Runs every 1s on the host to evaluate heavy/area-scanning per-car orders (such as Work Train
        /// Restock/Work mode checks) across 30 one-second stagger buckets, so each car runs every 30s
        /// without frame spikes.
        /// </summary>
        private void TickStaggeredHeavyOrders(float now)
        {
            if (TrainController.Shared == null || ordersByCarId.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, WorkOrder> kvp in ordersByCarId)
            {
                string carId = kvp.Key;
                WorkOrder order = kvp.Value;

                if (!IsHeavyStaggeredOrder(order))
                {
                    continue;
                }

                if (!nextHeavyCheckByCarId.TryGetValue(carId, out float nextCheck))
                {
                    nextHeavyCheckByCarId[carId] = now + GetCarStaggerOffset(carId);
                    continue;
                }

                if (now < nextCheck)
                {
                    continue;
                }

                if (TrainController.Shared.TryGetCarForId(carId, out Car car) && car != null)
                {
                    EvaluateHeavyOrder(car, order);
                }

                nextHeavyCheckByCarId[carId] = now + HeavyOrderCooldownSeconds;
            }
        }

        private bool IsHeavyStaggeredOrder(WorkOrder order)
        {
            return order.IsPassiveWorkTrain;
        }

        private void EvaluateHeavyOrder(Car car, WorkOrder order)
        {
            if (!order.IsPassiveWorkTrain)
            {
                return;
            }

            if (!CoupledToDestination.TryGetCoupledToDestination(order.DestinationId, out _, out Car targetWorkCar) ||
                targetWorkCar == null)
            {
                return;
            }

            WorkTrainMode wtMode = GetWorkTrainMode(targetWorkCar);
            if (wtMode != WorkTrainMode.Passive)
            {
                ApplyWorkTrainModeToCar(car, order, targetWorkCar, wtMode);
            }
        }

        // =========================================================================
        // Target Stock Levels (Domain Overloads Public; Primitive Keys Private)
        // =========================================================================

        public bool TryGetTargetStock(Industry industry, Load load, out float quantity)
        {
            quantity = 0f;
            if (industry == null || load == null)
            {
                return false;
            }

            return TryGetTargetStockInternal(industry.identifier, load.id, out quantity);
        }

        public void SetTargetStock(SupplyCommodityBar bar, float quantity)
        {
            if (bar == null || bar.Load == null)
            {
                return;
            }

            SetTargetStockInternal(bar.FacilityId, bar.Load.id, quantity);
        }

        private bool TryGetTargetStockInternal(string facilityId, string loadId, out float quantity)
        {
            string key = SupplyFleetData.MakeTargetKey(facilityId, loadId);
            return data.TargetStockLevels.TryGetValue(key, out quantity);
        }

        private void SetTargetStockInternal(string facilityId, string loadId, float quantity)
        {
            string key = SupplyFleetData.MakeTargetKey(facilityId, loadId);
            data.TargetStockLevels[key] = Mathf.Max(0f, quantity);
            SaveToKeyValueObject();
        }

        // =========================================================================
        // Planned Repairs & Milestone Work Car Assignments
        // =========================================================================

        public void AddOrUpdatePlannedRepair(PlannedRepairDemand demand)
        {
            if (demand == null || string.IsNullOrEmpty(demand.Id))
            {
                return;
            }

            int idx = data.PlannedRepairs.FindIndex(p => string.Equals(p.Id, demand.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                data.PlannedRepairs[idx] = demand;
            }
            else
            {
                data.PlannedRepairs.Add(demand);
            }

            SaveToKeyValueObject();
        }

        public bool RemovePlannedRepair(SupplyProjectSink sink)
        {
            if (sink == null || sink.IsConstructionMilestone)
            {
                return false;
            }

            int removed = data.PlannedRepairs.RemoveAll(p => string.Equals(p.Id, sink.SinkId, StringComparison.Ordinal));
            if (removed > 0)
            {
                SaveToKeyValueObject();
                return true;
            }

            return false;
        }

        public Car? GetAssignedWorkCar(string milestoneSinkId)
        {
            if (string.IsNullOrEmpty(milestoneSinkId) || TrainController.Shared == null)
            {
                return null;
            }

            if (data.MilestoneWorkCarAssignments.TryGetValue(milestoneSinkId, out string carId) &&
                TrainController.Shared.TryGetCarForId(carId, out Car car))
            {
                return car;
            }

            return null;
        }

        public void SetAssignedWorkCar(SupplyProjectSink sink, Car? workCar)
        {
            if (sink == null || string.IsNullOrEmpty(sink.SinkId))
            {
                return;
            }

            if (workCar == null)
            {
                data.MilestoneWorkCarAssignments.Remove(sink.SinkId);
            }
            else
            {
                data.MilestoneWorkCarAssignments[sink.SinkId] = workCar.id;
            }

            SaveToKeyValueObject();
        }

        // =========================================================================
        // Internal Helpers
        // =========================================================================

        public static string ResolveCarAreaName(Car car)
        {
            if (car == null) return "Unknown";
            if (car.IsInBardo) return "Off-Map";

            try
            {
                if (OpsController.Shared != null)
                {
                    Area area = OpsController.Shared.ClosestArea(car);
                    if (area != null && !string.IsNullOrEmpty(area.name))
                    {
                        return area.name;
                    }
                }
            }
            catch
            {
                // Ignore transient resolution errors
            }

            return "On Line";
        }

        public static bool IsEligibleSupplyFleetCar(Car? car)
        {
            if (car == null || !car.IsOwnedByPlayer)
            {
                return false;
            }

            CarType ct = car.CarType;
            if (ct.IsLocomotive || ct.IsTender || ct.IsCoach || ct.IsBaggage || ct.IsCaboose)
            {
                return false;
            }

            // Exclude log skeleton cars (FL) which only haul timber/logs, not MOW or locomotive supplies
            string code = ct.ToString();
            if (code.StartsWith("FL", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ct.IsFreight || ct.IsMaintenanceOfWay;
        }

        public static bool CanCarCarryLoad(Car? car, Load? load)
        {
            return TryGetCarCapacityForLoad(car, load, out _);
        }

        public static bool TryGetCarCapacityForLoad(Car? car, Load? load, out float maxCapacity)
        {
            maxCapacity = 0f;
            if (car == null || load == null || car.Definition == null || car.Definition.LoadSlots == null)
            {
                return false;
            }

            // Work Cars (MW*) are automatically treated as Work Trains and excluded from cargo order pools
            if (!IsEligibleSupplyFleetCar(car) || car.CarType.IsMaintenanceOfWay)
            {
                return false;
            }

            if (load.carTypeFilter != null && !load.carTypeFilter.MatchesAll && !load.carTypeFilter.Matches(car.CarType))
            {
                return false;
            }

            float total = 0f;
            for (int i = 0; i < car.Definition.LoadSlots.Count; i++)
            {
                Model.Definition.Data.LoadSlot slot = car.Definition.LoadSlots[i];
                if (slot == null) continue;

                float cap = slot.CalculateMaximumCapacity(load, car);
                if (cap > 0.001f)
                {
                    total += cap;
                }
            }

            if (total <= 0.001f)
            {
                return false;
            }

            maxCapacity = total;
            return true;
        }

        private static bool IsCarSpottedAtPosition(Car car, OpsCarPosition position)
        {
            if (car == null || OpsController.Shared == null)
            {
                return false;
            }

            try
            {
                IEnumerable<Car>? carsOnTrack = OpsController.Shared.CarsAtPosition(position);
                if (carsOnTrack == null)
                {
                    return false;
                }

                foreach (Car c in carsOnTrack)
                {
                    if (c == car || (c != null && string.Equals(c.id, car.id, StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore transient lookup errors
            }

            return false;
        }

        private static Vector3? TryGetReferencePoint(SupplyOriginInfo? source, SupplyCommodityBar destinationBar)
        {
            try
            {
                if (source != null) return source.Position.GetCenter();
                if (destinationBar.DestinationPosition.HasValue) return destinationBar.DestinationPosition.Value.GetCenter();
            }
            catch
            {
                // Ignore
            }

            return null;
        }

        private static float ComputeDistance(Car car, Vector3? refPos)
        {
            if (!refPos.HasValue) return 0f;
            try
            {
                return Vector3.Distance(car.GetCenterPosition(), refPos.Value);
            }
            catch
            {
                return float.MaxValue;
            }
        }
    }
}
