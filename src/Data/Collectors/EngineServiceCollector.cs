using System;
using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class EngineServiceCollector : IFacilityDataCollector
    {
        public FacilityType FacilityType => FacilityType.EngineService;

        public void PopulateFacilityMetadata(Industry industry, FacilitySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Locomotive Service Facility";
            snapshot.State = ProductionState.Running;
            snapshot.StatusMessage = "Terminal reserves ready for locomotive fueling, servicing, and repair.";
            snapshot.DiagnosticAlert = string.Empty;
        }

        public void PopulateBuffers(Industry industry, FacilitySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null) return;

            RepairTrack? repairTrack = null;
            foreach (IndustryComponent comp in components)
            {
                if (comp is RepairTrack rt)
                {
                    repairTrack = rt;
                    break;
                }
            }

            foreach (IndustryComponent component in components)
            {
                if (component is IndustryUnloader unloader && unloader.load is { } load)
                {
                    string? prefix = !unloader.sharedStorage ? unloader.subIdentifier : null;
                    float currentStock = industry.Storage != null
                        ? industry.Storage.QuantityInStorage(load, prefix)
                        : 0f;

                    bool isRepairParts = repairTrack != null &&
                        (string.Equals(load.id, repairTrack.repairPartsLoadId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(unloader.ShortName(industry), repairTrack.ShortName(industry), StringComparison.OrdinalIgnoreCase));

                    BufferRole role = isRepairParts ? BufferRole.MaintenanceReserve : BufferRole.LocomotiveSupply;

                    ReserveBuffer buffer = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, role);
                    buffer.CurrentAmount = currentStock;
                    buffer.MaxCapacity = Mathf.Max(0.001f, unloader.maxStorage);
                    buffer.IsRepairParts = isRepairParts;
                }
            }
        }

        public void EvaluateFacilityDiagnostics(Industry industry, FacilitySnapshot snapshot)
        {
            // Engine terminals are servicing depots with optional supplies (e.g. diesel, coal, sand, repair parts).
            // Empty reserves are normal operational state (e.g. running an all-steam roster) and do not halt operations.
            snapshot.DiagnosticAlert = string.Empty;
        }
    }
}
