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
    /// FIFO transfer queue calculator for commercial loaders and unloaders.
    /// Determines active transfer rates, ETAs, and queue positions for spotted cars in-place.
    /// </summary>
    public static class TransferQueueHelper
    {
        private static readonly List<SpottedCarInfo> workingCarList = new List<SpottedCarInfo>(16);

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
            BufferInfo? matchingBuffer)
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

                if (!CarTrackingHelper.IsWaybilledHere(opsCar.Waybill, industry, component))
                {
                    continue;
                }

                if (!opsCar.IsEmptyOrContains(load))
                {
                    continue;
                }

                (float qty, float cap) = opsCar.QuantityOfLoad(load);
                if (cap <= 0.001f)
                {
                    cap = load.NominalQuantityPerCarLoad;
                }
                carInfo.CurrentAmount = qty;
                carInfo.CapacityAmount = cap;

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
            float accumulatedMinutes = 0f;
            bool activeCarAssigned = false;

            foreach (SpottedCarInfo carInfo in workingCarList)
            {
                float remainingTransferAmount = isUnload ? carInfo.CurrentAmount : Mathf.Max(0f, carInfo.CapacityAmount - carInfo.CurrentAmount);

                if (remainingTransferAmount <= load.ZeroThreshold)
                {
                    carInfo.MarkReady(load, carInfo.CurrentAmount, carInfo.CapacityAmount);
                }
                else if (remainingBuffer > load.ZeroThreshold || continuousRatePerMin > 0.0001f)
                {
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
                    accumulatedMinutes += carMinutes;
                    TimeSpan? eta = TimeSpan.FromMinutes(accumulatedMinutes);

                    if (!activeCarAssigned)
                    {
                        activeCarAssigned = true;
                        float currentRate = (fromBuffer > 0.0001f) ? nominalRatePerMin : continuousRatePerMin;
                        string action = isUnload ? "Unloading" : "Loading";
                        carInfo.MarkTransfer(load, carInfo.CurrentAmount, carInfo.CapacityAmount, currentRate, eta, action);
                    }
                    else
                    {
                        carInfo.MarkQueued(load, carInfo.CurrentAmount, carInfo.CapacityAmount, eta);
                    }

                    if (matchingBuffer != null)
                    {
                        if (isUnload)
                        {
                            matchingBuffer.ActiveInbound += remainingTransferAmount;
                        }
                        else
                        {
                            matchingBuffer.ActiveOutbound += remainingTransferAmount;
                        }
                    }
                }
                else
                {
                    string action = isUnload ? "Buffer Full" : "Buffer Empty";
                    carInfo.MarkBlocked(load, carInfo.CurrentAmount, carInfo.CapacityAmount, action);
                    if (matchingBuffer != null)
                    {
                        if (isUnload)
                        {
                            matchingBuffer.ActiveInbound += remainingTransferAmount;
                        }
                        else
                        {
                            matchingBuffer.ActiveOutbound += remainingTransferAmount;
                        }
                    }
                }
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
