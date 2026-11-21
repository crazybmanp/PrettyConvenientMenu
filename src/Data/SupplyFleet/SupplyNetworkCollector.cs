using System;
using System.Collections.Generic;
using System.Linq;
using Game.Mow;
using Game.Progression;
using Helpers;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using Track;
using UnityEngine;

namespace PrettyConvenientMenu.Data.SupplyFleet
{
    /// <summary>
    /// Populates a <see cref="SupplyNetworkSnapshot"/> from live Railroader world state
    /// and <see cref="SupplyFleetStore"/>.
    /// </summary>
    public sealed class SupplyNetworkCollector
    {
        private static readonly HashSet<string> MowLoadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ballast", "ties", "rail", "rails", "concrete", "explosives",
            "steel-bridge-beams", "bridge-beams", "wooden-bridge-timbers", "bridge-timbers",
            "mow-ballast", "mow-ties", "mow-rail"
        };

        private static readonly HashSet<string> FuelLoadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "coal", "diesel-fuel", "diesel", "repair-parts", "parts"
        };

        private MilePost[]? cachedMilePosts;
        private TrackConstructionMilestone[]? cachedMilestones;

        private MilePost[] GetCachedMilePosts()
        {
            if (cachedMilePosts == null || cachedMilePosts.Length == 0 || cachedMilePosts[0] == null)
            {
                cachedMilePosts = UnityEngine.Object.FindObjectsByType<MilePost>(FindObjectsSortMode.None);
            }
            return cachedMilePosts ?? Array.Empty<MilePost>();
        }

        private TrackConstructionMilestone[] GetCachedMilestones()
        {
            if (cachedMilestones == null || cachedMilestones.Length == 0 || cachedMilestones[0] == null)
            {
                cachedMilestones = UnityEngine.Object.FindObjectsByType<TrackConstructionMilestone>(FindObjectsSortMode.None);
            }
            return cachedMilestones ?? Array.Empty<TrackConstructionMilestone>();
        }

        public void Populate(SupplyFleetStore store, SupplyNetworkSnapshot snapshot)
        {
            snapshot.Clear();
            if (store == null || OpsController.Shared == null)
            {
                return;
            }

            // 1. Discover available Work Cars
            if (TrainController.Shared != null)
            {
                foreach (Car car in TrainController.Shared.Cars)
                {
                    if (car != null && car.IsOwnedByPlayer && IsWorkCar(car))
                    {
                        snapshot.AvailableWorkCars.Add(car);
                    }
                }
            }

            // 2. Build tracked fleet car infos
            Dictionary<string, SupplyCarInfo> fleetById = new Dictionary<string, SupplyCarInfo>(StringComparer.Ordinal);
            if (TrainController.Shared != null)
            {
                foreach (string carId in store.TrackedCarIds)
                {
                    if (!TrainController.Shared.TryGetCarForId(carId, out Car car) || car == null)
                    {
                        continue;
                    }

                    SupplyCarInfo info = BuildCarInfo(car, store, snapshot);
                    snapshot.FleetCars.Add(info);
                    fleetById[carId] = info;
                }
            }

            snapshot.FleetCars.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            // 3. Scan all industries for MOW Origins, MOW Yards, Fuel Origins, and Engine Service / Repair Sinks
            Industry[]? allIndustries = OpsController.Shared.AllIndustries;
            if (allIndustries != null)
            {
                foreach (Industry industry in allIndustries)
                {
                    if (industry == null || industry.Components == null || !industry.IsEnabledReachable)
                    {
                        continue;
                    }

                    string areaName = ResolveIndustryAreaName(industry);
                    Color areaColor = snapshot.ResolveAreaColor(areaName);

                    CollectOriginsFromIndustry(industry, areaName, snapshot);

                    FacilityType facilityType = FacilityClassifier.Classify(industry);
                    if (facilityType == FacilityType.MaintenanceOfWay)
                    {
                        BuildMowYardNode(industry, areaName, areaColor, store, snapshot, fleetById);
                    }
                    else if (facilityType == FacilityType.EngineService)
                    {
                        BuildFuelSinkNode(industry, areaName, areaColor, store, snapshot, fleetById);
                    }
                }
            }

            // 4. Build MOW Sinks (Active TrackConstructionMilestones + User Planned Repairs)
            BuildMilestoneSinks(store, snapshot);
            BuildPlannedRepairSinks(store, snapshot);

            // 5. Route active & spotted fleet cars onto their nodes
            snapshot.AttachActiveCarsToNodes();

            // 6. Detect suggested jobs for unassigned supply cars
            snapshot.EvaluateSuggestedJobs();

            // 7. Evaluate per-car spot & blocked statuses (cached per spur)
            EvaluateCarStatuses(snapshot);

            // 8. Organize tracked cars into physically coupled strings ordered by nearest MilePost
            BuildCoupledStrings(snapshot, fleetById);
        }

        private void BuildCoupledStrings(SupplyNetworkSnapshot snapshot, Dictionary<string, SupplyCarInfo> fleetById)
        {
            if (snapshot.FleetCars.Count == 0)
            {
                return;
            }

            MilePost[] milePosts = GetCachedMilePosts();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < snapshot.FleetCars.Count; i++)
            {
                SupplyCarInfo seed = snapshot.FleetCars[i];
                if (visited.Contains(seed.CarId))
                {
                    continue;
                }

                CoupledCarString cut = new CoupledCarString
                {
                    AreaName = seed.LocationArea,
                    IsOffMap = seed.IsOffMap
                };

                if (seed.IsOffMap)
                {
                    // Group off-map cars individually or by location
                    visited.Add(seed.CarId);
                    cut.Cars.Add(seed);
                }
                else
                {
                    // Walk the physically coupled consist in order and pick out all tracked supply cars
                    try
                    {
                        foreach (Car coupled in seed.Car.EnumerateCoupled())
                        {
                            if (coupled != null && fleetById.TryGetValue(coupled.id, out SupplyCarInfo info) && visited.Add(info.CarId))
                            {
                                cut.Cars.Add(info);
                            }
                        }
                    }
                    catch
                    {
                        if (visited.Add(seed.CarId))
                        {
                            cut.Cars.Add(seed);
                        }
                    }

                    if (cut.Cars.Count == 0 && visited.Add(seed.CarId))
                    {
                        cut.Cars.Add(seed);
                    }

                    // Find nearest MilePost to the lead car of this string (converting car GamePos -> WorldPos)
                    if (milePosts != null && milePosts.Length > 0)
                    {
                        try
                        {
                            Vector3 leadWorldPos = cut.Cars[0].Car.GetCenterPosition().GameToWorld();
                            MilePost? nearest = null;
                            float bestSqr = float.MaxValue;
                            for (int m = 0; m < milePosts.Length; m++)
                            {
                                MilePost mp = milePosts[m];
                                if (mp == null) continue;
                                float sqr = (mp.transform.position - leadWorldPos).sqrMagnitude;
                                if (sqr < bestSqr)
                                {
                                    bestSqr = sqr;
                                    nearest = mp;
                                }
                            }

                            if (nearest != null)
                            {
                                cut.HasMilePost = true;
                                cut.NearestMileage = nearest.mileage;
                                string prefix = string.IsNullOrEmpty(nearest.prefix) ? "MP" : nearest.prefix;
                                cut.MilePostLabel = $"{prefix}{nearest.mileage:0.#}";
                            }
                        }
                        catch
                        {
                            // Ignore transient position lookup errors
                        }
                    }
                }

                snapshot.CoupledStrings.Add(cut);
            }

            // Order strings: on-map strings sorted ascending by nearest MilePost, then off-map strings at the end
            snapshot.CoupledStrings.Sort((a, b) =>
            {
                if (a.IsOffMap != b.IsOffMap) return a.IsOffMap ? 1 : -1;
                if (a.HasMilePost != b.HasMilePost) return a.HasMilePost ? -1 : 1;
                int mpCmp = a.NearestMileage.CompareTo(b.NearestMileage);
                if (mpCmp != 0) return mpCmp;
                return string.Compare(a.AreaName, b.AreaName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void CollectOriginsFromIndustry(Industry industry, string areaName, SupplyNetworkSnapshot snapshot)
        {
            foreach (IndustryComponent comp in industry.Components)
            {
                if (comp == null || !comp.IsVisible || !comp.IsProgressionEnabled)
                {
                    continue;
                }

                if (comp is InterchangedIndustryLoader icLoader && !string.IsNullOrEmpty(icLoader.loadId))
                {
                    Load? load = CarLoadLibrary.GetLoad(icLoader.loadId);
                    if (load == null) continue;

                    SupplyOriginInfo origin = new SupplyOriginInfo
                    {
                        Identifier = icLoader.Identifier,
                        DisplayName = industry.DisplayName ?? icLoader.DisplayName ?? string.Empty,
                        KindLabel = "Interchange",
                        AreaName = areaName,
                        IsOffMapSource = true,
                        Industry = industry,
                        Component = icLoader,
                        Position = (OpsCarPosition)icLoader,
                        Load = load
                    };

                    if (IsMowLoad(load)) snapshot.MowOrigins.Add(origin);
                    if (IsFuelOrRepairLoad(load)) snapshot.FuelOrigins.Add(origin);
                }
                else if (comp is IndustrySeller seller && !string.IsNullOrEmpty(seller.loadId))
                {
                    Load? load = CarLoadLibrary.GetLoad(seller.loadId);
                    if (load == null) continue;

                    float curStock = industry.Storage != null ? industry.Storage.QuantityInStorage(load) : 0f;
                    SupplyOriginInfo origin = new SupplyOriginInfo
                    {
                        Identifier = seller.Identifier,
                        DisplayName = industry.DisplayName ?? string.Empty,
                        KindLabel = "Industry Seller",
                        AreaName = areaName,
                        IsOffMapSource = false,
                        Industry = industry,
                        Component = seller,
                        Position = (OpsCarPosition)seller,
                        Load = load,
                        CurrentBuffer = curStock
                    };

                    if (IsMowLoad(load)) snapshot.MowOrigins.Add(origin);
                    if (IsFuelOrRepairLoad(load)) snapshot.FuelOrigins.Add(origin);
                }
            }
        }

        private static SupplyCarInfo BuildCarInfo(Car car, SupplyFleetStore store, SupplyNetworkSnapshot snapshot)
        {
            (Load? load, float qty, float cap) = CarTrackingHelper.GetCarCargo(car, null);
            if (cap <= 0.001f && car.Definition?.LoadSlots != null && car.Definition.LoadSlots.Count > 0)
            {
                cap = car.Definition.LoadSlots[0].MaximumCapacity;
            }

            WorkOrder? order = store.GetOrderForCar(car);
            string locArea = SupplyFleetStore.ResolveCarAreaName(car);
            string destName;
            string destArea;

            if (order != null)
            {
                if (order.IsPassiveWorkTrain &&
                    CoupledToDestination.TryGetCoupledToDestination(order.DestinationId, out _, out Car targetWc) &&
                    targetWc != null)
                {
                    // Pool cross-linked work trains under the top/root Work Car
                    Car rootWc = store.ResolveRootWorkCar(targetWc);
                    destName = $"Work Train {rootWc.DisplayName}";
                    destArea = SupplyFleetStore.ResolveCarAreaName(rootWc);
                }
                else
                {
                    destName = order.DestinationDisplayName;
                    destArea = !string.IsNullOrEmpty(order.DestinationAreaName) ? order.DestinationAreaName : order.DestinationDisplayName;
                }
            }
            else if (car.CarType.IsMaintenanceOfWay)
            {
                // Work Cars automatically have Work Train status
                Car rootWc = store.ResolveRootWorkCar(car);
                destName = $"Work Train {rootWc.DisplayName}";
                destArea = SupplyFleetStore.ResolveCarAreaName(rootWc);
            }
            else
            {
                destName = "Unassigned";
                destArea = "Unassigned";
            }

            SupplyCarInfo carInfo = new SupplyCarInfo
            {
                Car = car,
                CarId = car.id,
                DisplayName = car.DisplayName ?? car.id,
                Shape = ClassifyCarShape(car),
                CurrentLoad = load,
                CurrentQuantity = qty,
                MaxCapacity = cap,
                Units = load != null ? load.units : Model.Definition.Data.LoadUnits.Pounds,
                IsOffMap = car.IsInBardo,
                LocationArea = locArea,
                DestinationName = destName,
                DestinationArea = destArea,
                DestinationColor = snapshot.ResolveAreaColor(destArea),
                ActiveOrder = order
            };

            if (!car.IsInBardo)
            {
                try
                {
                    foreach (Car coupled in car.EnumerateCoupled())
                    {
                        if (coupled == null || coupled == car) continue;
                        if (coupled.IsLocomotive)
                        {
                            carInfo.CoupledLocomotives.Add(coupled.DisplayName ?? coupled.id);
                        }
                        else if (coupled.CarType.IsMaintenanceOfWay)
                        {
                            carInfo.CoupledWorkCars.Add(coupled.DisplayName ?? coupled.id);
                        }
                    }
                }
                catch
                {
                    // Ignore transient consist enumeration errors
                }
            }

            return carInfo;
        }

        private static void BuildMowYardNode(
            Industry industry,
            string areaName,
            Color areaColor,
            SupplyFleetStore store,
            SupplyNetworkSnapshot snapshot,
            Dictionary<string, SupplyCarInfo> fleetById)
        {
            SupplyFacilityNode node = new SupplyFacilityNode
            {
                FacilityId = industry.identifier,
                DisplayName = industry.DisplayName,
                AreaName = areaName,
                AreaColor = areaColor,
                Industry = industry
            };

            List<StorageLoader> storageLoaders = new List<StorageLoader>();
            foreach (IndustryComponent comp in industry.Components)
            {
                if (comp is StorageLoader storageLoader && storageLoader.loads != null)
                {
                    storageLoaders.Add(storageLoader);
                    foreach (StorageLoader.LoadEntry entry in storageLoader.loads)
                    {
                        if (entry.load == null) continue;
                        string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                        float curStock = industry.Storage != null ? industry.Storage.QuantityInStorage(entry.load, prefix) : 0f;
                        snapshot.MowOrigins.Add(new SupplyOriginInfo
                        {
                            Identifier = storageLoader.Identifier,
                            DisplayName = industry.DisplayName,
                            KindLabel = "MOW Yard",
                            AreaName = areaName,
                            IsOffMapSource = false,
                            Industry = industry,
                            Component = storageLoader,
                            Position = (OpsCarPosition)storageLoader,
                            Load = entry.load,
                            CurrentBuffer = curStock
                        });
                    }
                }
            }

            foreach (IndustryComponent comp in industry.Components)
            {
                if (comp is StorageUnloader unloader && unloader.loads != null)
                {
                    string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    foreach (StorageUnloader.LoadEntry entry in unloader.loads)
                    {
                        if (entry.load == null) continue;
                        Load load = entry.load;
                        float maxCap = Mathf.Max(0.001f, entry.storage * load.NominalQuantityPerCarLoad);
                        float curStock = industry.Storage != null ? industry.Storage.QuantityInStorage(load, prefix) : 0f;
                        bool hasTarget = store.TryGetTargetStock(industry, load, out float savedTarget) && savedTarget > 0.001f;
                        float target = hasTarget ? savedTarget : 0f;

                        (float fleetIn, float nonFleetIn) = MeasureInboundTonnage(industry, unloader, load, fleetById);
                        (float activeOut, float committedOut) = MeasureOutboundTonnage(industry, storageLoaders, load, fleetById);

                        node.Bars.Add(new SupplyCommodityBar
                        {
                            FacilityId = industry.identifier,
                            FacilityDisplayName = industry.DisplayName,
                            AreaName = areaName,
                            AreaColor = areaColor,
                            Load = load,
                            CurrentAmount = curStock,
                            OutgoingAmount = activeOut,
                            CommittedOutboundAmount = committedOut,
                            MaxCapacity = maxCap,
                            HasTarget = hasTarget,
                            TargetAmount = target,
                            RemainingDemand = hasTarget ? Mathf.Max(0f, target - curStock - fleetIn + activeOut + committedOut) : 0f,
                            FleetInboundAmount = fleetIn,
                            NonFleetInboundAmount = nonFleetIn,
                            IsSinkDemandOnly = false,
                            IsTargetEditable = true,
                            DestinationPosition = (OpsCarPosition)unloader,
                            DestinationKind = WorkOrderDestinationKind.MowYard
                        });
                    }
                }
            }

            if (node.Bars.Count > 0)
            {
                snapshot.MowYards.Add(node);
            }
        }

        private static void BuildFuelSinkNode(
            Industry industry,
            string areaName,
            Color areaColor,
            SupplyFleetStore store,
            SupplyNetworkSnapshot snapshot,
            Dictionary<string, SupplyCarInfo> fleetById)
        {
            SupplyFacilityNode node = new SupplyFacilityNode
            {
                FacilityId = industry.identifier,
                DisplayName = industry.DisplayName,
                AreaName = areaName,
                AreaColor = areaColor,
                Industry = industry
            };

            RepairTrack? repairTrack = industry.Components.OfType<RepairTrack>().FirstOrDefault();

            foreach (IndustryComponent comp in industry.Components)
            {
                if (comp is IndustryUnloader unloader && unloader.load is { } load)
                {
                    if (!IsFuelOrRepairLoad(load))
                    {
                        continue;
                    }

                    string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float curStock = industry.Storage != null ? industry.Storage.QuantityInStorage(load, prefix) : 0f;
                    float maxCap = Mathf.Max(0.001f, unloader.maxStorage);
                    bool hasTarget = true;
                    float target = store.TryGetTargetStock(industry, load, out float savedTarget)
                        ? savedTarget
                        : maxCap;

                    (float onTrackDelivery, float fleetIn, float nonFleetIn) = MeasureInboundTonnage(
                        industry, unloader, load, fleetById, includeSpottedDelivery: true);

                    if (repairTrack != null && string.Equals(load.id, repairTrack.repairPartsLoadId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (curStock + onTrackDelivery + fleetIn + nonFleetIn < maxCap * 0.25f)
                        {
                            node.HasRepairAlert = true;
                            node.AlertText = "Repair Parts Low";
                        }
                    }

                    node.Bars.Add(new SupplyCommodityBar
                    {
                        FacilityId = industry.identifier,
                        FacilityDisplayName = industry.DisplayName,
                        AreaName = areaName,
                        AreaColor = areaColor,
                        Load = load,
                        CurrentAmount = curStock,
                        DeliveryAmount = onTrackDelivery,
                        MaxCapacity = maxCap,
                        HasTarget = hasTarget,
                        TargetAmount = target,
                        RemainingDemand = Mathf.Max(0f, target - curStock - onTrackDelivery - fleetIn),
                        FleetInboundAmount = fleetIn,
                        NonFleetInboundAmount = nonFleetIn,
                        IsSinkDemandOnly = false,
                        IsTargetEditable = true,
                        DestinationPosition = (OpsCarPosition)unloader,
                        DestinationKind = WorkOrderDestinationKind.EngineTerminal
                    });
                }
            }

            if (node.Bars.Count > 0)
            {
                snapshot.FuelSinks.Add(node);
            }
        }

        private void BuildMilestoneSinks(SupplyFleetStore store, SupplyNetworkSnapshot snapshot)
        {
            TrackConstructionMilestone[] milestones = GetCachedMilestones();
            if (milestones == null || milestones.Length == 0)
            {
                return;
            }

            foreach (TrackConstructionMilestone milestone in milestones)
            {
                if (milestone == null || !milestone.IsInProgress)
                {
                    continue;
                }

                string sinkId = $"milestone:{milestone.name}";
                Car? assignedWorkCar = store.GetAssignedWorkCar(sinkId);
                if (assignedWorkCar == null && snapshot.AvailableWorkCars.Count == 1)
                {
                    assignedWorkCar = snapshot.AvailableWorkCars[0];
                }

                string milestoneTitle = !string.IsNullOrEmpty(milestone.Title) ? milestone.Title : milestone.name;
                string stagingLabel = assignedWorkCar != null
                    ? $"Work Car: {assignedWorkCar.DisplayName}"
                    : "No Work Car Assigned";
                string areaName = assignedWorkCar != null
                    ? SupplyFleetStore.ResolveCarAreaName(assignedWorkCar)
                    : milestoneTitle;
                Color color = snapshot.ResolveAreaColor(areaName);

                milestone.TryGetConstructionLengths(out float remainingMeters, out float totalMeters);
                float progress = totalMeters > 0.001f ? Mathf.Clamp01((totalMeters - remainingMeters) / totalMeters) : 0f;

                SupplyProjectSink sink = new SupplyProjectSink
                {
                    SinkId = sinkId,
                    DisplayName = milestoneTitle,
                    IsConstructionMilestone = true,
                    ProgressRatio = progress,
                    AssignedWorkCarId = assignedWorkCar?.id,
                    StagingLabel = stagingLabel,
                    AreaColor = color
                };

                try
                {
                    Graph? graph = Graph.Shared;
                    TrackConditionController? tcc = TrackConditionController.Shared;
                    if (graph != null && tcc != null && milestone.constructTrackGroups != null && milestone.constructTrackGroups.Count > 0)
                    {
                        List<string> queryGroupIds = milestone.constructTrackGroups.Distinct().ToList();
                        List<TrackSegment> segments = graph.SegmentsInGroups(queryGroupIds, includeGroupDisabled: false)
                            .Where(s => s.ConstructionState != TrackConstructionState.Complete)
                            .ToList();

                        ConstructionLoadRequirementsSummary summary = tcc.GetConstructionRequirementsSummary(segments);
                        if (summary != null && summary.LoadRequirements != null)
                        {
                            Dictionary<Load, float> totals = new Dictionary<Load, float>();
                            foreach ((TrackConstructionState _, List<TrackMaintenanceProfile.LoadRequirement> reqs, float _) in summary.LoadRequirements)
                            {
                                if (reqs == null) continue;
                                foreach (TrackMaintenanceProfile.LoadRequirement req in reqs)
                                {
                                    if (req.load == null || req.amount <= 0.001f) continue;
                                    totals[req.load] = (totals.TryGetValue(req.load, out float prev) ? prev : 0f) + req.amount;
                                }
                            }

                            OpsCarPosition? destPos = assignedWorkCar != null
                                ? CoupledToDestination.CreatePositionForCar(assignedWorkCar)
                                : (OpsCarPosition?)null;

                            foreach (KeyValuePair<Load, float> kvp in totals)
                            {
                                (float onTrain, float ordered) = MeasureProjectDeliveriesAndOrders(
                                    store, snapshot.FleetCars, sinkId, sink.AssignedWorkCarId, kvp.Key);
                                sink.Bars.Add(new SupplyCommodityBar
                                {
                                    FacilityId = sinkId,
                                    FacilityDisplayName = sink.DisplayName,
                                    AreaName = areaName,
                                    AreaColor = color,
                                    Load = kvp.Key,
                                    DeliveryAmount = onTrain,
                                    RemainingDemand = kvp.Value,
                                    FleetInboundAmount = ordered,
                                    IsSinkDemandOnly = true,
                                    IsTargetEditable = false,
                                    DestinationPosition = destPos,
                                    DestinationKind = WorkOrderDestinationKind.WorkCar,
                                    SinkId = sinkId
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Warning($"Failed to compute construction summary for '{milestone.name}': {ex.Message}");
                }

                snapshot.MowSinks.Add(sink);
            }
        }

        private static void BuildPlannedRepairSinks(SupplyFleetStore store, SupplyNetworkSnapshot snapshot)
        {
            foreach (PlannedRepairDemand repair in store.PlannedRepairs)
            {
                if (repair == null) continue;

                Car? workCar = null;
                if (!string.IsNullOrEmpty(repair.StagingWorkCarId) && TrainController.Shared != null)
                {
                    TrainController.Shared.TryGetCarForId(repair.StagingWorkCarId, out workCar);
                }

                string stagingLabel = workCar != null
                    ? $"Work Car: {workCar.DisplayName}"
                    : (!string.IsNullOrEmpty(repair.StagingMowYardId)
                        ? $"MOW Yard: {repair.StagingMowYardId} (Staging)"
                        : "Unassigned Staging");

                string areaName = workCar != null ? SupplyFleetStore.ResolveCarAreaName(workCar) : repair.Label;
                Color color = snapshot.ResolveAreaColor(areaName);
                OpsCarPosition? destPos = workCar != null
                    ? CoupledToDestination.CreatePositionForCar(workCar)
                    : (OpsCarPosition?)null;

                SupplyProjectSink sink = new SupplyProjectSink
                {
                    SinkId = repair.Id,
                    DisplayName = repair.Label,
                    IsConstructionMilestone = false,
                    AssignedWorkCarId = repair.StagingWorkCarId,
                    StagingLabel = stagingLabel,
                    AreaColor = color
                };

                foreach (KeyValuePair<string, float> kvp in repair.LoadDemands)
                {
                    if (kvp.Value <= 0.001f) continue;
                    Load? load = CarLoadLibrary.GetLoad(kvp.Key) ?? CarLoadLibrary.GetLoad($"rr/{kvp.Key}");
                    if (load == null) continue;

                    (float onTrain, float ordered) = MeasureProjectDeliveriesAndOrders(
                        store, snapshot.FleetCars, repair.Id, sink.AssignedWorkCarId, load);
                    sink.Bars.Add(new SupplyCommodityBar
                    {
                        FacilityId = repair.Id,
                        FacilityDisplayName = repair.Label,
                        AreaName = areaName,
                        AreaColor = color,
                        Load = load,
                        DeliveryAmount = onTrain,
                        RemainingDemand = kvp.Value,
                        FleetInboundAmount = ordered,
                        IsSinkDemandOnly = true,
                        IsTargetEditable = false,
                        DestinationPosition = destPos,
                        DestinationKind = WorkOrderDestinationKind.PlannedRepair,
                        SinkId = repair.Id
                    });
                }

                snapshot.MowSinks.Add(sink);
            }
        }

        private static (float fleetInbound, float nonFleetInbound) MeasureInboundTonnage(
            Industry industry,
            IndustryComponent targetComponent,
            Load load,
            Dictionary<string, SupplyCarInfo> fleetById)
        {
            (_, float fleetIn, float nonFleetIn) = MeasureInboundTonnage(industry, targetComponent, load, fleetById, includeSpottedDelivery: false);
            return (fleetIn, nonFleetIn);
        }

        private static (float onTrackDelivery, float fleetInbound, float nonFleetInbound) MeasureInboundTonnage(
            Industry industry,
            IndustryComponent targetComponent,
            Load load,
            Dictionary<string, SupplyCarInfo> fleetById,
            bool includeSpottedDelivery)
        {
            float onTrackDelivery = 0f;
            float fleetIn = 0f;
            float nonFleetIn = 0f;

            if (TrainController.Shared == null)
            {
                return (0f, 0f, 0f);
            }

            HashSet<string>? spottedCarIds = null;
            if (includeSpottedDelivery && OpsController.Shared != null)
            {
                try
                {
                    IEnumerable<Car>? carsOnTrack = OpsController.Shared.CarsAtPosition((OpsCarPosition)targetComponent);
                    if (carsOnTrack != null)
                    {
                        foreach (Car car in carsOnTrack)
                        {
                            if (car == null) continue;
                            (Load? carLoad, float qty, _) = CarTrackingHelper.GetCarCargo(car, null);
                            if (carLoad != null &&
                                string.Equals(carLoad.id, load.id, StringComparison.OrdinalIgnoreCase) &&
                                qty > 0.001f)
                            {
                                onTrackDelivery += qty;
                                spottedCarIds ??= new HashSet<string>(StringComparer.Ordinal);
                                spottedCarIds.Add(car.id);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore transient track lookup errors
                }
            }

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (car == null) continue;
                if (spottedCarIds != null && spottedCarIds.Contains(car.id)) continue;

                if (fleetById.TryGetValue(car.id, out SupplyCarInfo fleetCar) &&
                    fleetCar.ActiveOrder != null &&
                    fleetCar.ActiveOrder.IsForComponentAndLoad(targetComponent, load))
                {
                    fleetIn += fleetCar.IsLoaded ? fleetCar.CurrentQuantity : fleetCar.ActiveOrder.ExpectedQuantity;
                    continue;
                }

                if (CarTrackingHelper.IsWaybilledHere(car.Waybill, industry, targetComponent))
                {
                    (Load? carLoad, float qty, _) = CarTrackingHelper.GetCarCargo(car, null);
                    if (carLoad != null && string.Equals(carLoad.id, load.id, StringComparison.OrdinalIgnoreCase) && qty > 0.001f)
                    {
                        nonFleetIn += qty;
                    }
                }
            }

            return (onTrackDelivery, fleetIn, nonFleetIn);
        }

        private static (float activeOutbound, float committedOutbound) MeasureOutboundTonnage(
            Industry industry,
            List<StorageLoader> storageLoaders,
            Load load,
            Dictionary<string, SupplyCarInfo> fleetById)
        {
            if (storageLoaders.Count == 0 || TrainController.Shared == null)
            {
                return (0f, 0f);
            }

            float activeOut = 0f;
            float committedOut = 0f;
            HashSet<string>? spottedCarIds = null;
            HashSet<string> loaderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < storageLoaders.Count; i++)
            {
                StorageLoader loader = storageLoaders[i];
                loaderIds.Add(loader.Identifier);

                if (OpsController.Shared == null) continue;
                try
                {
                    IEnumerable<Car>? carsOnTrack = OpsController.Shared.CarsAtPosition((OpsCarPosition)loader);
                    if (carsOnTrack == null) continue;

                    foreach (Car car in carsOnTrack)
                    {
                        if (car == null) continue;

                        bool matchesLoad = false;
                        if (car.Waybill.HasValue && string.Equals(car.Waybill.Value.Tag, load.id, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesLoad = true;
                        }
                        else if (fleetById.TryGetValue(car.id, out SupplyCarInfo fc) &&
                                 fc.ActiveOrder != null &&
                                 !fc.ActiveOrder.HasBeenLoaded &&
                                 string.Equals(fc.ActiveOrder.SourceId, loader.Identifier, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(fc.ActiveOrder.LoadId, load.id, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesLoad = true;
                        }

                        if (!matchesLoad) continue;

                        if (SupplyFleetStore.TryGetCarCapacityForLoad(car, load, out float cap))
                        {
                            (Load? curLoad, float curQty, _) = CarTrackingHelper.GetCarCargo(car, null);
                            float existing = curLoad != null && string.Equals(curLoad.id, load.id, StringComparison.OrdinalIgnoreCase)
                                ? curQty
                                : 0f;
                            activeOut += Mathf.Max(0f, cap - existing);
                            spottedCarIds ??= new HashSet<string>(StringComparer.Ordinal);
                            spottedCarIds.Add(car.id);
                        }
                    }
                }
                catch
                {
                    // Ignore transient track lookup errors
                }
            }

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (car == null) continue;
                if (spottedCarIds != null && spottedCarIds.Contains(car.id)) continue;

                if (fleetById.TryGetValue(car.id, out SupplyCarInfo fleetCar) &&
                    fleetCar.ActiveOrder != null &&
                    !fleetCar.ActiveOrder.HasBeenLoaded &&
                    !string.IsNullOrEmpty(fleetCar.ActiveOrder.SourceId) &&
                    loaderIds.Contains(fleetCar.ActiveOrder.SourceId!) &&
                    string.Equals(fleetCar.ActiveOrder.LoadId, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    float cap = SupplyFleetStore.TryGetCarCapacityForLoad(car, load, out float carCap)
                        ? carCap
                        : fleetCar.ActiveOrder.ExpectedQuantity;
                    (Load? curLoad, float curQty, _) = CarTrackingHelper.GetCarCargo(car, null);
                    float existing = curLoad != null && string.Equals(curLoad.id, load.id, StringComparison.OrdinalIgnoreCase)
                        ? curQty
                        : 0f;
                    // Fleet orders are committed by our own dispatch, so show them directly as Outgoing
                    activeOut += Mathf.Max(0f, cap - existing);
                    continue;
                }

                if (car.Waybill.HasValue &&
                    string.Equals(car.Waybill.Value.Tag, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < storageLoaders.Count; i++)
                    {
                        if (CarTrackingHelper.IsWaybilledHere(car.Waybill, industry, storageLoaders[i]))
                        {
                            float cap = SupplyFleetStore.TryGetCarCapacityForLoad(car, load, out float carCap)
                                ? carCap
                                : load.NominalQuantityPerCarLoad;
                            (Load? curLoad, float curQty, _) = CarTrackingHelper.GetCarCargo(car, null);
                            float existing = curLoad != null && string.Equals(curLoad.id, load.id, StringComparison.OrdinalIgnoreCase)
                                ? curQty
                                : 0f;
                            committedOut += Mathf.Max(0f, cap - existing);
                            break;
                        }
                    }
                }
            }

            return (activeOut, committedOut);
        }

        private static (float onWorkTrain, float orderedNotYetOnTrain) MeasureProjectDeliveriesAndOrders(
            SupplyFleetStore store,
            List<SupplyCarInfo> fleetCars,
            string sinkId,
            string? assignedWorkCarId,
            Load load)
        {
            float onWorkTrain = 0f;
            HashSet<string>? onTrainCarIds = null;

            if (!string.IsNullOrEmpty(assignedWorkCarId) &&
                TrainController.Shared != null &&
                TrainController.Shared.TryGetCarForId(assignedWorkCarId, out Car workCar) &&
                workCar != null &&
                !workCar.IsInBardo)
            {
                try
                {
                    foreach (Car coupled in workCar.EnumerateCoupled())
                    {
                        if (coupled == null) continue;
                        onTrainCarIds ??= new HashSet<string>(StringComparer.Ordinal);
                        if (!onTrainCarIds.Add(coupled.id)) continue;

                        (Load? carLoad, float qty, _) = CarTrackingHelper.GetCarCargo(coupled, null);
                        if (carLoad != null &&
                            qty > 0.001f &&
                            string.Equals(carLoad.id, load.id, StringComparison.OrdinalIgnoreCase))
                        {
                            onWorkTrain += qty;
                        }
                    }
                }
                catch
                {
                    // Ignore transient consist enumeration errors
                }
            }

            float ordered = 0f;
            for (int i = 0; i < fleetCars.Count; i++)
            {
                SupplyCarInfo carInfo = fleetCars[i];
                if (onTrainCarIds != null && onTrainCarIds.Contains(carInfo.CarId))
                {
                    continue;
                }

                WorkOrder? order = carInfo.ActiveOrder;
                if (order == null) continue;

                if (order.IsForSinkAndLoad(sinkId, load))
                {
                    ordered += carInfo.IsLoaded ? carInfo.CurrentQuantity : order.ExpectedQuantity;
                }
                else if (carInfo.IsLoaded &&
                         carInfo.CurrentLoad != null &&
                         string.Equals(carInfo.CurrentLoad.id, load.id, StringComparison.OrdinalIgnoreCase) &&
                         order.IsPassiveForWorkCar(assignedWorkCarId, store))
                {
                    ordered += carInfo.CurrentQuantity;
                }
            }

            return (onWorkTrain, ordered);
        }

        private static void EvaluateCarStatuses(SupplyNetworkSnapshot snapshot)
        {
            if (OpsController.Shared == null || snapshot.FleetCars.Count == 0)
            {
                return;
            }

            Dictionary<string, SupplyOriginInfo> originsById = new Dictionary<string, SupplyOriginInfo>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < snapshot.MowOrigins.Count; i++)
            {
                SupplyOriginInfo o = snapshot.MowOrigins[i];
                if (!originsById.ContainsKey(o.Identifier)) originsById[o.Identifier] = o;
            }
            for (int i = 0; i < snapshot.FuelOrigins.Count; i++)
            {
                SupplyOriginInfo o = snapshot.FuelOrigins[i];
                if (!originsById.ContainsKey(o.Identifier)) originsById[o.Identifier] = o;
            }

            Dictionary<string, OpsCarPosition> destPositions = new Dictionary<string, OpsCarPosition>(StringComparer.OrdinalIgnoreCase);
            void IndexFacilityDests(List<SupplyFacilityNode> nodes)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int b = 0; b < nodes[i].Bars.Count; b++)
                    {
                        SupplyCommodityBar bar = nodes[i].Bars[b];
                        if (bar.DestinationPosition.HasValue && !destPositions.ContainsKey(bar.DestinationPosition.Value.Identifier))
                        {
                            destPositions[bar.DestinationPosition.Value.Identifier] = bar.DestinationPosition.Value;
                        }
                    }
                }
            }
            IndexFacilityDests(snapshot.MowYards);
            IndexFacilityDests(snapshot.FuelSinks);

            Dictionary<string, HashSet<string>> spottedCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            bool IsCarAt(string carId, OpsCarPosition pos)
            {
                string posId = pos.Identifier;
                if (string.IsNullOrEmpty(posId)) return false;

                if (!spottedCache.TryGetValue(posId, out HashSet<string> set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        IEnumerable<Car>? carsOnTrack = OpsController.Shared.CarsAtPosition(pos);
                        if (carsOnTrack != null)
                        {
                            foreach (Car c in carsOnTrack)
                            {
                                if (c != null) set.Add(c.id);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                    spottedCache[posId] = set;
                }

                return set.Contains(carId);
            }

            for (int i = 0; i < snapshot.FleetCars.Count; i++)
            {
                SupplyCarInfo carInfo = snapshot.FleetCars[i];
                if (carInfo.IsOffMap || carInfo.Car == null) continue;

                WorkOrder? order = carInfo.ActiveOrder;
                if (order != null)
                {
                    // Check if parked at Destination
                    if (order.IsPassiveWorkTrain || order.DestinationKind == WorkOrderDestinationKind.WorkCar)
                    {
                        if (carInfo.CoupledWorkCars.Count > 0)
                        {
                            carInfo.IsAtDestination = true;
                        }
                    }
                    else if (!string.IsNullOrEmpty(order.DestinationId) &&
                             destPositions.TryGetValue(order.DestinationId, out OpsCarPosition dPos) &&
                             IsCarAt(carInfo.CarId, dPos))
                    {
                        carInfo.IsAtDestination = true;
                    }

                    // Check if at Source / Blocked (no material to load)
                    if (!order.HasBeenLoaded && !order.SkipLoading && !string.IsNullOrEmpty(order.SourceId) &&
                        originsById.TryGetValue(order.SourceId!, out SupplyOriginInfo srcOrigin))
                    {
                        if (IsCarAt(carInfo.CarId, srcOrigin.Position))
                        {
                            carInfo.IsAtSource = true;
                        }

                        if (!srcOrigin.IsOffMapSource && srcOrigin.CurrentBuffer <= 0.001f)
                        {
                            carInfo.IsBlockedNoMaterial = true;
                        }
                    }
                }
                else if (carInfo.Car.Waybill.HasValue)
                {
                    if (IsCarAt(carInfo.CarId, carInfo.Car.Waybill.Value.Destination))
                    {
                        carInfo.IsAtDestination = true;
                    }
                }
            }
        }

        private static bool IsMowLoad(Load load)
        {
            if (load == null || string.IsNullOrEmpty(load.id)) return false;
            if (MowLoadIds.Contains(load.id)) return true;
            string lower = load.id.ToLowerInvariant();
            return lower.Contains("ballast") || lower.Contains("ties") || lower.Contains("rail")
                || lower.Contains("concrete") || lower.Contains("explosive")
                || lower.Contains("bridge") || lower.Contains("timber") || lower.Contains("beam");
        }

        private static bool IsFuelOrRepairLoad(Load load)
        {
            if (load == null || string.IsNullOrEmpty(load.id)) return false;
            if (FuelLoadIds.Contains(load.id)) return true;
            string lower = load.id.ToLowerInvariant();
            return lower.Contains("coal") || lower.Contains("diesel") || lower.Contains("repair");
        }

        private static bool IsWorkCar(Car car)
        {
            return car != null && car.CarType.IsMaintenanceOfWay;
        }

        private static SupplyCarShape ClassifyCarShape(Car car)
        {
            if (car == null) return SupplyCarShape.Gondola;
            CarType ct = car.CarType;
            if (ct.IsMaintenanceOfWay) return SupplyCarShape.WorkCar;
            if (ct.IsTank) return SupplyCarShape.Tanker;
            if (ct.IsFlat) return SupplyCarShape.Flatcar;
            if (ct.IsGondola) return SupplyCarShape.Gondola;

            string code = ct.ToString();
            if (code.StartsWith("H", StringComparison.OrdinalIgnoreCase) || code.StartsWith("LO", StringComparison.OrdinalIgnoreCase))
            {
                return SupplyCarShape.Hopper;
            }
            if (code.StartsWith("X", StringComparison.OrdinalIgnoreCase) || code.StartsWith("R", StringComparison.OrdinalIgnoreCase))
            {
                return SupplyCarShape.Boxcar;
            }

            return SupplyCarShape.Gondola;
        }

        private static string ResolveIndustryAreaName(Industry industry)
        {
            try
            {
                if (OpsController.Shared != null && industry.Components != null && industry.Components.Count > 0)
                {
                    Area area = OpsController.Shared.AreaForCarPosition((OpsCarPosition)industry.Components[0]);
                    if (area != null && !string.IsNullOrEmpty(area.name))
                    {
                        return area.name;
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return industry.DisplayName ?? "Unknown";
        }
    }
}
