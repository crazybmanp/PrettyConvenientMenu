using System;
using System.Collections.Generic;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.UI.Common;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Helpers
{
    public static class RepairTrackHelper
    {
        public const float PoundsPerRepairUnit = 12000f;

        private static readonly Func<Car, float>? calculateRepairWorkOverall =
            AccessTools.MethodDelegate<Func<Car, float>>(
                AccessTools.Method(typeof(RepairTrack), "CalculateRepairWorkOverall", new[] { typeof(Car) }));

        public static float CalculateCarRepairPartsDemand(Car? car)
        {
            if (ReferenceEquals(car, null) || car == null)
            {
                return 0f;
            }

            if (calculateRepairWorkOverall != null)
            {
                try
                {
                    float workUnits = calculateRepairWorkOverall(car);
                    return Mathf.Max(0f, workUnits * PoundsPerRepairUnit);
                }
                catch
                {
                    float targetCond = car.RepairCap;
                    return Mathf.Max(0f, targetCond - car.Condition) * PoundsPerRepairUnit;
                }
            }

            return 0f;
        }

        public static void CollectRepairTrackCars(
            OpsController opsController,
            RepairTrack repairTrack,
            Industry industry,
            IndustrySnapshot snapshot,
            HashSet<string>? spottedCarIds = null)
        {
            spottedCarIds ??= new HashSet<string>(16);

            ReserveBuffer? repairBuffer = FindOrAddRepairBuffer(repairTrack, industry, snapshot);

            string trackName = repairTrack.ShortName(industry) ?? "Repair Track";
            SpurCarGroup spurGroup = BufferHelper.GetOrAddSpurGroup(snapshot.SpurGroups, trackName, SpurRole.LocomotiveRepair);

            IEnumerable<Car>? cars = opsController.CarsAtPosition(repairTrack);
            if (cars != null)
            {
                foreach (Car car in cars)
                {
                    if (ReferenceEquals(car, null) || car == null || Mathf.Abs(car.velocity) > 0.05f)
                    {
                        continue;
                    }

                    spottedCarIds.Add(car.DisplayName);
                    spottedCarIds.Add(car.id);

                    IOpsCar? opsCar = opsController.CachedOpsCar(car);
                    string mark = opsCar?.DisplayName ?? car.DisplayName;
                    string carType = opsCar?.CarType ?? car.Definition?.CarType ?? "Car";

                    bool hasOverride = car.TryGetOverrideDestination(OverrideDestination.Repair, opsController, out (OpsCarPosition position, string tag)? overrideDest)
                                       && overrideDest.HasValue
                                       && string.Equals(overrideDest.Value.position.Identifier, repairTrack.Identifier, StringComparison.OrdinalIgnoreCase);

                    string? overrideTag = hasOverride ? overrideDest!.Value.tag : null;

                    bool isOverhaul = (hasOverride && string.Equals(overrideTag, "overhaul", StringComparison.OrdinalIgnoreCase))
                                      || car.OverhaulProgress > 0f
                                      || (car.CarType.IsTender && car.TryGetAdjacentCar(car.EndToLogical(Car.End.F), out Car adjacent) && IsCarInForOverhaul(adjacent, repairTrack, opsController));

                    bool isProgram = hasOverride || isOverhaul;
                    float targetCond = isOverhaul ? 1.0f : car.RepairCap;
                    bool isServiced = car.Condition >= targetCond - 0.001f && !car.NeedsOiling && !car.HasHotbox;

                    CarStatusState statusState;
                    string statusDetail;
                    float partsDemand;
                    string customLoad;

                    if (isServiced)
                    {
                        statusState = CarStatusState.Serviced;
                        statusDetail = "Serviced (Ready)";
                        partsDemand = 0f;
                        customLoad = "--";
                    }
                    else if (isProgram)
                    {
                        statusState = isOverhaul ? CarStatusState.ProgramOverhaul : CarStatusState.ProgramRepair;
                        statusDetail = isOverhaul ? "Program Overhaul" : "Program Repair";
                        partsDemand = CalculateCarRepairPartsDemand(car);
                        customLoad = $"{FormatHelper.FormatWeight(partsDemand)} Parts ({Mathf.RoundToInt(car.Condition * 100f)}% -> {Mathf.RoundToInt(targetCond * 100f)}%)";

                        if (repairBuffer != null)
                        {
                            repairBuffer.ProgramDemand += partsDemand;
                        }
                    }
                    else
                    {
                        statusState = CarStatusState.SpotRepair;
                        statusDetail = "Spot Repair";
                        partsDemand = CalculateCarRepairPartsDemand(car);
                        customLoad = $"{FormatHelper.FormatWeight(partsDemand)} Parts ({Mathf.RoundToInt(car.Condition * 100f)}% -> {Mathf.RoundToInt(targetCond * 100f)}%)";

                        if (repairBuffer != null)
                        {
                            repairBuffer.SpotDemand += partsDemand;
                        }
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        CarId = car.id,
                        ReportingMark = mark,
                        CarType = carType,
                        TrackName = trackName,
                        CargoName = isProgram ? (isOverhaul ? "Program Overhaul" : "Program Repair") : "Spot Repair",
                        Units = LoadUnits.Pounds,
                        CurrentAmount = partsDemand,
                        CapacityAmount = PoundsPerRepairUnit,
                        TransferRatePerMinute = 0f,
                        EstimatedTimeToCompletion = null,
                        StatusState = statusState,
                        StatusDetail = statusDetail,
                        CustomLoadString = customLoad
                    };

                    spurGroup.Cars.Add(carInfo);
                }
            }

            // Map-wide scan for arriving equipment routed for repair/overhaul at this repair track
            if (TrainController.Shared != null)
            {
                SpurCarGroup? enRouteRepairGroup = null;

                foreach (Car car in TrainController.Shared.Cars)
                {
                    if (ReferenceEquals(car, null) || car == null || spottedCarIds.Contains(car.DisplayName) || spottedCarIds.Contains(car.id))
                    {
                        continue;
                    }

                    if (!car.TryGetOverrideDestination(OverrideDestination.Repair, opsController, out (OpsCarPosition position, string tag)? result) || !result.HasValue)
                    {
                        continue;
                    }

                    if (!string.Equals(result.Value.position.Identifier, repairTrack.Identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    IOpsCar? opsCar = opsController.CachedOpsCar(car);
                    string mark = opsCar?.DisplayName ?? car.DisplayName;
                    string carType = opsCar?.CarType ?? car.Definition?.CarType ?? "Car";

                    bool isOverhaul = string.Equals(result.Value.tag, "overhaul", StringComparison.OrdinalIgnoreCase)
                                      || car.OverhaulProgress > 0f
                                      || (car.CarType.IsTender && car.TryGetAdjacentCar(car.EndToLogical(Car.End.F), out Car adjacent) && IsCarInForOverhaul(adjacent, repairTrack, opsController));

                    float targetCond = isOverhaul ? 1.0f : car.RepairCap;
                    float partsDemand = CalculateCarRepairPartsDemand(car);

                    if (repairBuffer != null)
                    {
                        repairBuffer.ProgramDemand += partsDemand;
                    }

                    if (enRouteRepairGroup == null)
                    {
                        enRouteRepairGroup = new SpurCarGroup
                        {
                            TrackName = trackName,
                            Role = SpurRole.EnRouteRepair
                        };
                    }

                    SpottedCarInfo carInfo = new SpottedCarInfo
                    {
                        CarId = car.id,
                        ReportingMark = mark,
                        CarType = carType,
                        TrackName = trackName,
                        CargoName = isOverhaul ? "Program Overhaul" : "Program Repair",
                        Units = LoadUnits.Pounds,
                        CurrentAmount = partsDemand,
                        CapacityAmount = PoundsPerRepairUnit,
                        TransferRatePerMinute = 0f,
                        EstimatedTimeToCompletion = null,
                        StatusState = CarStatusState.EnRoute,
                        StatusDetail = "En Route",
                        CustomLoadString = $"{FormatHelper.FormatWeight(partsDemand)} Parts ({Mathf.RoundToInt(car.Condition * 100f)}% -> {Mathf.RoundToInt(targetCond * 100f)}%)"
                    };

                    enRouteRepairGroup.Cars.Add(carInfo);
                    spottedCarIds.Add(car.DisplayName);
                    spottedCarIds.Add(car.id);
                }

                if (enRouteRepairGroup != null && enRouteRepairGroup.Cars.Count > 0)
                {
                    snapshot.SpurGroups.Add(enRouteRepairGroup);
                }
            }
        }

        private static ReserveBuffer? FindOrAddRepairBuffer(RepairTrack repairTrack, Industry industry, IndustrySnapshot snapshot)
        {
            ReserveBuffer? repairBuffer = null;
            for (int b = 0; b < snapshot.Buffers.Count; b++)
            {
                if (snapshot.Buffers[b] is ReserveBuffer rb &&
                    (string.Equals(rb.CargoType, repairTrack.repairPartsLoadId, StringComparison.OrdinalIgnoreCase) ||
                     rb.CargoDisplayName.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    repairBuffer = rb;
                    break;
                }
            }

            if (repairBuffer == null && !string.IsNullOrEmpty(repairTrack.repairPartsLoadId))
            {
                Load? load = CarLoadLibrary.GetLoad(repairTrack.repairPartsLoadId);
                if (load != null)
                {
                    repairBuffer = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.LocomotiveSupply);
                    repairBuffer.CurrentAmount = industry.Storage != null
                        ? industry.Storage.QuantityInStorage(load)
                        : 0f;
                    repairBuffer.MaxCapacity = Mathf.Max(repairBuffer.CurrentAmount, 50000f);
                }
            }

            if (repairBuffer != null)
            {
                repairBuffer.IsRepairParts = true;
            }

            return repairBuffer;
        }

        private static bool IsCarInForOverhaul(Car car, RepairTrack repairTrack, OpsController opsController, int depth = 0)
        {
            if (depth > 3 || ReferenceEquals(car, null) || car == null)
            {
                return false;
            }

            if (car.TryGetOverrideDestination(OverrideDestination.Repair, opsController, out (OpsCarPosition position, string tag)? result)
                && result.HasValue
                && string.Equals(result.Value.position.Identifier, repairTrack.Identifier, StringComparison.OrdinalIgnoreCase)
                && string.Equals(result.Value.tag, "overhaul", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (car.OverhaulProgress > 0f)
            {
                return true;
            }

            if (car.CarType.IsTender && car.TryGetAdjacentCar(car.EndToLogical(Car.End.F), out Car adjacent))
            {
                return IsCarInForOverhaul(adjacent, repairTrack, opsController, depth + 1);
            }

            return false;
        }
    }
}
