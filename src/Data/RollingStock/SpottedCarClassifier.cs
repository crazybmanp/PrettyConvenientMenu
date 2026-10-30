using System;
using System.Collections.Generic;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
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
            if (carsOnTrack == null) return;

            // Step 1: Instantiate our tracked car objects upfront in spurGroup.Cars
            spurGroup.Cars.Clear();
            int trackIndex = 0;
            foreach (Car car in carsOnTrack)
            {
                if (car == null || Mathf.Abs(car.velocity) > 0.05f) continue;
                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar == null) continue;
                spurGroup.Cars.Add(new SpottedCarInfo(car, opsCar, trackIndex++));
            }

            if (spurGroup.Cars.Count == 0) return;

            ReserveBuffer? repairBuffer = FindRepairBuffer(componentsOnSpur, snapshot, caps);
            float contractMultiplier = industry.GetContractMultiplier();

            // Step 2: Active component operations (Unloading, Loading, Team Track, Program Repair, Material Transfer)
            ProcessActiveComponentWork(
                spurGroup.Cars, componentsOnSpur, industry, snapshot,
                repairBuffer, contractMultiplier, opsController);

            // Step 3: Passive & Non-active rolling stock (Ready, Spot Repair, Staged, Stored, Parked, Misspotted)
            ProcessNonActiveSpottedStock(
                spurGroup.Cars, componentsOnSpur, industry,
                repairBuffer, caps, role);
        }

        private static ReserveBuffer? FindRepairBuffer(IReadOnlyList<IndustryComponent> componentsOnSpur, IndustrySnapshot snapshot, SpurCapabilities caps)
        {
            if (!caps.CanRepair) return null;

            foreach (IndustryComponent comp in componentsOnSpur)
            {
                if (comp is RepairTrack rt)
                {
                    ReserveBuffer buffer = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, rt.repairPartsLoadId, "Parts", LoadUnits.Pounds, BufferRole.MaintenanceReserve);
                    buffer.IsRepairParts = true;
                    return buffer;
                }
            }
            return null;
        }

        private static BufferInfo? FindMatchingBuffer(List<BufferInfo> buffers, Load load, bool isUnload)
        {
            foreach (BufferInfo buf in buffers)
            {
                if (string.Equals(buf.CargoType, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    if (buf is CommercialBuffer comm)
                    {
                        if (comm.IsInput == isUnload) return comm;
                    }
                    else
                    {
                        return buf;
                    }
                }
            }
            return null;
        }

        private static void ProcessActiveComponentWork(
            List<SpottedCarInfo> trackCars,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            Industry industry,
            IndustrySnapshot snapshot,
            ReserveBuffer? repairBuffer,
            float contractMultiplier,
            OpsController opsController)
        {
            foreach (IndustryComponent comp in componentsOnSpur)
            {
                // 1A. Commercial Unloader
                if (comp is IndustryUnloader { load: { } inLoad } unloader)
                {
                    string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float currentStock = industry.Storage?.QuantityInStorage(inLoad, prefix) ?? 0f;
                    float bufferAvailable = Mathf.Max(0f, unloader.maxStorage - currentStock);
                    float nominalRatePerDay = unloader.carUnloadRate * contractMultiplier;
                    float nominalRatePerMin = nominalRatePerDay / 1440f;
                    BufferInfo? matchingBuffer = FindMatchingBuffer(snapshot.Buffers, inLoad, isUnload: true);

                    TransferQueueHelper.SequenceQueue(
                        trackCars, unloader, inLoad, industry, bufferAvailable,
                        nominalRatePerMin, isUnload: true, matchingBuffer);
                }

                // 1B. Commercial Loader
                if (comp is IndustryLoader { load: { } outLoad } loader)
                {
                    string? prefix = !loader.sharedStorage ? loader.subIdentifier : null;
                    float currentStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                    float nominalRatePerDay = loader.carLoadRate * contractMultiplier;
                    float nominalRatePerMin = nominalRatePerDay / 1440f;
                    BufferInfo? matchingBuffer = FindMatchingBuffer(snapshot.Buffers, outLoad, isUnload: false);

                    TransferQueueHelper.SequenceQueue(
                        trackCars, loader, outLoad, industry, currentStock,
                        nominalRatePerMin, isUnload: false, matchingBuffer);
                }

                // 1C. Team Track
                if (comp is TeamTrack teamTrack)
                {
                    foreach (SpottedCarInfo carInfo in trackCars)
                    {
                        if (carInfo.IsClassified) continue;

                        IOpsCar opsCar = carInfo.OpsCar;
                        if (opsCar?.Waybill is not { } waybill || !CarTrackingHelper.IsWaybilledHere(waybill, industry, comp))
                        {
                            continue;
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

                        if (!matchedEntry.HasValue || matchedEntry.Value.load is not { } ttLoad)
                        {
                            continue;
                        }

                        (float qty, float cap) = opsCar.QuantityOfLoad(ttLoad);
                        float loadingTimeDays = Mathf.Max(0.1f, matchedEntry.Value.loadingTime);
                        float ratePerDay = cap / loadingTimeDays;
                        float ratePerMin = ratePerDay / 1440f;
                        bool isExport = matchedEntry.Value.export;

                        float toTransfer = isExport ? Mathf.Max(0f, cap - qty) : qty;
                        TimeSpan? eta = (ratePerMin > 0.0001f && toTransfer > 0.001f)
                            ? TimeSpan.FromMinutes(toTransfer / ratePerMin)
                            : null;

                        if (toTransfer <= 0.001f)
                        {
                            carInfo.MarkReady(ttLoad, qty, cap);
                        }
                        else
                        {
                            carInfo.MarkTransfer(ttLoad, qty, cap, ratePerMin, eta, isExport ? "Loading" : "Unloading");
                        }
                    }
                }

                // 1D. Repair Track (Program Overhaul / Program Repair)
                if (comp is RepairTrack repairTrack)
                {
                    foreach (SpottedCarInfo carInfo in trackCars)
                    {
                        if (carInfo.IsClassified) continue;

                        Car? car = carInfo.Car;
                        if (car == null) continue;

                        bool hasOverride = car.TryGetOverrideDestination(OverrideDestination.Repair, opsController, out (OpsCarPosition position, string tag)? overrideDest)
                                           && overrideDest.HasValue;

                        if (!hasOverride || !string.Equals(overrideDest!.Value.position.Identifier, repairTrack.Identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        bool isOverhaul = string.Equals(overrideDest.Value.tag, "overhaul", StringComparison.OrdinalIgnoreCase)
                                          || (car.CarType.IsTender && car.TryGetAdjacentCar(car.EndToLogical(Car.End.F), out Car adjacent) && RepairTrackHelper.IsCarInForOverhaul(adjacent, repairTrack, opsController));

                        float partsDemand = RepairTrackHelper.CalculateCarRepairPartsDemand(car);
                        if (repairBuffer != null)
                        {
                            repairBuffer.ProgramDemand += partsDemand;
                        }

                        CarStatusState state = isOverhaul ? CarStatusState.ProgramOverhaul : CarStatusState.ProgramRepair;
                        string detail = isOverhaul ? "Overhaul" : "Repair";
                        carInfo.MarkRepair(partsDemand, state, detail, car.Condition);
                    }
                }

                // 1E. Material Transfer (StorageUnloader / StorageLoader)
                if (comp is StorageUnloader storageUnloader)
                {
                    foreach (SpottedCarInfo carInfo in trackCars)
                    {
                        if (carInfo.IsClassified) continue;

                        IOpsCar opsCar = carInfo.OpsCar;
                        if (opsCar?.Waybill is not { } waybill || !CarTrackingHelper.IsWaybilledHere(waybill, industry, storageUnloader))
                        {
                            continue;
                        }

                        Car? car = carInfo.Car;
                        (Load? actualLoad, float actualQty, float actualCap) = CarTrackingHelper.GetCarCargo(car, opsCar);
                        if (actualLoad == null || actualQty <= actualLoad.ZeroThreshold) continue;

                        string? prefix = !storageUnloader.sharedStorage ? storageUnloader.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(actualLoad, prefix) ?? 0f;

                        float unloaderStorageCap = 100f;
                        int unloadRateSetting = 1;
                        foreach (StorageUnloader.LoadEntry l in storageUnloader.loads)
                        {
                            if (l.loadId == actualLoad.id)
                            {
                                unloaderStorageCap = l.storage * actualLoad.NominalQuantityPerCarLoad;
                                unloadRateSetting = l.unloadRate;
                                break;
                            }
                        }

                        float spaceRemaining = Mathf.Max(0f, unloaderStorageCap - currentStock);
                        if (spaceRemaining > actualLoad.ZeroThreshold)
                        {
                            float ratePerDay = unloadRateSetting * actualLoad.NominalQuantityPerCarLoad;
                            float ratePerMin = ratePerDay / 1440f;
                            TimeSpan? eta = ratePerMin > 0.0001f ? TimeSpan.FromMinutes(actualQty / ratePerMin) : null;

                            foreach (BufferInfo b in snapshot.Buffers)
                            {
                                if (b.CargoType == actualLoad.id)
                                {
                                    b.ActiveInbound += actualQty;
                                    break;
                                }
                            }

                            carInfo.MarkTransfer(actualLoad, actualQty, actualCap, ratePerMin, eta, "Unloading");
                        }
                    }
                }

                if (comp is StorageLoader storageLoader)
                {
                    foreach (SpottedCarInfo carInfo in trackCars)
                    {
                        if (carInfo.IsClassified) continue;

                        IOpsCar opsCar = carInfo.OpsCar;
                        if (opsCar?.Waybill is not { } waybill || !CarTrackingHelper.IsWaybilledHere(waybill, industry, storageLoader))
                        {
                            continue;
                        }

                        Load? targetLoad = !string.IsNullOrEmpty(waybill.Tag) ? CarLoadLibrary.GetLoad(waybill.Tag) : null;
                        if (targetLoad == null) continue;

                        (float q, float cap) = opsCar.QuantityOfLoad(targetLoad);
                        float toFill = Mathf.Max(0f, cap - q);

                        string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(targetLoad, prefix) ?? 0f;

                        if (toFill <= targetLoad.ZeroThreshold)
                        {
                            carInfo.MarkReady(targetLoad, q, cap);
                        }
                        else if (currentStock > targetLoad.ZeroThreshold)
                        {
                            int loadRate = 1;
                            foreach (StorageLoader.LoadEntry l in storageLoader.loads)
                            {
                                if (l.loadId == targetLoad.id)
                                {
                                    loadRate = l.carLoadRate;
                                    break;
                                }
                            }

                            float ratePerDay = loadRate * targetLoad.NominalQuantityPerCarLoad;
                            float ratePerMin = ratePerDay / 1440f;
                            TimeSpan? eta = ratePerMin > 0.0001f ? TimeSpan.FromMinutes(toFill / ratePerMin) : null;

                            foreach (BufferInfo b in snapshot.Buffers)
                            {
                                if (b.CargoType == targetLoad.id)
                                {
                                    b.ActiveOutbound += toFill;
                                    break;
                                }
                            }

                            carInfo.MarkTransfer(targetLoad, q, cap, ratePerMin, eta, "Loading");
                        }
                    }
                }
            }
        }

        private static void ProcessNonActiveSpottedStock(
            List<SpottedCarInfo> trackCars,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            Industry industry,
            ReserveBuffer? repairBuffer,
            SpurCapabilities caps,
            SpurRole role)
        {
            foreach (SpottedCarInfo carInfo in trackCars)
            {
                if (carInfo.IsClassified) continue;

                Car? car = carInfo.Car;
                IOpsCar opsCar = carInfo.OpsCar;
                if (opsCar == null) continue;

                (Load? cargo, float cQty, float cCap) = CarTrackingHelper.GetCarCargo(car, opsCar);

                // Step 2: Finished Work (Ready for pickup)
                bool isCompletedHere = false;
                if (CarTrackingHelper.IsWaybilledFromHere(opsCar.Waybill, industry))
                {
                    isCompletedHere = true;
                }
                else
                {
                    foreach (IndustryComponent comp in componentsOnSpur)
                    {
                        if (CarTrackingHelper.IsCarCompletedAt(opsCar, industry, comp, cargo, comp is IndustryUnloader))
                        {
                            isCompletedHere = true;
                            break;
                        }
                    }
                }

                if (isCompletedHere)
                {
                    carInfo.MarkReady(cargo, cQty, cCap);
                    continue;
                }

                // Step 3: Physical Spot Repair (Unscheduled car repair on designated repair tracks)
                if (caps.CanRepair && car != null)
                {
                    bool isDamaged = car.Condition < car.RepairCap;
                    bool needsOil = car.NeedsOiling;
                    bool hasHotbox = car.HasHotbox;

                    if (isDamaged || needsOil || hasHotbox)
                    {
                        float partsDemand = RepairTrackHelper.CalculateCarRepairPartsDemand(car);
                        if (repairBuffer != null)
                        {
                            repairBuffer.SpotDemand += partsDemand;
                        }

                        carInfo.MarkRepair(partsDemand, CarStatusState.SpotRepair, "Spot Repair", car.Condition);
                        continue;
                    }
                }

                // Step 4: Foreign Waybill (Staged on maintenance tracks vs Misspotted on commercial tracks)
                if (opsCar.Waybill.HasValue)
                {
                    if (role is SpurRole.MowMaterialHandling or SpurRole.MowStaging)
                    {
                        carInfo.MarkPassive("Staged", cargo, cQty, cCap);
                    }
                    else
                    {
                        string destName = !string.IsNullOrEmpty(opsCar.Waybill.Value.Destination.DisplayName)
                            ? opsCar.Waybill.Value.Destination.DisplayName
                            : opsCar.Waybill.Value.Destination.Identifier;
                        if (destName.Length > 20) destName = destName.Substring(0, 18) + "...";
                        carInfo.MarkMisspotted(destName, cargo);
                    }
                    continue;
                }

                // Step 5: Passive Stock (Stored yard stock vs Parked commercial empties)
                if (role is SpurRole.MowMaterialHandling or SpurRole.MowStaging)
                {
                    carInfo.MarkPassive("Stored", cargo, cQty, cCap);
                }
                else
                {
                    carInfo.MarkPassive("Parked", cargo, cQty, cCap);
                }
            }
        }
    }
}
