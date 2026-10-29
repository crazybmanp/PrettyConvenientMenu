using System;
using System.Collections.Generic;
using Model;
using Model.Definition.Data;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class CommercialFreightCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.CommercialFreight;

        public void Collect(Industry industry, IndustrySnapshot snapshot)
        {
            float contractMultiplier = industry.GetContractMultiplier();
            if (industry.TryGetActiveContract(out Contract contract))
            {
                snapshot.CurrentTier = contract.Tier;
                snapshot.TierDescription = $"Contract Tier {contract.Tier}";
            }
            else
            {
                snapshot.CurrentTier = 0;
                snapshot.TierDescription = industry.usesContract ? "No Active Contract" : "Non-Contracted Industry";
            }

            int totalInputCount = 0;
            int starvedInputCount = 0;
            string starvedInputCargo = string.Empty;

            int totalOutputCount = 0;
            int blockedOutputCount = 0;
            string blockedOutputCargo = string.Empty;

            List<IndustryComponent>? components = industry.Components;
            if (components == null)
            {
                return;
            }

            OpsController? opsController = OpsController.Shared;

            // 1. Process Formulaic Components (Inputs & Outputs)
            foreach (IndustryComponent c in components)
            {
                if (c is FormulaicIndustryComponent { IsProgressionEnabled: true } formulaic)
                {
                    string? formulaPrefix = !formulaic.sharedStorage ? formulaic.subIdentifier : null;

                    // Inputs
                    foreach (FormulaicIndustryComponent.Term term in formulaic.inputTerms)
                    {
                        if (term.load is not { } inLoad)
                        {
                            continue;
                        }

                        float dailyRate = term.unitsPerDay * contractMultiplier;
                        float currentStock = industry.Storage?.QuantityInStorage(inLoad, formulaPrefix) ?? 0f;
                        float maxStorage = BufferHelper.FindMaxStorageForLoad(components, inLoad);

                        CommercialBuffer buf = BufferHelper.GetOrAddCommercialBuffer(snapshot.Buffers, inLoad, true);
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = maxStorage;
                        buf.DailyRate = dailyRate;

                        if (dailyRate > 0.001f)
                        {
                            float days = currentStock / dailyRate;
                            buf.RunwayDuration = TimeSpan.FromDays(days);
                        }
                        else
                        {
                            buf.RunwayDuration = null;
                        }

                        totalInputCount++;
                        if (currentStock <= inLoad.ZeroThreshold && dailyRate > 0.001f)
                        {
                            starvedInputCount++;
                            starvedInputCargo = inLoad.description ?? "Input";
                        }
                    }

                    // Outputs
                    foreach (FormulaicIndustryComponent.Term term in formulaic.outputTerms)
                    {
                        if (term.load is not { } outLoad)
                        {
                            continue;
                        }

                        float dailyRate = term.unitsPerDay * contractMultiplier;
                        float currentStock = industry.Storage?.QuantityInStorage(outLoad, formulaPrefix) ?? 0f;
                        float maxStorage = BufferHelper.FindMaxStorageForLoad(components, outLoad);

                        CommercialBuffer buf = BufferHelper.GetOrAddCommercialBuffer(snapshot.Buffers, outLoad, false);
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = maxStorage;
                        buf.DailyRate = dailyRate;

                        float remainingCapacity = Mathf.Max(0f, maxStorage - currentStock);
                        if (dailyRate > 0.001f)
                        {
                            float days = remainingCapacity / dailyRate;
                            buf.RunwayDuration = TimeSpan.FromDays(days);
                        }
                        else
                        {
                            buf.RunwayDuration = null;
                        }

                        totalOutputCount++;
                        if (remainingCapacity <= outLoad.ZeroThreshold && dailyRate > 0.001f)
                        {
                            blockedOutputCount++;
                            blockedOutputCargo = outLoad.description ?? "Output";
                        }
                    }
                }
            }

            // 2. Process Spurs & Spotted Cars
            foreach (IndustryComponent component in components)
            {
                if (component is IndustryUnloader { load: { } inLoad } unloader)
                {
                    string? formulaPrefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float currentStock = industry.Storage?.QuantityInStorage(inLoad, formulaPrefix) ?? 0f;
                    float bufferAvailable = Mathf.Max(0f, unloader.maxStorage - currentStock);
                    float nominalRatePerDay = unloader.carUnloadRate * contractMultiplier;
                    float nominalRatePerMin = nominalRatePerDay / 1440f;

                    CommercialBuffer buf = BufferHelper.GetOrAddCommercialBuffer(snapshot.Buffers, inLoad, true);
                    if (buf.MaxCapacity <= 0.001f)
                    {
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = unloader.maxStorage;
                        float dailyRate = unloader.storageConsumptionRate * contractMultiplier;
                        buf.DailyRate = dailyRate;
                        if (dailyRate > 0.001f)
                        {
                            float days = currentStock / dailyRate;
                            buf.RunwayDuration = TimeSpan.FromDays(days);
                        }
                        else
                        {
                            buf.RunwayDuration = null;
                        }
                        totalInputCount++;
                        if (currentStock <= inLoad.ZeroThreshold && dailyRate > 0.001f)
                        {
                            starvedInputCount++;
                            starvedInputCargo = inLoad.description ?? "Input";
                        }
                    }
                    SpurCarProgressionHelper.CollectCarsForSpur(opsController, unloader, inLoad, component, industry, bufferAvailable, nominalRatePerMin, true, snapshot);
                }

                if (component is IndustryLoader { load: { } outLoad } loader)
                {
                    string? formulaPrefix = !loader.sharedStorage ? loader.subIdentifier : null;
                    float currentStock = industry.Storage?.QuantityInStorage(outLoad, formulaPrefix) ?? 0f;
                    float nominalRatePerDay = loader.carLoadRate * contractMultiplier;
                    float nominalRatePerMin = nominalRatePerDay / 1440f;

                    CommercialBuffer buf = BufferHelper.GetOrAddCommercialBuffer(snapshot.Buffers, outLoad, false);
                    if (buf.MaxCapacity <= 0.001f)
                    {
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = loader.maxStorage;
                        float dailyRate = loader.productionRate * contractMultiplier;
                        buf.DailyRate = dailyRate;
                        float remainingCapacity = Mathf.Max(0f, loader.maxStorage - currentStock);
                        if (dailyRate > 0.001f)
                        {
                            float days = remainingCapacity / dailyRate;
                            buf.RunwayDuration = TimeSpan.FromDays(days);
                        }
                        else
                        {
                            buf.RunwayDuration = null;
                        }
                        totalOutputCount++;
                        if (remainingCapacity <= outLoad.ZeroThreshold && dailyRate > 0.001f)
                        {
                            blockedOutputCount++;
                            blockedOutputCargo = outLoad.description ?? "Output";
                        }
                    }
                    SpurCarProgressionHelper.CollectCarsForSpur(opsController, loader, outLoad, component, industry, currentStock, nominalRatePerMin, false, snapshot);
                }

                if (component is TeamTrack teamTrack && opsController != null)
                {
                    CarTrackingHelper.CollectTeamTrackCars(opsController, teamTrack, component, industry, snapshot);
                }

                if (component is ProgressionIndustryComponent prog && opsController != null)
                {
                    CollectProgressionIndustryCars(opsController, prog, component, industry, snapshot);
                }
            }

            // 3. Status Messaging
            if (industry.usesContract && snapshot.CurrentTier == 0)
            {
                snapshot.State = ProductionState.Idle;
                snapshot.StatusMessage = "No active contract tier set.";
                snapshot.DiagnosticAlert = "Establish a contract in the company window to begin operations.";
            }
            else if (starvedInputCount > 0 && starvedInputCount == totalInputCount)
            {
                snapshot.State = ProductionState.Starved;
                snapshot.StatusMessage = "Raw input material depleted.";
                snapshot.DiagnosticAlert = $"Input buffer depleted: {starvedInputCargo}. Spot loaded cars on inbound spurs to resume production.";
            }
            else if (blockedOutputCount > 0 && blockedOutputCount == totalOutputCount)
            {
                snapshot.State = ProductionState.Blocked;
                snapshot.StatusMessage = "All output buffers full.";
                snapshot.DiagnosticAlert = "Output buffer is full. Spot empty cars on outbound spurs to resume production.";
            }
            else
            {
                snapshot.State = ProductionState.Running;
                if (blockedOutputCount > 0)
                {
                    snapshot.StatusMessage = $"Producing normally ({blockedOutputCargo} buffer full).";
                    snapshot.DiagnosticAlert = $"Notice: {blockedOutputCargo} output buffer is full. Spot empty cars on outbound spurs.";
                }
                else if (starvedInputCount > 0)
                {
                    snapshot.StatusMessage = $"Producing normally ({starvedInputCargo} input depleted).";
                    snapshot.DiagnosticAlert = $"Notice: {starvedInputCargo} depleted. Spot loaded cars on inbound spurs.";
                }
                else
                {
                    snapshot.StatusMessage = $"Producing normally at {contractMultiplier * 100f:F0}% throughput.";
                    snapshot.DiagnosticAlert = string.Empty;
                }
            }
        }

        private static void CollectProgressionIndustryCars(
            OpsController opsController,
            ProgressionIndustryComponent prog,
            IndustryComponent displayComponent,
            Industry industry,
            IndustrySnapshot snapshot)
        {
            string trackName = displayComponent.ShortName(industry) ?? "Material Delivery Track";
            SpurCarGroup spurGroup = BufferHelper.GetOrAddSpurGroup(snapshot.SpurGroups, trackName, SpurRole.CommercialInbound);

            IEnumerable<Car>? cars = opsController.CarsAtPosition(prog);
            if (cars == null)
            {
                return;
            }

            foreach (Car car in cars)
            {
                if (car == null || Mathf.Abs(car.velocity) > 0.05f)
                {
                    continue;
                }

                IOpsCar? opsCar = opsController.CachedOpsCar(car);
                if (opsCar == null)
                {
                    continue;
                }

                SpottedCarInfo carInfo = new SpottedCarInfo
                {
                    CarId = car.id,
                    ReportingMark = opsCar.DisplayName ?? opsCar.Id ?? string.Empty,
                    CarType = opsCar.CarType ?? "Car",
                    TrackName = trackName,
                    CargoName = "Staged Car",
                    Units = LoadUnits.Quantity,
                    CurrentAmount = 0f,
                    CapacityAmount = 0f,
                    TransferRatePerMinute = 0f,
                    EstimatedTimeToCompletion = null,
                    StatusState = CarStatusState.Idle,
                    StatusDetail = "Staged"
                };

                spurGroup.Cars.Add(carInfo);
            }
        }
    }
}
