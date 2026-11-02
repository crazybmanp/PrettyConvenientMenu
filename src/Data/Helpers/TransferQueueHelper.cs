using System;
using System.Collections.Generic;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.RollingStock;
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
            List<BufferInfo> buffers,
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
                        BufferInfo? matchingBuffer = BufferHelper.FindMatchingBuffer(buffers, inLoad, isUnload: true);

                        SequenceQueue(trackCars, unloader, inLoad, industry, bufferAvailable, nominalRatePerMin, isUnload: true, matchingBuffer);
                        break;
                    }

                    case IndustryLoader { load: { } outLoad } loader:
                    {
                        string? prefix = !loader.sharedStorage ? loader.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                        float nominalRatePerMin = (loader.carLoadRate * contractMultiplier) / 1440f;
                        BufferInfo? matchingBuffer = BufferHelper.FindMatchingBuffer(buffers, outLoad, isUnload: false);

                        SequenceQueue(trackCars, loader, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer);
                        break;
                    }

                    case IndustrySeller seller:
                    {
                        if (string.IsNullOrEmpty(seller.loadId) || CarLoadLibrary.GetLoad(seller.loadId) is not { } outLoad)
                        {
                            break;
                        }

                        string? prefix = !seller.sharedStorage ? seller.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                        float nominalRatePerMin = seller.carLoadRate / 1440f;
                        BufferInfo? matchingBuffer = BufferHelper.FindMatchingBuffer(buffers, outLoad, isUnload: false);

                        SequenceQueue(trackCars, seller, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer);
                        break;
                    }

                    case TeleportLoadingIndustry { load: { } outLoad } teleLoader:
                    {
                        string? prefix = !teleLoader.sharedStorage ? teleLoader.subIdentifier : null;
                        float currentStock = industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f;
                        float periodMinutes = Mathf.Max(0.1f, teleLoader.carLoadPeriod / 60f);
                        float nominalRatePerMin = (outLoad.NominalQuantityPerCarLoad > 0 ? outLoad.NominalQuantityPerCarLoad : 100000f) / periodMinutes;
                        BufferInfo? matchingBuffer = BufferHelper.FindMatchingBuffer(buffers, outLoad, isUnload: false);

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
                            BufferInfo? matchingBuffer = BufferHelper.FindMatchingBuffer(buffers, inLoad, isUnload: true);

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
                            BufferInfo? matchingBuffer = BufferHelper.FindMatchingBuffer(buffers, outLoad, isUnload: false);

                            SequenceQueue(trackCars, storageLoader, outLoad, industry, currentStock, nominalRatePerMin, isUnload: false, matchingBuffer, ref activeBatchAssigned, ref accumulatedMinutes, multiLoadComponent: true, allowParallelTransfer: true);
                        }
                        break;
                    }
                }
            }
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
            bool multiLoadComponent = false)
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
                allowParallelTransfer: false);
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
            bool allowParallelTransfer = false)
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
            float continuousRatePerMin = (matchingBuffer is CommercialBuffer commBuf && commBuf.DailyRate > 0.001f)
                ? (commBuf.DailyRate / 1440f)
                : 0f;

            float batchDuration = 0f;

            foreach (SpottedCarInfo carInfo in workingCarList)
            {
                float remainingTransferAmount = isUnload ? carInfo.CurrentAmount : Mathf.Max(0f, carInfo.CapacityAmount - carInfo.CurrentAmount);

                if (remainingTransferAmount <= load.ZeroThreshold)
                {
                    carInfo.MarkReady();
                    continue;
                }

                if (remainingBuffer <= load.ZeroThreshold && continuousRatePerMin <= 0.0001f)
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
                if (unbuffered > 0.0001f && continuousRatePerMin > 0.0001f)
                {
                    carMinutes += unbuffered / continuousRatePerMin;
                }

                if (carMinutes > batchDuration)
                {
                    batchDuration = carMinutes;
                }

                if (!activeAssigned)
                {
                    float currentRate = (fromBuffer > 0.0001f) ? nominalRatePerMin : continuousRatePerMin;
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
