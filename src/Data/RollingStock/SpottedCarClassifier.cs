using System;
using System.Collections.Generic;
using Model;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.Spurs;
using UnityEngine;

namespace PrettyConvenientMenu.Data.RollingStock
{
    /// <summary>
    /// Evaluates and tracks the operational and passive lifecycle states of rolling stock spotted on industry spurs.
    /// </summary>
    public static class SpottedCarClassifier
    {
        /// <summary>
        /// Instantiates tracked car records for stationary cars on a physical spur and classifies their status in-place.
        /// </summary>
        public static void ClassifySpurCars(
            SpurCarGroup spurGroup,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            IEnumerable<Car> carsOnTrack,
            Industry industry,
            IndustrySnapshot snapshot,
            OpsController opsController,
            SpurCapabilities caps,
            SpurRole role)
        {
            spurGroup.Cars.Clear();
            int trackIndex = 0;
            foreach (Car car in carsOnTrack)
            {
                if (car == null || Mathf.Abs(car.velocity) > 0.05f) continue;
                if (opsController.CachedOpsCar(car) is not { } opsCar) continue;
                spurGroup.Cars.Add(new SpottedCarInfo(car, opsCar, trackIndex++));
            }

            if (spurGroup.Cars.Count == 0) return;

            ReserveBuffer? repairBuffer = BufferHelper.FindRepairBuffer(componentsOnSpur, snapshot.Buffers, caps);
            float contractMultiplier = industry.GetContractMultiplier();

            // Sequence FIFO unloader/loader queues (Commercial & Storage) across the spur
            TransferQueueHelper.SequenceSpurQueues(
                spurGroup.Cars, componentsOnSpur, industry, snapshot.Buffers, contractMultiplier);

            // Evaluate each car on the spur through the classification hierarchy
            foreach (SpottedCarInfo carInfo in spurGroup.Cars)
            {
                ClassifyCar(carInfo, componentsOnSpur, industry, repairBuffer, opsController, role);
            }
        }

        private static void ClassifyCar(
            SpottedCarInfo carInfo,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            Industry industry,
            ReserveBuffer? repairBuffer,
            OpsController opsController,
            SpurRole role)
        {
            // Step 1: Active Component Work (Queue already classified, or RepairTrack / TeamTrack match)
            if (carInfo.IsClassified || TryClassifyComponentWork(carInfo, componentsOnSpur, industry, repairBuffer, opsController))
            {
                return;
            }

            IOpsCar opsCar = carInfo.OpsCar;

            // Step 2: Finished Work Here (Ordered away from this facility and awaiting pickup)
            if (CarTrackingHelper.IsWaybilledFromHere(opsCar.Waybill, industry))
            {
                carInfo.MarkReady();
                return;
            }

            // Step 3: Foreign Waybill (Staged on maintenance tracks vs Misspotted on commercial tracks)
            if (opsCar.Waybill is { } waybill)
            {
                if (role is SpurRole.MowMaterialHandling or SpurRole.MowStaging)
                {
                    carInfo.MarkPassive("Staged");
                }
                else
                {
                    string destName = !string.IsNullOrEmpty(waybill.Destination.DisplayName)
                        ? waybill.Destination.DisplayName
                        : waybill.Destination.Identifier;
                    if (destName.Length > 20) destName = destName.Substring(0, 18) + "...";
                    carInfo.MarkMisspotted(destName);
                }
                return;
            }

            // Step 4: Passive Stock (Stored yard stock vs Parked commercial empties)
            string passiveAction = role is SpurRole.MowMaterialHandling or SpurRole.MowStaging ? "Stored" : "Parked";
            carInfo.MarkPassive(passiveAction);
        }

        private static bool TryClassifyComponentWork(
            SpottedCarInfo carInfo,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            Industry industry,
            ReserveBuffer? repairBuffer,
            OpsController opsController)
        {
            foreach (IndustryComponent comp in componentsOnSpur)
            {
                switch (comp)
                {
                    case RepairTrack repairTrack when TryClassifyRepairTrack(carInfo, repairTrack, opsController, repairBuffer):
                        return true;

                    case TeamTrack teamTrack when TryClassifyTeamTrack(carInfo, teamTrack, industry):
                        return true;
                }
            }

            return false;
        }

        private static bool TryClassifyRepairTrack(
            SpottedCarInfo carInfo,
            RepairTrack repairTrack,
            OpsController opsController,
            ReserveBuffer? repairBuffer)
        {
            Car? car = carInfo.Car;
            if (car == null) return false;

            if (RepairTrackHelper.TryGetProgramRepairMode(car, repairTrack, opsController, out bool isOverhaul))
            {
                float partsDemand = RepairTrackHelper.CalculateCarRepairPartsDemand(car);
                if (repairBuffer is not null)
                {
                    repairBuffer.ProgramDemand += partsDemand;
                }

                CarStatusState state = isOverhaul ? CarStatusState.ProgramOverhaul : CarStatusState.ProgramRepair;
                string detail = isOverhaul ? "Program Overhaul" : "Program Repair";
                TimeSpan? eta = RepairTrackHelper.CalculateRepairEta(partsDemand, repairTrack);
                carInfo.MarkRepair(partsDemand, state, detail, car.Condition, eta);
                return true;
            }

            if (car.Condition < car.RepairCap - 0.001f || car.NeedsOiling || car.HasHotbox)
            {
                float partsDemand = RepairTrackHelper.CalculateCarRepairPartsDemand(car);
                if (repairBuffer is not null)
                {
                    repairBuffer.SpotDemand += partsDemand;
                }

                TimeSpan? eta = RepairTrackHelper.CalculateRepairEta(partsDemand, repairTrack);
                carInfo.MarkRepair(partsDemand, CarStatusState.SpotRepair, "Spot Repair", car.Condition, eta);
                return true;
            }

            carInfo.MarkServiced(car.Condition, carInfo.OpsCar?.Waybill.HasValue == true);
            return true;
        }

        private static bool TryClassifyTeamTrack(SpottedCarInfo carInfo, TeamTrack teamTrack, Industry industry)
        {
            IOpsCar opsCar = carInfo.OpsCar;
            if (opsCar?.Waybill is not { } waybill || !CarTrackingHelper.IsWaybilledHere(waybill, industry, teamTrack))
            {
                return false;
            }

            TeamTrackProfile.Entry? matchedEntry = null;
            foreach (TeamTrackProfile.Entry profile in teamTrack.profileEntries)
            {
                if (profile.tag == waybill.Tag)
                {
                    matchedEntry = profile;
                    break;
                }
            }

            if (matchedEntry is not { load: { } ttLoad } entry)
            {
                return false;
            }

            (float qty, float cap) = opsCar.QuantityOfLoad(ttLoad);
            carInfo.SetLoad(ttLoad, qty, cap);

            float loadingTimeDays = Mathf.Max(0.1f, entry.loadingTime);
            float ratePerMin = (carInfo.CapacityAmount / loadingTimeDays) / 1440f;
            float toTransfer = entry.export ? Mathf.Max(0f, carInfo.CapacityAmount - qty) : qty;

            if (toTransfer <= 0.001f)
            {
                carInfo.MarkReady();
                return true;
            }

            TimeSpan? eta = ratePerMin > 0.0001f ? TimeSpan.FromMinutes(toTransfer / ratePerMin) : null;
            carInfo.MarkTransfer(ratePerMin, eta, entry.export ? "Loading" : "Unloading");
            return true;
        }
    }
}
