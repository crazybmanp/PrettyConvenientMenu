using System;
using System.Collections.Generic;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.Spurs;
using UnityEngine;

namespace PrettyConvenientMenu.Data.RollingStock
{
    public static class EnRouteCarClassifier
    {
        public static void ClassifyEnRouteCars(Industry industry, FacilitySnapshot snapshot, OpsController? opsController = null)
        {
            if (snapshot.FacilityType is FacilityType.CommercialFreight or FacilityType.Depot) return;
            if (industry.Components is not { Count: > 0 } components || TrainController.Shared == null) return;

            opsController ??= OpsController.Shared;
            if (opsController == null) return;

            int spottedGroupCount = snapshot.CarGroups.Count;

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (car == null || IsAlreadySpotted(snapshot.CarGroups, spottedGroupCount, car)) continue;
                if (opsController.CachedOpsCar(car) is not { } opsCar) continue;

                // Check program maintenance repair modes first
                bool handledByRepair = false;
                foreach (IndustryComponent comp in components)
                {
                    if (comp is RepairTrack repairTrack &&
                        RepairTrackHelper.TryGetProgramRepairMode(car, repairTrack, opsController, out bool isOverhaul))
                    {
                        float demand = RepairTrackHelper.CalculateCarRepairPartsDemand(car);
                        foreach (BufferInfo buf in snapshot.Buffers)
                        {
                            if (buf is ReserveBuffer { IsRepairParts: true } rb)
                            {
                                rb.ProgramDemand += demand;
                                break;
                            }
                        }

                        SpottedCarInfo carInfo = new SpottedCarInfo(car, opsCar);
                        carInfo.MarkRepair(demand, CarStatus.EnRoute, isOverhaul ? "En Route (Overhaul)" : "En Route (Repair)", car.Condition);

                        string trackName = repairTrack.ShortName(industry) ?? "Repair Track";
                        snapshot.GetOrAddCarGroup(trackName, CarGroupRole.EnRouteRepair).Cars.Add(carInfo);
                        handledByRepair = true;
                        break;
                    }
                }
                if (handledByRepair) continue;

                if (opsCar.Waybill is not { } waybill) continue;

                // 1. Try exact destination identifier match on components (handles specific spurs and general destinations)
                IndustryComponent? destComp = null;
                string destId = waybill.Destination.Identifier;
                if (!string.IsNullOrEmpty(destId))
                {
                    foreach (IndustryComponent comp in components)
                    {
                        if (string.Equals(comp.Identifier, destId, StringComparison.OrdinalIgnoreCase))
                        {
                            destComp = comp;
                            break;
                        }
                    }
                }

                // 2. Fallback: match via IsWaybilledHere if destination identifier was not an exact component match
                if (destComp == null)
                {
                    if (!CarTrackingHelper.IsWaybilledHere(waybill, industry))
                    {
                        continue;
                    }

                    foreach (IndustryComponent comp in components)
                    {
                        if (CarTrackingHelper.IsWaybilledHere(waybill, industry, comp))
                        {
                            destComp = comp;
                            break;
                        }
                    }
                }

                if (destComp == null) continue;

                if (destComp is IndustryUnloader or StorageUnloader)
                {
                    (Load? inLoad, float qty, float cap) = CarTrackingHelper.GetCarCargo(car, opsCar);
                    if (inLoad == null || qty <= 0.001f) continue;

                    if (snapshot.Buffers.FindMatching(destComp, inLoad) is { } buf)
                    {
                        buf.ScheduledInbound += qty;
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo(car, opsCar);
                    carInfo.MarkEnRoute();
                    snapshot.GetOrAddCarGroup("En Route (Arriving)", CarGroupRole.EnRouteWaybill).Cars.Add(carInfo);
                    continue;
                }

                if (destComp is StorageLoader or IndustryLoaderBase or IndustrySeller || destComp.GetType().Name == "GeneralIndustryLoader")
                {
                    Load? outLoad = destComp is IndustryLoaderBase { load: { } lLoad }
                        ? lLoad
                        : (destComp.GetType().Name == "GeneralIndustryLoader"
                            ? destComp.GetType().GetProperty("TargetLoad")?.GetValue(destComp) as Load
                            : (!string.IsNullOrEmpty(waybill.Tag) ? CarLoadLibrary.GetLoad(waybill.Tag) : null));
                    if (outLoad == null) continue;

                    (float qty, float cap) = opsCar.QuantityOfLoad(outLoad);
                    if (snapshot.Buffers.FindMatching(destComp, outLoad) is { } buf)
                    {
                        buf.CommittedOutbound += Mathf.Max(0f, cap - qty);
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo(car, opsCar);
                    carInfo.SetLoad(outLoad, qty, cap);
                    carInfo.MarkEnRoute();
                    snapshot.GetOrAddCarGroup("En Route (Arriving)", CarGroupRole.EnRouteWaybill).Cars.Add(carInfo);
                    continue;
                }
            }
        }

        private static bool IsAlreadySpotted(List<FacilityCarGroup> groups, int count, Car car)
        {
            for (int i = 0; i < count; i++)
            {
                foreach (SpottedCarInfo info in groups[i].Cars)
                {
                    if (info.Car == car) return true;
                }
            }
            return false;
        }
    }
}
