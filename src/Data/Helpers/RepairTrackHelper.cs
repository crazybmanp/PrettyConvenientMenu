using System;
using HarmonyLib;
using Model;
using Model.Ops;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Helpers
{
    public static class RepairTrackHelper
    {
        public const float PoundsPerRepairUnit = 12000f;

        private delegate float EffectiveRepairPerDayDelegate(RepairTrack instance, out float payRate);

        private static readonly Func<Car, float>? calculateRepairWorkOverall =
            AccessTools.MethodDelegate<Func<Car, float>>(
                AccessTools.Method(typeof(RepairTrack), "CalculateRepairWorkOverall", new[] { typeof(Car) }));

        private static readonly EffectiveRepairPerDayDelegate? effectiveRepairPerDayPerCar =
            AccessTools.MethodDelegate<EffectiveRepairPerDayDelegate>(
                AccessTools.Method(typeof(RepairTrack), "EffectiveRepairPerDayPerCar"));

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

        public static TimeSpan? CalculateRepairEta(float partsDemand, RepairTrack repairTrack)
        {
            if (partsDemand <= 0.001f || repairTrack == null || effectiveRepairPerDayPerCar == null)
            {
                return null;
            }

            try
            {
                float repairPerDay = effectiveRepairPerDayPerCar(repairTrack, out _);
                if (repairPerDay <= 0.0001f)
                {
                    return null;
                }

                float workUnits = partsDemand / PoundsPerRepairUnit;
                return TimeSpan.FromMinutes(workUnits / repairPerDay * 1440f);
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGetProgramRepairMode(
            Car car,
            RepairTrack repairTrack,
            OpsController opsController,
            out bool isOverhaul,
            int depth = 0)
        {
            isOverhaul = false;
            if (depth > 3 || ReferenceEquals(car, null) || car == null)
            {
                return false;
            }

            if (car.TryGetOverrideDestination(OverrideDestination.Repair, opsController, out (OpsCarPosition position, string tag)? result)
                && result.HasValue
                && string.Equals(result.Value.position.Identifier, repairTrack.Identifier, StringComparison.OrdinalIgnoreCase))
            {
                isOverhaul = string.Equals(result.Value.tag, "overhaul", StringComparison.OrdinalIgnoreCase) || car.OverhaulProgress > 0f;
                return true;
            }

            // Base game RepairTrack.InForOverhaul: Tenders coupled at the front end (End.F) inherit the overhaul status of their attached locomotive.
            if (car.CarType.IsTender && car.TryGetAdjacentCar(car.EndToLogical(Car.End.F), out Car adjacent) && adjacent != null)
            {
                return TryGetProgramRepairMode(adjacent, repairTrack, opsController, out isOverhaul, depth + 1);
            }

            return false;
        }
    }
}
