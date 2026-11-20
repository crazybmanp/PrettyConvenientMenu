using System;
using System.Collections.Generic;
using Game;
using Model;
using Model.Ops;
using Model.Ops.Definition;

namespace PrettyConvenientMenu.Data.Helpers
{
    public readonly struct BardoCarEntry
    {
        public readonly Car Car;
        public readonly IOpsCar OpsCar;
        public readonly GameDateTime ExpectedReturnTime;
        public readonly int PaidAmount;

        public BardoCarEntry(Car car, IOpsCar opsCar, GameDateTime expectedReturnTime, int paidAmount)
        {
            Car = car;
            OpsCar = opsCar;
            ExpectedReturnTime = expectedReturnTime;
            PaidAmount = paidAmount;
        }
    }

    /// <summary>
    /// Shared domain helpers for Interchange active checks, captive freight pricing,
    /// and off-map (Bardo) return schedules.
    /// </summary>
    public static class InterchangeHelper
    {
        /// <summary>
        /// Mirrors Railroader's <c>OpsController.EnabledInterchanges</c> check so disabled or
        /// progression-locked interchanges (e.g. Whittier after Sylva unlocks) are excluded.
        /// </summary>
        public static bool IsInterchangeActive(Industry? industry, Interchange? interchange = null)
        {
            if (industry == null)
            {
                return false;
            }

            interchange ??= industry.GetComponentInChildren<Interchange>();
            if (interchange == null)
            {
                return false;
            }

            return !interchange.Disabled
                && interchange.IsProgressionEnabled
                && interchange.IsTrackReachable
                && industry.IsProgressionEnabled
                && industry.IsTrackReachable;
        }

        /// <summary>
        /// Finds the <see cref="InterchangedIndustryLoader"/> on <paramref name="industry"/> that a car's
        /// waybill is targeting, if any.
        /// </summary>
        public static InterchangedIndustryLoader? FindTargetLoader(Industry industry, Waybill? waybill)
        {
            if (industry?.Components == null || !waybill.HasValue)
            {
                return null;
            }

            OpsCarPosition dest = waybill.Value.Destination;
            foreach (IndustryComponent comp in industry.Components)
            {
                if (comp is InterchangedIndustryLoader loader &&
                    (dest.Equals((OpsCarPosition)loader) ||
                     string.Equals(dest.Identifier, loader.Identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    return loader;
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates the exact goods + handling cost Railroader will charge at interchange service
        /// for a car ordering a load through <paramref name="loader"/>.
        /// </summary>
        public static bool TryCalculateLoaderOrderCost(
            IOpsCar opsCar,
            InterchangedIndustryLoader loader,
            out Load? load,
            out int goodsCost,
            out int handlingFee)
        {
            load = null;
            goodsCost = 0;
            handlingFee = 0;

            if (opsCar == null || loader == null)
            {
                return false;
            }

            load = loader.load;
            if (load == null)
            {
                return false;
            }

            if (load.carTypeFilter != null && !load.carTypeFilter.MatchesAll && !load.carTypeFilter.Matches(opsCar.CarType))
            {
                return false;
            }

            if (!opsCar.IsEmptyOrContains(load))
            {
                return false;
            }

            (float qty, float cap) = opsCar.QuantityOfLoad(load);
            float needed = cap - qty;
            if (needed <= load.ZeroThreshold)
            {
                return false;
            }

            goodsCost = InterchangePricing.CostForQuantity(load, needed);
            handlingFee = InterchangePricing.HandlingFeePerCar;
            return true;
        }

        /// <summary>
        /// Enumerates all cars currently off-map (<see cref="Car.IsInBardo"/>) scheduled to return
        /// through <paramref name="interchange"/> or any of its <see cref="InterchangedIndustryLoader"/>s.
        /// </summary>
        public static List<BardoCarEntry> EnumerateBardoCars(Industry industry, Interchange interchange, OpsController opsController)
        {
            List<BardoCarEntry> result = new List<BardoCarEntry>();
            if (industry == null || interchange == null || opsController == null || TrainController.Shared == null)
            {
                return result;
            }

            HashSet<string> seenCarIds = new HashSet<string>(StringComparer.Ordinal);

            void CollectFromSchedule(BardoSchedule schedule)
            {
                foreach (BardoSchedule.Group group in schedule.EnumerateGroups())
                {
                    if (group.CarIds == null) continue;
                    GameDateTime returnTime = interchange.GetServiceTimeAtOrAfter(group.ReturnTime);

                    for (int i = 0; i < group.CarIds.Count; i++)
                    {
                        string carId = group.CarIds[i];
                        if (string.IsNullOrEmpty(carId) || !seenCarIds.Add(carId)) continue;
                        if (!TrainController.Shared.TryGetCarForId(carId, out Car car) || car == null || !car.IsInBardo) continue;
                        if (opsController.CachedOpsCar(car) is not { } opsCar) continue;

                        result.Add(new BardoCarEntry(car, opsCar, returnTime, group.PaidAmount));
                    }
                }
            }

            CollectFromSchedule(interchange.BardoSchedule);

            if (industry.Components != null)
            {
                foreach (IndustryComponent comp in industry.Components)
                {
                    if (comp is InterchangedIndustryLoader loader)
                    {
                        CollectFromSchedule(loader.BardoSchedule);
                    }
                }
            }

            result.Sort((a, b) =>
                a.ExpectedReturnTime < b.ExpectedReturnTime ? -1 :
                (a.ExpectedReturnTime > b.ExpectedReturnTime ? 1 : 0));
            return result;
        }
    }
}
