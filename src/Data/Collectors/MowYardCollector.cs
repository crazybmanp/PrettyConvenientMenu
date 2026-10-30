using System.Collections.Generic;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class MowYardCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.MaintenanceOfWay;

        public void PopulateFacilityMetadata(Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Maintenance of Way Material Depot";
            snapshot.State = ProductionState.Running;
            snapshot.StatusMessage = "Material reserves and staging tracks for track maintenance work trains.";
            snapshot.DiagnosticAlert = string.Empty;
        }

        public void PopulateBuffers(Industry industry, IndustrySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null) return;

            foreach (IndustryComponent component in components)
            {
                if (component is StorageUnloader storageUnloader)
                {
                    string? prefix = !storageUnloader.sharedStorage ? storageUnloader.subIdentifier : null;
                    foreach (StorageUnloader.LoadEntry entry in storageUnloader.loads)
                    {
                        if (entry.load is not { } load) continue;

                        float capacity = entry.storage * load.NominalQuantityPerCarLoad;
                        float currentStock = industry.Storage?.QuantityInStorage(load, prefix) ?? 0f;

                        ReserveBuffer buf = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.MaintenanceReserve);
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = Mathf.Max(0.001f, capacity);
                    }
                }
                else if (component is StorageLoader storageLoader)
                {
                    string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                    foreach (StorageLoader.LoadEntry entry in storageLoader.loads)
                    {
                        if (entry.load is not { } load) continue;

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
        }

        public void ScanEnRouteTraffic(Industry industry, IndustrySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null || TrainController.Shared == null) return;

            OpsController? opsController = OpsController.Shared;
            if (opsController == null) return;

            StorageLoader? storageLoader = null;
            StorageUnloader? storageUnloader = null;
            foreach (IndustryComponent comp in components)
            {
                if (comp is StorageLoader sl) storageLoader = sl;
                else if (comp is StorageUnloader su) storageUnloader = su;
            }

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
                if (car == null || spottedCarIds.Contains(car.id) || (car.DisplayName is { } name && spottedCarIds.Contains(name)))
                {
                    continue;
                }

                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar?.Waybill is not { } waybill) continue;

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

                    if (enRouteGroup == null)
                    {
                        enRouteGroup = new SpurCarGroup
                        {
                            TrackName = "En Route (Arriving)",
                            Role = SpurRole.EnRouteWaybill
                        };
                    }

                    enRouteGroup.Cars.Add(SpottedCarInfo.CreateEnRoute(car, opsCar, targetLoad, q, c));
                    spottedCarIds.Add(car.id);
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

                    if (enRouteGroup == null)
                    {
                        enRouteGroup = new SpurCarGroup
                        {
                            TrackName = "En Route (Arriving)",
                            Role = SpurRole.EnRouteWaybill
                        };
                    }

                    enRouteGroup.Cars.Add(SpottedCarInfo.CreateEnRoute(car, opsCar, actualLoad, actualQty, actualCap));
                    spottedCarIds.Add(car.id);
                }
            }

            if (enRouteGroup != null && enRouteGroup.Cars.Count > 0)
            {
                snapshot.SpurGroups.Add(enRouteGroup);
            }
        }

        public void EvaluateFacilityDiagnostics(Industry industry, IndustrySnapshot snapshot)
        {
            // MOW yards are passive stockpiles and staging tracks for work trains, not active manufacturing facilities.
            // Empty reserves are normal and do not halt operations.
            snapshot.DiagnosticAlert = string.Empty;
        }
    }
}
