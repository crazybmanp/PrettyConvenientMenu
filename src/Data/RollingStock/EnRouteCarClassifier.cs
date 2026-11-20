using System;
using System.Collections.Generic;
using Game;
using Model;
using Model.Ops;
using Model.Ops.AutoDestinations;
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
            if (industry.Components is not { Count: > 0 } components || TrainController.Shared == null) return;

            opsController ??= OpsController.Shared;
            if (opsController == null) return;

            int spottedGroupCount = snapshot.CarGroups.Count;

            if (snapshot.FacilityType == FacilityType.Interchange)
            {
                ClassifyInterchangeBardoCars(industry, snapshot, opsController);
            }

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

                // Area-level and zone-level General Waybilling destinations do not target a single real
                // industry; skip them so we don't show or count ambiguous cars on facilities.
                string destId = waybill.Destination.Identifier;
                if (!string.IsNullOrEmpty(destId) &&
                    (destId.StartsWith("gw_area_", StringComparison.OrdinalIgnoreCase) ||
                     destId.StartsWith("gw_zone_", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // 1. Try exact destination identifier match on components (handles specific spurs and general destinations)
                IndustryComponent? destComp = null;
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

                if (destComp == null || car.IsInBardo) continue;

                if (destComp is Interchange)
                {
                    SpottedCarInfo carInfo = new SpottedCarInfo(car, opsCar);
                    string detail = !opsCar.IsOwnedByPlayer
                        ? "Outbound"
                        : (waybill.Tag == "sell" ? "Selling" : "En Route");
                    carInfo.MarkEnRoute(detail);
                    snapshot.GetOrAddCarGroup("En Route (Arriving)", CarGroupRole.EnRouteWaybill).Cars.Add(carInfo);
                    continue;
                }

                if (destComp is InterchangedIndustryLoader icLoader)
                {
                    SpottedCarInfo carInfo = new SpottedCarInfo(car, opsCar);
                    if (InterchangeHelper.TryCalculateLoaderOrderCost(opsCar, icLoader, out Load? orderLoad, out int goodsCost, out int handlingFee) &&
                        orderLoad != null)
                    {
                        if (snapshot.InterchangeInfo != null)
                        {
                            snapshot.InterchangeInfo.EnRouteOrderCount++;
                            snapshot.InterchangeInfo.EnRouteTotalCost += goodsCost + handlingFee;
                        }

                        (float qty, float cap) = opsCar.QuantityOfLoad(orderLoad);
                        carInfo.SetLoad(orderLoad, qty, cap);
                        carInfo.MarkEnRoute($"Order {orderLoad.description} ({goodsCost + handlingFee:C0})");
                    }
                    else
                    {
                        carInfo.MarkEnRoute($"En Route ({icLoader.load?.description ?? "Load"})");
                    }

                    snapshot.GetOrAddCarGroup("En Route (Arriving)", CarGroupRole.EnRouteWaybill).Cars.Add(carInfo);
                    continue;
                }

                bool isGeneralComponent = CarTrackingHelper.TryGetGeneralComponentInfo(destComp, out Load? generalLoad, out AutoDestinationType generalDestType);

                if (destComp is IndustryUnloader or StorageUnloader || (isGeneralComponent && generalDestType == AutoDestinationType.Load))
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

                if (destComp is TeamTrack)
                {
                    (Load? inLoad, float qty, float cap) = CarTrackingHelper.GetCarCargo(car, opsCar);
                    SpottedCarInfo carInfo = new SpottedCarInfo(car, opsCar);
                    if (inLoad != null)
                    {
                        carInfo.SetLoad(inLoad, qty, cap);
                    }
                    carInfo.MarkEnRoute();
                    snapshot.GetOrAddCarGroup("En Route (Arriving)", CarGroupRole.EnRouteWaybill).Cars.Add(carInfo);
                    continue;
                }

                if (destComp is StorageLoader or IndustryLoaderBase or IndustrySeller || (isGeneralComponent && generalDestType == AutoDestinationType.Empty))
                {
                    Load? outLoad = destComp is IndustryLoaderBase { load: { } lLoad }
                        ? lLoad
                        : (destComp is IndustrySeller seller && !string.IsNullOrEmpty(seller.loadId)
                            ? CarLoadLibrary.GetLoad(seller.loadId)
                            : (isGeneralComponent
                                ? generalLoad
                                : (!string.IsNullOrEmpty(waybill.Tag) ? CarLoadLibrary.GetLoad(waybill.Tag) : null)));
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

        private static void ClassifyInterchangeBardoCars(Industry industry, FacilitySnapshot snapshot, OpsController opsController)
        {
            Interchange? interchange = industry.GetComponentInChildren<Interchange>();
            if (interchange == null) return;

            List<BardoCarEntry> entries = InterchangeHelper.EnumerateBardoCars(industry, interchange, opsController);
            if (entries.Count == 0) return;

            FacilityCarGroup bardoGroup = snapshot.GetOrAddCarGroup("Off-Map (Returning)", CarGroupRole.InterchangeBardo);
            GameDateTime now = TimeWeather.Now;

            for (int i = 0; i < entries.Count; i++)
            {
                BardoCarEntry entry = entries[i];
                SpottedCarInfo carInfo = new SpottedCarInfo(entry.Car, entry.OpsCar);

                double daysRemaining = Math.Max(0.0, (entry.ExpectedReturnTime - now) * (1.0 / 86400.0));
                TimeSpan eta = TimeSpan.FromDays(daysRemaining);

                string detail = entry.PaidAmount > 0 ? "Purchased" : "Off-Map";
                if (entry.OpsCar.Waybill is { } wb)
                {
                    string dest = !string.IsNullOrEmpty(wb.Destination.DisplayName) ? wb.Destination.DisplayName : wb.Destination.Identifier;
                    if (!string.IsNullOrEmpty(dest))
                    {
                        if (dest.Length > 24) dest = dest.Substring(0, 22) + "...";
                        detail = $"To {dest}";
                    }
                }

                carInfo.MarkEnRoute(detail, eta);
                bardoGroup.Cars.Add(carInfo);
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
