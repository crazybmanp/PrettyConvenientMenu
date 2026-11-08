using System;
using System.Collections.Generic;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Integration;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Helpers
{
    /// <summary>
    /// Transfer queue calculator for commercial and maintenance-of-way loaders and unloaders.
    /// Determines active transfer rates, ETAs, and queue positions for spotted cars in-place.
    /// </summary>
    public static class TransferQueueHelper
    {
        private static readonly List<SpottedCarInfo> workingCarList = new List<SpottedCarInfo>(16);

        /// <summary>
        /// Sequences active progression and storage queues for all unloaders and loaders on the spur.
        /// </summary>
        public static void SequenceSpurQueues(
            IReadOnlyList<SpottedCarInfo> trackCars,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            Industry industry,
            BufferCollection buffers,
            float contractMultiplier)
        {
            foreach (IndustryComponent comp in componentsOnSpur)
            {
                switch (comp)
                {
                    case IndustryUnloader { load: { } inLoad } unloader:
                    {
                        string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(inLoad, prefix) ?? 0f;
                        float bufferAvailable = Mathf.Max(0f, unloader.maxStorage - currentStock);
                        float nominalRatePerMin = (unloader.carUnloadRate * contractMultiplier) / 1440f;
                        BufferInfo? matchingBuffer = buffers.FindMatching(unloader);

                        SequenceQueue(trackCars, unloader, inLoad, industry, bufferAvailable, nominalRatePerMin, isUnload: true, matchingBuffer);
                        break;
                    }

                    case IndustryLoader { load: { } outLoad } loader:
                    {
                        string? prefix = !loader.sharedStorage ? loader.subIdentifier : null;
                        float rawStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                        int drawerCount = CountDrawers(industry, loader, outLoad);
                        float currentStock = drawerCount > 1 ? (rawStock / drawerCount) : rawStock;
                        float nominalRatePerMin = (loader.carLoadRate * contractMultiplier) / 1440f;
                        float continuousRatePerMin = ResolveContinuousRatePerMin(industry, loader, outLoad, buffers);
                        BufferInfo? matchingBuffer = buffers.FindMatching(loader);

                        SequenceQueue(trackCars, loader, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer, continuousRatePerMin: continuousRatePerMin);
                        break;
                    }

                    case IndustrySeller seller:
                    {
                        if (string.IsNullOrEmpty(seller.loadId) || CarLoadLibrary.GetLoad(seller.loadId) is not { } outLoad)
                        {
                            break;
                        }

                        string? prefix = !seller.sharedStorage ? seller.subIdentifier : null;
                        float rawStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                        int drawerCount = seller.sharedStorage ? CountDrawers(industry, seller, outLoad) : 1;
                        float currentStock = drawerCount > 1 ? (rawStock / drawerCount) : rawStock;
                        float nominalRatePerMin = seller.carLoadRate / 1440f;
                        float continuousRatePerMin = ResolveContinuousRatePerMin(industry, seller, outLoad, buffers);
                        BufferInfo? matchingBuffer = buffers.FindMatching(seller, outLoad);

                        SequenceQueue(trackCars, seller, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer, continuousRatePerMin: continuousRatePerMin);
                        break;
                    }

                    case TeleportLoadingIndustry { load: { } outLoad } teleLoader:
                    {
                        string? prefix = !teleLoader.sharedStorage ? teleLoader.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                        float periodMinutes = Mathf.Max(0.1f, teleLoader.carLoadPeriod / 60f);
                        float nominalRatePerMin = (outLoad.NominalQuantityPerCarLoad > 0 ? outLoad.NominalQuantityPerCarLoad : 100000f) / periodMinutes;
                        BufferInfo? matchingBuffer = buffers.FindMatching(teleLoader);

                        SequenceQueue(trackCars, teleLoader, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer);
                        break;
                    }

                    case StorageUnloader storageUnloader:
                    {
                        string? prefix = !storageUnloader.sharedStorage ? storageUnloader.subIdentifier : null;
                        bool activeCarAssigned = false;
                        float accumulatedMinutes = 0f;
                        foreach (StorageUnloader.LoadEntry entry in storageUnloader.loads)
                        {
                            if (entry.load is not { } inLoad) continue;
                            float maxStorage = entry.storage * inLoad.NominalQuantityPerCarLoad;
                            float currentStock = industry.Storage?.QuantityInStorage(inLoad, prefix) ?? 0f;
                            float bufferAvailable = Mathf.Max(0f, maxStorage - currentStock);
                            float nominalRatePerMin = (entry.unloadRate * inLoad.NominalQuantityPerCarLoad) / 1440f;
                            BufferInfo? matchingBuffer = buffers.FindMatching(storageUnloader, inLoad);

                            SequenceQueue(trackCars, storageUnloader, inLoad, industry, bufferAvailable, nominalRatePerMin, isUnload: true, matchingBuffer, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true, allowParallelTransfer: false);
                        }
                        break;
                    }

                    case StorageLoader storageLoader:
                    {
                        string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                        bool activeBatchAssigned = false;
                        float accumulatedMinutes = 0f;
                        foreach (StorageLoader.LoadEntry entry in storageLoader.loads)
                        {
                            if (entry.load is not { } outLoad) continue;
                            float currentStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                            float nominalRatePerMin = (entry.carLoadRate * outLoad.NominalQuantityPerCarLoad) / 1440f;
                            BufferInfo? matchingBuffer = buffers.FindMatching(storageLoader, outLoad);

                            SequenceQueue(trackCars, storageLoader, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer, ref activeBatchAssigned, ref accumulatedMinutes, multiLoadComponent: true, allowParallelTransfer: true);
                        }
                        break;
                    }
                }
            }
        }

        private static int CountDrawers(Industry industry, IndustryComponent component, Load load)
        {
            string storageKey = StorageKeyFor(component, load);
            int count = 0;
            List<IndustryComponent>? allComps = industry.Components;
            if (allComps != null)
            {
                for (int i = 0; i < allComps.Count; i++)
                {
                    IndustryComponent c = allComps[i];
                    if (c.FillsCarsFrom(load) && StorageKeyFor(c, load) == storageKey)
                    {
                        count++;
                    }
                }
            }
            return count > 0 ? count : 1;
        }

        private static string StorageKeyFor(IndustryComponent component, Load load)
        {
            if (component.sharedStorage)
            {
                return load.id;
            }
            return string.IsNullOrEmpty(component.subIdentifier) ? load.id : $"{component.subIdentifier}.{load.id}";
        }

        private static float ResolveContinuousRatePerMin(
            Industry industry,
            IndustryComponent component,
            Load load,
            BufferCollection buffers)
        {
            // 1. Locate commercial daily rate for this cargo
            float commercialDailyRate = 0f;
            for (int i = 0; i < buffers.Count; i++)
            {
                if (buffers[i] is CommercialBuffer cb && !cb.IsInput && string.Equals(cb.CargoType, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    commercialDailyRate = cb.DailyRate;
                    break;
                }
            }

            if (commercialDailyRate <= 0.001f)
            {
                return 0f;
            }

            float fullRatePerMin = commercialDailyRate / 1440f;

            // 2. Check if MoW Producer Priority is active for this plant & load
            if (MowProducerPriorityAdapter.TryGetReserveInfo(industry.identifier, load.id, null, out MowReserveInfo reserveInfo) && reserveInfo.IsActive)
            {
                if (component is IndustrySeller)
                {
                    // While MoW reserve is refilling, it captures 100% of incoming production
                    return reserveInfo.IsRefilling ? fullRatePerMin : 0f;
                }

                if (component is IndustryLoaderBase)
                {
                    // Commercial shipping only receives production once MoW reserve is satisfied
                    return reserveInfo.IsRefilling ? 0f : fullRatePerMin;
                }

                return fullRatePerMin;
            }

            // 3. Base Game (Vanilla): Split production across all drawers sharing the storage key (Issue #6)
            int drawerCount = CountDrawers(industry, component, load);
            return drawerCount > 1 ? (fullRatePerMin / drawerCount) : fullRatePerMin;
        }

        /// <summary>
        /// Sequences unclassified spotted cars at an unloader or loader into active, queued, or blocked states in-place.
        /// </summary>
        public static void SequenceQueue(
            IReadOnlyList<SpottedCarInfo> trackCars,
            IndustryComponent component,
            Load load,
            Industry industry,
            float bufferAvailable,
            float nominalRatePerMin,
            bool isUnload,
            BufferInfo? matchingBuffer,
            bool multiLoadComponent = false,
            float continuousRatePerMin = 0f)
        {
            bool activeAssigned = false;
            float accumulatedMinutes = 0f;
            SequenceQueue(
                trackCars,
                component,
                load,
                industry,
                bufferAvailable,
                nominalRatePerMin,
                isUnload,
                matchingBuffer,
                ref activeAssigned,
                ref accumulatedMinutes,
                multiLoadComponent,
                allowParallelTransfer: false,
                continuousRatePerMin: continuousRatePerMin);
        }

        /// <summary>
        /// Sequences unclassified spotted cars at an unloader or loader into active, queued, or blocked states in-place,
        /// maintaining transfer state across multi-load component queues.
        /// Supports sequential FIFO (default) and parallel batch transfer modes.
        /// </summary>
        public static void SequenceQueue(
            IReadOnlyList<SpottedCarInfo> trackCars,
            IndustryComponent component,
            Load load,
            Industry industry,
            float bufferAvailable,
            float nominalRatePerMin,
            bool isUnload,
            BufferInfo? matchingBuffer,
            ref bool activeAssigned,
            ref float accumulatedMinutes,
            bool multiLoadComponent = false,
            bool allowParallelTransfer = false,
            float continuousRatePerMin = 0f)
        {
            workingCarList.Clear();

            foreach (SpottedCarInfo carInfo in trackCars)
            {
                if (carInfo.IsClassified) continue;

                IOpsCar opsCar = carInfo.OpsCar;
                if (opsCar == null || !component.carTypeFilter.Matches(opsCar.CarType))
                {
                    continue;
                }

                if (opsCar.Waybill is not { } waybill || !CarTrackingHelper.IsWaybilledHere(waybill, industry, component))
                {
                    continue;
                }

                if (!opsCar.IsEmptyOrContains(load))
                {
                    continue;
                }

                (float qty, float cap) = opsCar.QuantityOfLoad(load);
                if (multiLoadComponent)
                {
                    if (isUnload && qty <= load.ZeroThreshold) continue;
                    if (!isUnload && !string.Equals(waybill.Tag, load.id, StringComparison.OrdinalIgnoreCase)) continue;
                }

                carInfo.SetLoad(load, qty, cap);
                workingCarList.Add(carInfo);
            }

            if (workingCarList.Count == 0) return;

            if (isUnload)
            {
                workingCarList.Sort(UnloadOrderAscending.Instance);
            }
            else
            {
                workingCarList.Sort(LoadOrderDescending.Instance);
            }

            float remainingBuffer = bufferAvailable;
            float effectiveContinuousRate = continuousRatePerMin > 0.0001f
                ? continuousRatePerMin
                : ((matchingBuffer is CommercialBuffer commBuf && commBuf.DailyRate > 0.001f) ? (commBuf.DailyRate / 1440f) : 0f);

            float batchDuration = 0f;

            foreach (SpottedCarInfo carInfo in workingCarList)
            {
                float remainingTransferAmount = isUnload ? carInfo.CurrentAmount : Mathf.Max(0f, carInfo.CapacityAmount - carInfo.CurrentAmount);

                if (remainingTransferAmount <= load.ZeroThreshold)
                {
                    carInfo.MarkReady();
                    continue;
                }

                if (remainingBuffer <= load.ZeroThreshold && effectiveContinuousRate <= 0.0001f)
                {
                    string action = isUnload ? "Buffer Full" : "Buffer Empty";
                    carInfo.MarkBlocked(action);
                    UpdateActiveBuffer(matchingBuffer, remainingTransferAmount, isUnload);
                    continue;
                }

                float fromBuffer = (remainingBuffer > load.ZeroThreshold)
                    ? Mathf.Min(remainingBuffer, remainingTransferAmount)
                    : 0f;
                remainingBuffer = Mathf.Max(0f, remainingBuffer - fromBuffer);

                float carMinutes = 0f;
                if (fromBuffer > 0.0001f && nominalRatePerMin > 0.0001f)
                {
                    carMinutes += fromBuffer / nominalRatePerMin;
                }
                float unbuffered = remainingTransferAmount - fromBuffer;
                if (unbuffered > 0.0001f && effectiveContinuousRate > 0.0001f)
                {
                    carMinutes += unbuffered / effectiveContinuousRate;
                }

                if (carMinutes > batchDuration)
                {
                    batchDuration = carMinutes;
                }

                if (!activeAssigned)
                {
                    float currentRate = (fromBuffer > 0.0001f) ? nominalRatePerMin : effectiveContinuousRate;
                    string action = isUnload ? "Unloading" : "Loading";
                    carInfo.MarkTransfer(currentRate, TimeSpan.FromMinutes(carMinutes), action);

                    if (!allowParallelTransfer)
                    {
                        activeAssigned = true;
                        accumulatedMinutes += carMinutes;
                    }
                }
                else
                {
                    carInfo.MarkQueued(TimeSpan.FromMinutes(accumulatedMinutes + carMinutes));

                    if (!allowParallelTransfer)
                    {
                        accumulatedMinutes += carMinutes;
                    }
                }

                UpdateActiveBuffer(matchingBuffer, remainingTransferAmount, isUnload);
            }

            if (allowParallelTransfer && batchDuration > 0f)
            {
                activeAssigned = true;
                accumulatedMinutes += batchDuration;
            }
        }

        private static void UpdateActiveBuffer(BufferInfo? matchingBuffer, float amount, bool isUnload)
        {
            if (matchingBuffer == null) return;
            if (isUnload)
            {
                matchingBuffer.ActiveInbound += amount;
            }
            else
            {
                matchingBuffer.ActiveOutbound += amount;
            }
        }
    }

    internal sealed class UnloadOrderAscending : IComparer<SpottedCarInfo>
    {
        public static readonly UnloadOrderAscending Instance = new UnloadOrderAscending();

        public int Compare(SpottedCarInfo x, SpottedCarInfo y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int cmp = x.CurrentAmount.CompareTo(y.CurrentAmount);
            if (cmp != 0) return cmp;

            return x.TrackIndex.CompareTo(y.TrackIndex);
        }
    }

    internal sealed class LoadOrderDescending : IComparer<SpottedCarInfo>
    {
        public static readonly LoadOrderDescending Instance = new LoadOrderDescending();

        public int Compare(SpottedCarInfo x, SpottedCarInfo y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int cmp = y.CurrentAmount.CompareTo(x.CurrentAmount);
            if (cmp != 0) return cmp;

            return x.TrackIndex.CompareTo(y.TrackIndex);
        }
    }
}
