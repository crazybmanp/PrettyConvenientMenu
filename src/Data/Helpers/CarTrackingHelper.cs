using System;
using System.Collections.Generic;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Helpers
{
    public static class CarTrackingHelper
    {
        public static (Load? load, float quantity, float capacity) GetCarCargo(Car? car, IOpsCar? opsCar)
        {
            if (ReferenceEquals(car, null) || car == null || car.Definition == null || car.Definition.LoadSlots == null)
            {
                return (null, 0f, 0f);
            }

            for (int i = 0; i < car.Definition.LoadSlots.Count; i++)
            {
                CarLoadInfo? info = car.GetLoadInfo(i);
                if (info.HasValue && !string.IsNullOrEmpty(info.Value.LoadId) && info.Value.Quantity > 0.001f)
                {
                    Load load = CarLoadLibrary.GetLoad(info.Value.LoadId);
                    float cap = car.Definition.LoadSlots[i].CalculateMaximumCapacity(load, car);
                    return (load, info.Value.Quantity, cap);
                }
            }

            return (null, 0f, 0f);
        }

        public static bool IsWaybilledHere(Waybill? waybill, Industry? industry, IndustryComponent? specificComponent = null)
        {
            if (!waybill.HasValue || industry == null)
            {
                return false;
            }

            if (specificComponent != null)
            {
                return string.Equals(waybill.Value.Destination.Identifier, specificComponent.Identifier, StringComparison.OrdinalIgnoreCase)
                    || waybill.Value.Destination.Equals((OpsCarPosition)specificComponent);
            }

            if (industry.Components != null)
            {
                foreach (IndustryComponent c in industry.Components)
                {
                    if (string.Equals(waybill.Value.Destination.Identifier, c.Identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (waybill.Value.Destination.Equals((OpsCarPosition)c))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(industry.identifier) && !string.IsNullOrEmpty(waybill.Value.Destination.Identifier))
            {
                if (waybill.Value.Destination.Identifier.StartsWith(industry.identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static void CollectTeamTrackCars(
            OpsController opsController,
            TeamTrack teamTrack,
            IndustryComponent component,
            Industry industry,
            IndustrySnapshot snapshot)
        {
            string trackName = component.ShortName(industry) ?? "House Track";
            SpurRole role = snapshot.FacilityType == IndustryFacilityType.Depot
                ? SpurRole.DepotHouseTrack
                : SpurRole.TeamTrack;
            SpurCarGroup spurGroup = BufferHelper.GetOrAddSpurGroup(snapshot.SpurGroups, trackName, role);

            IEnumerable<Car>? cars = opsController.CarsAtPosition(teamTrack);
            if (cars == null)
            {
                return;
            }

            foreach (Car car in cars)
            {
                if (car == null || Mathf.Abs(car.velocity) > 0.05f)
                {
                    continue;
                }

                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar == null || opsCar.Waybill is not { } waybill)
                {
                    continue;
                }

                if (!IsWaybilledHere(waybill, industry, component))
                {
                    continue;
                }

                TeamTrackProfile.Entry? matchedEntry = null;
                for (int p = 0; p < teamTrack.profileEntries.Count; p++)
                {
                    if (teamTrack.profileEntries[p].tag == waybill.Tag)
                    {
                        matchedEntry = teamTrack.profileEntries[p];
                        break;
                    }
                }

                if (!matchedEntry.HasValue || matchedEntry.Value.load is not { } ttLoad)
                {
                    continue;
                }

                (float qty, float cap) = opsCar.QuantityOfLoad(ttLoad);
                float loadingTimeDays = Mathf.Max(0.1f, matchedEntry.Value.loadingTime);
                float ratePerDay = cap / loadingTimeDays;
                float ratePerMin = ratePerDay / 1440f;
                bool isExport = matchedEntry.Value.export;

                if (isExport)
                {
                    float toFill = Mathf.Max(0f, cap - qty);
                    TimeSpan? eta = null;
                    if (ratePerMin > 0.0001f && toFill > 0.001f)
                    {
                        float minutes = toFill / ratePerMin;
                        eta = TimeSpan.FromMinutes(minutes);
                    }

                    CarStatusState statusState;
                    string statusDetail;
                    if (toFill <= 0.001f)
                    {
                        statusState = CarStatusState.Completed;
                        statusDetail = "Completed";
                    }
                    else if (ratePerMin > 0.0001f && toFill > 0.001f)
                    {
                        statusState = CarStatusState.ActiveTransfer;
                        statusDetail = "Loading";
                    }
                    else
                    {
                        statusState = CarStatusState.Idle;
                        statusDetail = "Idle";
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        CarId = car.id,
                        ReportingMark = opsCar.DisplayName ?? opsCar.Id ?? string.Empty,
                        CarType = opsCar.CarType ?? "Car",
                        TrackName = trackName,
                        CargoName = ttLoad.description ?? "Cargo",
                        Units = ttLoad.units,
                        CurrentAmount = qty,
                        CapacityAmount = cap,
                        TransferRatePerMinute = ratePerMin,
                        EstimatedTimeToCompletion = eta,
                        StatusState = statusState,
                        StatusDetail = statusDetail
                    };
                    spurGroup.Cars.Add(carInfo);
                }
                else
                {
                    TimeSpan? eta = null;
                    if (ratePerMin > 0.0001f && qty > 0.001f)
                    {
                        float minutes = qty / ratePerMin;
                        eta = TimeSpan.FromMinutes(minutes);
                    }

                    CarStatusState statusState;
                    string statusDetail;
                    if (qty <= 0.001f)
                    {
                        statusState = CarStatusState.Completed;
                        statusDetail = "Completed";
                    }
                    else if (ratePerMin > 0.0001f && qty > 0.001f)
                    {
                        statusState = CarStatusState.ActiveTransfer;
                        statusDetail = "Unloading";
                    }
                    else
                    {
                        statusState = CarStatusState.Idle;
                        statusDetail = "Idle";
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        CarId = car.id,
                        ReportingMark = opsCar.DisplayName ?? opsCar.Id ?? string.Empty,
                        CarType = opsCar.CarType ?? "Car",
                        TrackName = trackName,
                        CargoName = ttLoad.description ?? "Cargo",
                        Units = ttLoad.units,
                        CurrentAmount = qty,
                        CapacityAmount = cap,
                        TransferRatePerMinute = ratePerMin,
                        EstimatedTimeToCompletion = eta,
                        StatusState = statusState,
                        StatusDetail = statusDetail
                    };
                    spurGroup.Cars.Add(carInfo);
                }
            }
        }
    }
}
