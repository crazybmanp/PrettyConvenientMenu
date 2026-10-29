using System;
using System.Collections.Generic;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Helpers
{
    public static class SpurCarProgressionHelper
    {
        private static readonly List<SpurTrackCar> workingCarList = new List<SpurTrackCar>(16);

        public static void CollectCarsForSpur(
            OpsController? opsController,
            IndustryComponent component,
            Load load,
            IndustryComponent displayComponent,
            Industry industry,
            float bufferAvailable,
            float nominalRatePerMin,
            bool isUnload,
            IndustrySnapshot snapshot,
            SpurRole? roleOverride = null)
        {
            if (opsController == null)
            {
                return;
            }

            string trackName = displayComponent.ShortName(industry) ?? "Spur";
            SpurRole role = roleOverride ?? (isUnload ? SpurRole.CommercialInbound : SpurRole.CommercialOutbound);
            SpurCarGroup spurGroup = BufferHelper.GetOrAddSpurGroup(snapshot.SpurGroups, trackName, role);

            IEnumerable<Car>? carsAtSpur = opsController.CarsAtPosition(component);
            if (carsAtSpur == null)
            {
                return;
            }

            workingCarList.Clear();
            int carTrackIdx = 0;

            foreach (Car car in carsAtSpur)
            {
                if (car == null || Mathf.Abs(car.velocity) > 0.05f)
                {
                    continue;
                }

                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar == null || !component.carTypeFilter.Matches(opsCar.CarType))
                {
                    continue;
                }

                if (isUnload)
                {
                    if (opsCar.Waybill is not { } waybill || !waybill.Destination.Equals(component))
                    {
                        continue;
                    }
                    if (!opsCar.IsEmptyOrContains(load))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!opsCar.IsEmptyOrContains(load))
                    {
                        continue;
                    }
                }

                (float qty, float cap) = opsCar.QuantityOfLoad(load);
                workingCarList.Add(new SpurTrackCar(opsCar, qty, cap, carTrackIdx++));
            }

            if (isUnload)
            {
                workingCarList.Sort(UnloadOrderAscending.Instance);
            }
            else
            {
                workingCarList.Sort(LoadOrderDescending.Instance);
            }

            float remainingBuffer = bufferAvailable;
            BufferInfo? matchingBuffer = null;
            foreach (BufferInfo buf in snapshot.Buffers)
            {
                if (string.Equals(buf.CargoType, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    if (buf is CommercialBuffer comm)
                    {
                        if (comm.IsInput == isUnload)
                        {
                            matchingBuffer = comm;
                            break;
                        }
                    }
                    else
                    {
                        matchingBuffer = buf;
                        break;
                    }
                }
            }

            float continuousRatePerMin = (matchingBuffer is CommercialBuffer commBuf && commBuf.DailyRate > 0.001f)
                ? (commBuf.DailyRate / 1440f)
                : 0f;
            float accumulatedMinutes = 0f;
            bool activeCarAssigned = false;

            foreach (SpurTrackCar entry in workingCarList)
            {
                float remainingTransferAmount = isUnload ? entry.Quantity : Mathf.Max(0f, entry.Capacity - entry.Quantity);

                CarStatusState statusState;
                string statusDetail;
                TimeSpan? eta = null;
                float currentRate = 0f;

                if (remainingTransferAmount <= load.ZeroThreshold)
                {
                    statusState = CarStatusState.Completed;
                    statusDetail = "Completed";
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
                    eta = TimeSpan.FromMinutes(accumulatedMinutes);

                    if (!activeCarAssigned)
                    {
                        activeCarAssigned = true;
                        currentRate = (fromBuffer > 0.0001f) ? nominalRatePerMin : continuousRatePerMin;
                        statusState = CarStatusState.ActiveTransfer;
                        statusDetail = isUnload ? "Unloading" : "Loading";
                    }
                    else
                    {
                        currentRate = 0f;
                        statusState = CarStatusState.Queued;
                        statusDetail = "Queued";
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
                    statusState = CarStatusState.Blocked;
                    statusDetail = isUnload ? "Buffer Full" : "Buffer Empty";
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

                SpottedCarInfo carInfo = new SpottedCarInfo
                {
                    CarId = entry.OpsCar.Id,
                    ReportingMark = entry.OpsCar.DisplayName ?? entry.OpsCar.Id ?? string.Empty,
                    CarType = entry.OpsCar.CarType ?? "Car",
                    TrackName = trackName,
                    CargoName = load.description ?? "Cargo",
                    Units = load.units,
                    CurrentAmount = entry.Quantity,
                    CapacityAmount = entry.Capacity,
                    TransferRatePerMinute = currentRate,
                    EstimatedTimeToCompletion = eta,
                    StatusState = statusState,
                    StatusDetail = statusDetail
                };

                spurGroup.Cars.Add(carInfo);
            }
        }
    }
}
