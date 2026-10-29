using System;
using System.Collections.Generic;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class MowYardCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.MaintenanceOfWay;

        public void Collect(Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Maintenance of Way Material Depot";
            snapshot.State = ProductionState.Running;
            snapshot.StatusMessage = "Material reserves and staging tracks for track maintenance work trains.";
            snapshot.DiagnosticAlert = string.Empty;

            List<IndustryComponent>? components = industry.Components;
            if (components == null)
            {
                return;
            }

            OpsController? opsController = OpsController.Shared;

            // 1. Storage Buffers from StorageUnloader / StorageLoader
            StorageUnloader? storageUnloaderComp = null;
            StorageLoader? storageLoaderComp = null;

            foreach (IndustryComponent component in components)
            {
                if (component is StorageUnloader storageUnloader)
                {
                    storageUnloaderComp = storageUnloader;
                    string? prefix = !storageUnloader.sharedStorage ? storageUnloader.subIdentifier : null;
                    foreach (StorageUnloader.LoadEntry entry in storageUnloader.loads)
                    {
                        if (entry.load is not { } load)
                        {
                            continue;
                        }

                        float capacity = entry.storage * load.NominalQuantityPerCarLoad;
                        float currentStock = industry.Storage?.QuantityInStorage(load, prefix) ?? 0f;

                        ReserveBuffer buf = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.MaintenanceReserve);
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = Mathf.Max(0.001f, capacity);
                    }
                }
                else if (component is StorageLoader storageLoader)
                {
                    storageLoaderComp = storageLoader;
                    string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                    foreach (StorageLoader.LoadEntry entry in storageLoader.loads)
                    {
                        if (entry.load is not { } load)
                        {
                            continue;
                        }

                        ReserveBuffer buf = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.MaintenanceReserve);
                        if (buf.MaxCapacity <= 0.001f)
                        {
                            float currentStock = industry.Storage?.QuantityInStorage(load, prefix) ?? 0f;
                            buf.CurrentAmount = currentStock;
                            buf.MaxCapacity = Mathf.Max(0.001f, load.NominalQuantityPerCarLoad * 10f);
                        }
                    }
                }
            }

            // 2. Collect Cars for all MOW yard tracks (unified, no duplicate tracks or cars)
            if (opsController != null)
            {
                CollectMowYardCars(opsController, industry, storageLoaderComp, storageUnloaderComp, snapshot);
            }
        }

        private static void CollectMowYardCars(
            OpsController opsController,
            Industry industry,
            StorageLoader? storageLoader,
            StorageUnloader? storageUnloader,
            IndustrySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null)
            {
                return;
            }

            string trackName = "MOW Yard Track";
            foreach (IndustryComponent c in components)
            {
                if (c is StorageLoader or StorageUnloader or ProgressionIndustryComponent)
                {
                    string? name = c.ShortName(industry);
                    if (!string.IsNullOrEmpty(name))
                    {
                        trackName = name;
                        break;
                    }
                }
            }

            SpurCarGroup spurGroup = BufferHelper.GetOrAddSpurGroup(snapshot.SpurGroups, trackName, SpurRole.MowMaterialHandling);
            HashSet<string> seenCarIds = new HashSet<string>(16);

            foreach (IndustryComponent comp in components)
            {
                if (comp is not (StorageLoader or StorageUnloader or ProgressionIndustryComponent))
                {
                    continue;
                }

                IEnumerable<Car>? cars = opsController.CarsAtPosition(comp);
                if (cars == null)
                {
                    continue;
                }

                foreach (Car car in cars)
                {
                    if (car == null || Mathf.Abs(car.velocity) > 0.05f)
                    {
                        continue;
                    }

                    if (!seenCarIds.Add(car.id))
                    {
                        continue;
                    }

                    IOpsCar? opsCar = opsController.CachedOpsCar(car);
                    string reportingMark = opsCar?.DisplayName ?? car.DisplayName;
                    string carType = opsCar?.CarType ?? car.Definition?.CarType ?? "Car";

                    (Load? actualLoad, float actualQty, float actualCap) = CarTrackingHelper.GetCarCargo(car, opsCar);
                    Waybill? waybill = opsCar?.Waybill;
                    bool isBilledHere = CarTrackingHelper.IsWaybilledHere(waybill, industry);

                    CarStatusState statusState;
                    string statusDetail;
                    string cargoDisplayName = actualLoad?.description ?? string.Empty;
                    LoadUnits carUnits = actualLoad?.units ?? LoadUnits.Pounds;
                    float currentWeight = actualQty;
                    float capacityWeight = actualCap;
                    float ratePerMin = 0f;
                    TimeSpan? eta = null;

                    if (!isBilledHere || !waybill.HasValue)
                    {
                        if (waybill.HasValue)
                        {
                            string destName = !string.IsNullOrEmpty(waybill.Value.Destination.DisplayName)
                                ? waybill.Value.Destination.DisplayName
                                : waybill.Value.Destination.Identifier;
                            if (destName.Length > 20)
                            {
                                destName = destName.Substring(0, 18) + "...";
                            }
                            statusState = CarStatusState.WrongDestination;
                            statusDetail = $"Dest: {destName}";
                        }
                        else
                        {
                            statusState = CarStatusState.Idle;
                            statusDetail = actualQty > 0.001f ? "Loaded" : "Idle";
                        }
                    }
                    else
                    {
                        Waybill wb = waybill.Value;
                        bool isMowUnload = storageUnloader != null
                                           && actualLoad != null
                                           && actualQty > actualLoad.ZeroThreshold
                                           && (CarTrackingHelper.IsWaybilledHere(wb, industry, storageUnloader) || !CarTrackingHelper.IsWaybilledHere(wb, industry, storageLoader));

                        if (!isMowUnload && storageLoader != null && (CarTrackingHelper.IsWaybilledHere(wb, industry, storageLoader) || CarTrackingHelper.IsWaybilledHere(wb, industry)))
                        {
                            Load? targetLoad = !string.IsNullOrEmpty(wb.Tag)
                                ? CarLoadLibrary.GetLoad(wb.Tag)
                                : actualLoad;

                            if (targetLoad != null)
                            {
                                carUnits = targetLoad.units;
                                cargoDisplayName = targetLoad.description ?? "Material";
                                (float q, float c) = opsCar?.QuantityOfLoad(targetLoad) ?? (actualQty, actualCap);
                                currentWeight = q;
                                capacityWeight = c;
                                float toFill = Mathf.Max(0f, c - q);

                                string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                                float currentStock = industry.Storage?.QuantityInStorage(targetLoad, prefix) ?? 0f;

                                if (toFill <= targetLoad.ZeroThreshold)
                                {
                                    statusState = CarStatusState.Completed;
                                    statusDetail = "Completed";
                                }
                                else if (currentStock > targetLoad.ZeroThreshold)
                                {
                                    statusState = CarStatusState.ActiveTransfer;
                                    statusDetail = "Loading";

                                    int loadRate = 1;
                                    foreach (StorageLoader.LoadEntry l in storageLoader.loads)
                                    {
                                        if (l.loadId == targetLoad.id)
                                        {
                                            loadRate = l.carLoadRate;
                                            break;
                                        }
                                    }

                                    float ratePerDay = loadRate * targetLoad.NominalQuantityPerCarLoad;
                                    ratePerMin = ratePerDay / 1440f;

                                    if (ratePerMin > 0.0001f)
                                    {
                                        float minutes = toFill / ratePerMin;
                                        eta = TimeSpan.FromMinutes(minutes);
                                    }

                                    foreach (BufferInfo b in snapshot.Buffers)
                                    {
                                        if (b.CargoType == targetLoad.id)
                                        {
                                            b.ActiveOutbound += toFill;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    statusState = CarStatusState.Blocked;
                                    statusDetail = "Yard Depleted";
                                }
                            }
                            else
                            {
                                statusState = CarStatusState.ActiveTransfer;
                                statusDetail = "Loading";
                            }
                        }
                        else if (isMowUnload && storageUnloader != null)
                        {
                            Load? targetLoad = actualLoad;
                            if (targetLoad != null && actualQty > targetLoad.ZeroThreshold)
                            {
                                carUnits = targetLoad.units;
                                string? prefix = !storageUnloader.sharedStorage ? storageUnloader.subIdentifier : null;
                                float currentStock = industry.Storage?.QuantityInStorage(targetLoad, prefix) ?? 0f;

                                float unloaderStorageCap = 100f;
                                int unloadRateSetting = 1;
                                foreach (StorageUnloader.LoadEntry l in storageUnloader.loads)
                                {
                                    if (l.loadId == targetLoad.id)
                                    {
                                        unloaderStorageCap = l.storage * targetLoad.NominalQuantityPerCarLoad;
                                        unloadRateSetting = l.unloadRate;
                                        break;
                                    }
                                }

                                float spaceRemaining = Mathf.Max(0f, unloaderStorageCap - currentStock);
                                if (spaceRemaining > targetLoad.ZeroThreshold)
                                {
                                    statusState = CarStatusState.ActiveTransfer;
                                    statusDetail = "Unloading";

                                    float ratePerDay = unloadRateSetting * targetLoad.NominalQuantityPerCarLoad;
                                    ratePerMin = ratePerDay / 1440f;

                                    if (ratePerMin > 0.0001f)
                                    {
                                        float minutes = actualQty / ratePerMin;
                                        eta = TimeSpan.FromMinutes(minutes);
                                    }

                                    foreach (BufferInfo b in snapshot.Buffers)
                                    {
                                        if (b.CargoType == targetLoad.id)
                                        {
                                            b.ActiveInbound += actualQty;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    statusState = CarStatusState.Blocked;
                                    statusDetail = "Storage Full";
                                }
                            }
                            else
                            {
                                statusState = CarStatusState.Completed;
                                statusDetail = "Completed";
                            }
                        }
                        else
                        {
                            statusState = CarStatusState.Idle;
                            statusDetail = actualQty > 0.001f ? "Loaded" : "Idle";
                        }
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        ReportingMark = reportingMark,
                        CarType = carType,
                        TrackName = trackName,
                        CargoName = cargoDisplayName,
                        Units = carUnits,
                        CurrentAmount = currentWeight,
                        CapacityAmount = capacityWeight,
                        TransferRatePerMinute = ratePerMin,
                        EstimatedTimeToCompletion = eta,
                        StatusState = statusState,
                        StatusDetail = statusDetail
                    };

                    spurGroup.Cars.Add(carInfo);
                }
            }

            // Check for arriving work cars & material delivery cars en route across the map
            if (TrainController.Shared == null) return;
            SpurCarGroup enRouteGroup = new SpurCarGroup
            {
                TrackName = "En Route (Arriving)",
                Role = SpurRole.EnRouteWaybill
            };

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (car == null || seenCarIds.Contains(car.id))
                {
                    continue;
                }

                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar?.Waybill is not { } waybill)
                {
                    continue;
                }

                // Arriving work car heading here to load material
                if (storageLoader != null && CarTrackingHelper.IsWaybilledHere(waybill, industry, storageLoader))
                {
                    Load? targetLoad = !string.IsNullOrEmpty(waybill.Tag) ? CarLoadLibrary.GetLoad(waybill.Tag) : null;
                    if (targetLoad == null) continue;
                    (float q, float c) = opsCar.QuantityOfLoad(targetLoad);
                    float toFill = Mathf.Max(0f, c - q);

                    foreach (BufferInfo b in snapshot.Buffers)
                    {
                        if (b.CargoType == targetLoad.id)
                        {
                            b.CommittedOutbound += toFill;
                            break;
                        }
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        ReportingMark = opsCar.DisplayName ?? car.DisplayName,
                        CarType = opsCar.CarType ?? car.Definition?.CarType ?? "Car",
                        TrackName = "En Route",
                        CargoName = targetLoad.description ?? "Material",
                        Units = targetLoad.units,
                        CurrentAmount = q,
                        CapacityAmount = c,
                        TransferRatePerMinute = 0f,
                        EstimatedTimeToCompletion = null,
                        StatusState = CarStatusState.EnRoute,
                        StatusDetail = "En Route"
                    };

                    enRouteGroup.Cars.Add(carInfo);
                }
                // Arriving delivery car heading here to unload material
                else if (storageUnloader != null && CarTrackingHelper.IsWaybilledHere(waybill, industry, storageUnloader))
                {
                    (Load? actualLoad, float actualQty, float actualCap) = CarTrackingHelper.GetCarCargo(car, opsCar);
                    if (actualLoad == null) continue;
                    foreach (BufferInfo b in snapshot.Buffers)
                    {
                        if (b.CargoType == actualLoad.id)
                        {
                            b.ScheduledInbound += actualQty;
                            break;
                        }
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        ReportingMark = opsCar.DisplayName ?? car.DisplayName,
                        CarType = opsCar.CarType ?? car.Definition?.CarType ?? "Car",
                        TrackName = "En Route",
                        CargoName = actualLoad.description ?? "Material",
                        Units = actualLoad.units,
                        CurrentAmount = actualQty,
                        CapacityAmount = actualCap,
                        TransferRatePerMinute = 0f,
                        EstimatedTimeToCompletion = null,
                        StatusState = CarStatusState.EnRoute,
                        StatusDetail = "En Route"
                    };

                    enRouteGroup.Cars.Add(carInfo);
                }
            }

            if (enRouteGroup.Cars.Count > 0)
            {
                snapshot.SpurGroups.Add(enRouteGroup);
            }
        }
    }
}
