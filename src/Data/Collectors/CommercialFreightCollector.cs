using System;
using System.Collections.Generic;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Integration;
using PrettyConvenientMenu.UI.Common;

namespace PrettyConvenientMenu.Data.Collectors
{
    /// <summary>
    /// Collector for commercial freight industries (factories, logging camps, mines, mills).
    /// Populates commercial buffer states, accumulating multi-landing operations directly into summary cards with SubBuffers.
    /// Also surfaces dedicated MoW reserves / sales buffers for plants equipped with IndustrySellers.
    /// </summary>
    public sealed class CommercialFreightCollector : IFacilityDataCollector
    {
        public FacilityType FacilityType => FacilityType.CommercialFreight;

        /// <summary>
        /// Populates contract tier information and progression status.
        /// </summary>
        public void PopulateFacilityMetadata(Industry industry, FacilitySnapshot snapshot)
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

        /// <summary>
        /// Populates commercial input/output buffers directly into snapshot.Buffers.
        /// Distinct physical landings for the same cargo are automatically aggregated by the BufferCollection.
        /// </summary>
        public void PopulateBuffers(Industry industry, FacilitySnapshot snapshot)
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
                    string? qualifier = !formulaic.sharedStorage ? (formulaic.ShortName(industry) ?? formulaic.subIdentifier) : null;

                    // Inputs
                    foreach (FormulaicIndustryComponent.Term term in formulaic.inputTerms)
                    {
                        if (term.load is not { } inLoad) continue;
                        float dailyRate = term.unitsPerDay * contractMultiplier;
                        float currentStock = industry.Storage?.QuantityInStorage(inLoad, formulaPrefix) ?? 0f;
                        float maxStorage = FindMaxStorageForLoad(components, inLoad, formulaPrefix);
                        snapshot.Buffers.AddCommercial(inLoad, true, currentStock, maxStorage, dailyRate, formulaPrefix, qualifier);
                    }

                    // Outputs
                    foreach (FormulaicIndustryComponent.Term term in formulaic.outputTerms)
                    {
                        if (term.load is not { } outLoad) continue;
                        float dailyRate = term.unitsPerDay * contractMultiplier;
                        float currentStock = industry.Storage?.QuantityInStorage(outLoad, formulaPrefix) ?? 0f;
                        float maxStorage = FindMaxStorageForLoad(components, outLoad, formulaPrefix);
                        snapshot.Buffers.AddCommercial(outLoad, false, currentStock, maxStorage, dailyRate, formulaPrefix, qualifier);
                    }
                }
            }

            // 2. Process Buffers for Non-Formulaic Unloaders/Loaders
            foreach (IndustryComponent component in components)
            {
                if (component is IndustryUnloader { load: { } inLoad } unloader)
                {
                    string? formulaPrefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    string? qualifier = !unloader.sharedStorage ? (unloader.ShortName(industry) ?? unloader.subIdentifier) : null;
                    float currentStock = industry.Storage?.QuantityInStorage(inLoad, formulaPrefix) ?? 0f;
                    float dailyRate = unloader.storageConsumptionRate * contractMultiplier;
                    snapshot.Buffers.AddCommercial(inLoad, true, currentStock, unloader.maxStorage, dailyRate, formulaPrefix, qualifier);
                }

                if (component is IndustryLoaderBase { load: { } outLoad } loader)
                {
                    string? formulaPrefix = !loader.sharedStorage ? loader.subIdentifier : null;
                    string? qualifier = !loader.sharedStorage ? (loader.ShortName(industry) ?? loader.subIdentifier) : null;
                    float currentStock = industry.Storage?.QuantityInStorage(outLoad, formulaPrefix) ?? 0f;
                    float dailyRate = loader.productionRate * contractMultiplier;
                    snapshot.Buffers.AddCommercial(outLoad, false, currentStock, loader.maxStorage, dailyRate, formulaPrefix, qualifier);
                }
            }

            // 3. Process MoW Sellers (Maintenance Reserves / MoW Sales)
            foreach (IndustryComponent component in components)
            {
                if (component is IndustrySeller seller && !string.IsNullOrEmpty(seller.loadId) && CarLoadLibrary.GetLoad(seller.loadId) is { } outLoad)
                {
                    bool isMowModActive = MowProducerPriorityAdapter.TryGetReserveInfo(
                        industry.identifier, outLoad.id, seller.Identifier, out MowReserveInfo reserveInfo) && reserveInfo.IsActive;

                    bool isDecoupled = isMowModActive || !seller.sharedStorage;
                    string? prefix = isDecoupled ? seller.subIdentifier : null;
                    string qualifier = isDecoupled ? "MoW Reserve" : "MoW Sales";

                    float currentStock = isMowModActive && reserveInfo.Stock > 0.001f
                        ? reserveInfo.Stock
                        : (industry.Storage?.QuantityInStorage(outLoad, prefix) ?? 0f);

                    float maxStorage = (isMowModActive && reserveInfo.Cap > 0.001f)
                        ? reserveInfo.Cap
                        : FindMaxStorageForLoad(components, outLoad, null);

                    snapshot.Buffers.AddReserve(
                        outLoad,
                        BufferRole.MaintenanceReserve,
                        currentStock,
                        Math.Max(0.001f, maxStorage),
                        prefix ?? seller.subIdentifier,
                        qualifier);
                }
            }
        }

        /// <summary>
        /// Evaluates industry operational status (Starved, Blocked, Running, Idle) based on buffer fill levels.
        /// </summary>
        public void EvaluateFacilityDiagnostics(Industry industry, FacilitySnapshot snapshot)
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

            // Check for active MoW reserve refill prioritization
            for (int i = 0; i < snapshot.Buffers.Count; i++)
            {
                if (snapshot.Buffers[i] is ReserveBuffer rb && rb.Role == BufferRole.MaintenanceReserve)
                {
                    if (MowProducerPriorityAdapter.TryGetReserveInfo(industry.identifier, rb.CargoType, null, out MowReserveInfo rInfo) && rInfo.IsRefilling)
                    {
                        float needed = Math.Max(0f, rInfo.Cap - rInfo.Stock);
                        snapshot.DiagnosticAlert = $"Production prioritized to MoW reserve ({FormatHelper.FormatQuantity(needed, rb.Units)} needed before outbound shipping resumes).";
                        snapshot.DiagnosticAlertColor = "#FFD54F";
                        break;
                    }
                }
            }
        }

        private static float FindMaxStorageForLoad(List<IndustryComponent>? components, Load? targetLoad, string? storagePrefix = null)
        {
            if (components == null || targetLoad == null) return 100f;

            foreach (IndustryComponent comp in components)
            {
                string? compPrefix = !comp.sharedStorage ? comp.subIdentifier : null;
                if (storagePrefix != null && !string.Equals(compPrefix, storagePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (comp is IndustryUnloader unloader && unloader.load == targetLoad)
                {
                    return Math.Max(0.001f, unloader.maxStorage);
                }
                if (comp is IndustryLoaderBase loader && loader.load == targetLoad)
                {
                    return Math.Max(0.001f, loader.maxStorage);
                }
            }
            return 100f;
        }
    }
}
