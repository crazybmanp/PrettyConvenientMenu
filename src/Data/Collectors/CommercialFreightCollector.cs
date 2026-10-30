using System;
using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class CommercialFreightCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.CommercialFreight;

        public void PopulateFacilityMetadata(Industry industry, IndustrySnapshot snapshot)
        {
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
        }

        public void PopulateBuffers(Industry industry, IndustrySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null) return;

            float contractMultiplier = industry.GetContractMultiplier();

            // 1. Process Formulaic Components (Inputs & Outputs)
            foreach (IndustryComponent c in components)
            {
                if (c is FormulaicIndustryComponent { IsProgressionEnabled: true } formulaic)
                {
                    string? formulaPrefix = !formulaic.sharedStorage ? formulaic.subIdentifier : null;

                    // Inputs
                    foreach (FormulaicIndustryComponent.Term term in formulaic.inputTerms)
                    {
                        if (term.load is not { } inLoad) continue;

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
                    }

                    // Outputs
                    foreach (FormulaicIndustryComponent.Term term in formulaic.outputTerms)
                    {
                        if (term.load is not { } outLoad) continue;

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
                    }
                }
            }

            // 2. Process Buffers for Non-Formulaic Unloaders/Loaders
            foreach (IndustryComponent component in components)
            {
                if (component is IndustryUnloader { load: { } inLoad } unloader)
                {
                    string? formulaPrefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float currentStock = industry.Storage?.QuantityInStorage(inLoad, formulaPrefix) ?? 0f;

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
                    }
                }

                if (component is IndustryLoader { load: { } outLoad } loader)
                {
                    string? formulaPrefix = !loader.sharedStorage ? loader.subIdentifier : null;
                    float currentStock = industry.Storage?.QuantityInStorage(outLoad, formulaPrefix) ?? 0f;

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
                    }
                }
            }
        }

        public void ScanEnRouteTraffic(Industry industry, IndustrySnapshot snapshot)
        {
            // TODO: Scan TrainController.Shared.Cars for en-route cars waybilled to this commercial facility's loaders/unloaders
            // and populate an "En Route (Arriving)" SpurCarGroup with scheduled inbound/outbound buffer tallies.
        }

        public void EvaluateFacilityDiagnostics(Industry industry, IndustrySnapshot snapshot)
        {
            if (industry.usesContract && snapshot.CurrentTier == 0)
            {
                snapshot.State = ProductionState.Idle;
                snapshot.StatusMessage = "No active contract tier set.";
                snapshot.DiagnosticAlert = "Establish a contract in the company window to begin operations.";
                return;
            }

            int totalInputCount = 0;
            int starvedInputCount = 0;
            string starvedInputCargo = string.Empty;

            int totalOutputCount = 0;
            int blockedOutputCount = 0;
            string blockedOutputCargo = string.Empty;

            for (int i = 0; i < snapshot.Buffers.Count; i++)
            {
                if (snapshot.Buffers[i] is CommercialBuffer cb)
                {
                    if (cb.IsInput)
                    {
                        totalInputCount++;
                        if (cb.CurrentAmount <= 0.001f && cb.DailyRate > 0.001f)
                        {
                            starvedInputCount++;
                            starvedInputCargo = cb.CargoDisplayName;
                        }
                    }
                    else
                    {
                        totalOutputCount++;
                        float remaining = Math.Max(0f, cb.MaxCapacity - cb.CurrentAmount);
                        if (remaining <= 0.001f && cb.DailyRate > 0.001f)
                        {
                            blockedOutputCount++;
                            blockedOutputCargo = cb.CargoDisplayName;
                        }
                    }
                }
            }

            if (starvedInputCount > 0 && starvedInputCount == totalInputCount)
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
                    float contractMultiplier = industry.GetContractMultiplier();
                    snapshot.StatusMessage = $"Producing normally at {contractMultiplier * 100f:F0}% throughput.";
                    snapshot.DiagnosticAlert = string.Empty;
                }
            }
        }
    }
}
