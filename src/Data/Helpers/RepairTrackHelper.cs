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

        public static bool IsCarInForOverhaul(Car car, RepairTrack repairTrack, OpsController opsController, int depth = 0)
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
