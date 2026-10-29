using System;
using System.Collections.Generic;
using Model;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class EngineServiceCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.EngineService;

        public void Collect(Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Locomotive Service Facility";
            snapshot.State = ProductionState.Running;
            snapshot.StatusMessage = "Terminal reserves ready for locomotive fueling, servicing, and repair.";
            snapshot.DiagnosticAlert = string.Empty;

            List<IndustryComponent>? components = industry.Components;
            if (components == null)
            {
                return;
            }

            OpsController? opsController = OpsController.Shared;
            HashSet<string> spottedCarIds = new HashSet<string>(16);

            RepairTrack? repairTrackComponent = null;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] is RepairTrack rt)
                {
                    repairTrackComponent = rt;
                    break;
                }
            }

            // 1. Process supply unloaders first to establish terminal supply buffers
            for (int i = 0; i < components.Count; i++)
            {
                IndustryComponent component = components[i];

                if (component is IndustryUnloader unloader && unloader.load is { } load)
                {
                    string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float currentStock = industry.Storage != null
                        ? industry.Storage.QuantityInStorage(load, prefix)
                        : 0f;

                    ReserveBuffer buf = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.LocomotiveSupply);
                    buf.CurrentAmount = currentStock;
                    buf.MaxCapacity = Mathf.Max(0.001f, unloader.maxStorage);

                    float bufferAvailable = Mathf.Max(0f, unloader.maxStorage - currentStock);
                    float nominalRatePerDay = unloader.carUnloadRate;
                    float nominalRatePerMin = nominalRatePerDay / 1440f;

                    bool isRepairTrackUnloader = repairTrackComponent != null &&
                        (string.Equals(load.id, repairTrackComponent.repairPartsLoadId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(unloader.ShortName(industry), repairTrackComponent.ShortName(industry), StringComparison.OrdinalIgnoreCase));
                    if (isRepairTrackUnloader)
                    {
                        buf.IsRepairParts = true;
                    }
                    SpurRole? roleOverride = isRepairTrackUnloader ? SpurRole.LocomotiveRepair : null;

                    SpurCarProgressionHelper.CollectCarsForSpur(opsController, unloader, load, component, industry, bufferAvailable, nominalRatePerMin, true, snapshot, roleOverride);
                }
            }

            // Track all cars spotted on supply tracks so en-route scanners ignore them
            for (int g = 0; g < snapshot.SpurGroups.Count; g++)
            {
                SpurCarGroup group = snapshot.SpurGroups[g];
                for (int c = 0; c < group.Cars.Count; c++)
                {
                    spottedCarIds.Add(group.Cars[c].ReportingMark);
                }
            }

            // 2. Process repair tracks, spotted equipment, and arriving repair/overhaul traffic
            if (opsController != null)
            {
                for (int i = 0; i < components.Count; i++)
                {
                    if (components[i] is RepairTrack repairTrack)
                    {
                        RepairTrackHelper.CollectRepairTrackCars(opsController, repairTrack, industry, snapshot, spottedCarIds);
                    }
                }
            }

            // 3. Scan for arriving fuel/supply cars en route across the map
            if (TrainController.Shared != null && opsController != null)
            {
                SpurCarGroup enRouteGroup = new SpurCarGroup
                {
                    TrackName = "En Route (Arriving)",
                    Role = SpurRole.EnRouteWaybill
                };

                foreach (Car car in TrainController.Shared.Cars)
                {
                    if (ReferenceEquals(car, null) || car == null ||
                        (car.DisplayName is { } carName && spottedCarIds.Contains(carName)) ||
                        (car.id is { } carIdentifier && spottedCarIds.Contains(carIdentifier)))
                    {
                        continue;
                    }

                    IOpsCar? opsCar = opsController.CachedOpsCar(car);
                    if (opsCar?.Waybill is not { } waybill)
                    {
                        continue;
                    }

                    for (int i = 0; i < components.Count; i++)
                    {
                        if (components[i] is IndustryUnloader unloader && unloader.load is { } load && CarTrackingHelper.IsWaybilledHere(waybill, industry, unloader))
                        {
                            (float qty, float cap) = opsCar.QuantityOfLoad(load);

                            for (int b = 0; b < snapshot.Buffers.Count; b++)
                            {
                                if (snapshot.Buffers[b].CargoType == load.id)
                                {
                                    snapshot.Buffers[b].ScheduledInbound += qty;
                                    break;
                                }
                            }

                            SpottedCarInfo carInfo = new SpottedCarInfo
                            {
                                CarId = car.id,
                                ReportingMark = opsCar?.DisplayName ?? car.DisplayName ?? string.Empty,
                                CarType = opsCar?.CarType ?? car.Definition?.CarType ?? "Car",
                                TrackName = "En Route",
                                CargoName = load.description ?? "Supply",
                                Units = load.units,
                                CurrentAmount = qty,
                                CapacityAmount = cap,
                                TransferRatePerMinute = 0f,
                                EstimatedTimeToCompletion = null,
                                StatusState = CarStatusState.EnRoute,
                                StatusDetail = "En Route"
                            };

                            enRouteGroup.Cars.Add(carInfo);
                            if (car.DisplayName is { } name) spottedCarIds.Add(name);
                            if (car.id is { } id) spottedCarIds.Add(id);
                            break;
                        }
                    }
                }

                if (enRouteGroup.Cars.Count > 0)
                {
                    snapshot.SpurGroups.Add(enRouteGroup);
                }
            }

            // 4. Alert on empty terminal supplies
            for (int b = 0; b < snapshot.Buffers.Count; b++)
            {
                BufferInfo buf = snapshot.Buffers[b];
                if (buf.CurrentAmount <= 0.001f)
                {
                    snapshot.DiagnosticAlert = $"{buf.CargoDisplayName} reserve depleted. Spot supply cars to replenish terminal.";
                    break;
                }
            }
        }
    }
}
