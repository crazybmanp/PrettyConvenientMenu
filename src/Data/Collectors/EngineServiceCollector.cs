using System;
using System.Collections.Generic;
using Model;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class EngineServiceCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.EngineService;

        public void PopulateFacilityMetadata(Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Locomotive Service Facility";
            snapshot.State = ProductionState.Running;
            snapshot.StatusMessage = "Terminal reserves ready for locomotive fueling, servicing, and repair.";
            snapshot.DiagnosticAlert = string.Empty;
        }

        public void PopulateBuffers(Industry industry, IndustrySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null) return;

            RepairTrack? repairTrack = null;
            foreach (IndustryComponent comp in components)
            {
                if (comp is RepairTrack rt)
                {
                    repairTrack = rt;
                    break;
                }
            }

            foreach (IndustryComponent component in components)
            {
                if (component is IndustryUnloader unloader && unloader.load is { } load)
                {
                    string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float currentStock = industry.Storage != null
                        ? industry.Storage.QuantityInStorage(load, prefix)
                        : 0f;

                    bool isRepairParts = repairTrack != null &&
                        (string.Equals(load.id, repairTrack.repairPartsLoadId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(unloader.ShortName(industry), repairTrack.ShortName(industry), StringComparison.OrdinalIgnoreCase));

                    BufferRole role = isRepairParts ? BufferRole.MaintenanceReserve : BufferRole.LocomotiveSupply;

                    ReserveBuffer buffer = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, role);
                    buffer.CurrentAmount = currentStock;
                    buffer.MaxCapacity = Mathf.Max(0.001f, unloader.maxStorage);
                    buffer.IsRepairParts = isRepairParts;
                }
            }
        }

        public void ScanEnRouteTraffic(Industry industry, IndustrySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null || TrainController.Shared == null) return;

            OpsController? opsController = OpsController.Shared;
            if (opsController == null) return;

            HashSet<string> spottedCarIds = new HashSet<string>(16);
            foreach (SpurCarGroup group in snapshot.SpurGroups)
            {
                foreach (SpottedCarInfo carInfo in group.Cars)
                {
                    spottedCarIds.Add(carInfo.ReportingMark);
                    if (carInfo.CarId is { } id) spottedCarIds.Add(id);
                }
            }

            // En-route cars are discovered on active trains in transit across the world map.
            // Unlike spotted cars (which are discovered and bulk-created by SpurClassifier on physical tracks),
            // arriving en-route cars are instantiated individually here for the "En Route (Arriving)" display group.
            SpurCarGroup? enRouteGroup = null;

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (ReferenceEquals(car, null) || car == null ||
                    (car.DisplayName is { } carName && spottedCarIds.Contains(carName)) ||
                    (car.id is { } carIdentifier && spottedCarIds.Contains(carIdentifier)))
                {
                    continue;
                }

                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar?.Waybill is not { } waybill) continue;

                foreach (IndustryComponent component in components)
                {
                    if (component is IndustryUnloader unloader && unloader.load is { } load && CarTrackingHelper.IsWaybilledHere(waybill, industry, unloader))
                    {
                        (float qty, float cap) = opsCar.QuantityOfLoad(load);

                        foreach (BufferInfo buffer in snapshot.Buffers)
                        {
                            if (buffer.CargoType == load.id)
                            {
                                buffer.ScheduledInbound += qty;
                                break;
                            }
                        }

                        if (enRouteGroup is null)
                        {
                            enRouteGroup = new SpurCarGroup
                            {
                                TrackName = "En Route (Arriving)",
                                Role = SpurRole.EnRouteWaybill
                            };
                        }

                        enRouteGroup.Cars.Add(SpottedCarInfo.CreateEnRoute(car, opsCar, load, qty, cap));
                        if (car.DisplayName is { } name) spottedCarIds.Add(name);
                        if (car.id is { } id) spottedCarIds.Add(id);
                        break;
                    }
                }
            }

            if (enRouteGroup is { Cars: { Count: > 0 } })
            {
                snapshot.SpurGroups.Add(enRouteGroup);
            }
        }

        public void EvaluateFacilityDiagnostics(Industry industry, IndustrySnapshot snapshot)
        {
            // Engine terminals are servicing depots with optional supplies (e.g. diesel, coal, sand, repair parts).
            // Empty reserves are normal operational state (e.g. running an all-steam roster) and do not halt operations.
            snapshot.DiagnosticAlert = string.Empty;
        }
    }
}
