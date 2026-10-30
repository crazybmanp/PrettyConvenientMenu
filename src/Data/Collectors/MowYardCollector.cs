using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class MowYardCollector : IFacilityDataCollector
    {
        public FacilityType FacilityType => FacilityType.MaintenanceOfWay;

        public void PopulateFacilityMetadata(Industry industry, FacilitySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Maintenance of Way Material Depot";
            snapshot.State = ProductionState.Running;
            snapshot.StatusMessage = "Material reserves and staging tracks for track maintenance work trains.";
            snapshot.DiagnosticAlert = string.Empty;
        }

        public void PopulateBuffers(Industry industry, FacilitySnapshot snapshot)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null) return;

            foreach (IndustryComponent component in components)
            {
                if (component is StorageUnloader storageUnloader)
                {
                    string? prefix = !storageUnloader.sharedStorage ? storageUnloader.subIdentifier : null;
                    foreach (StorageUnloader.LoadEntry entry in storageUnloader.loads)
                    {
                        if (entry.load is not { } load) continue;

                        float capacity = entry.storage * load.NominalQuantityPerCarLoad;
                        float currentStock = industry.Storage?.QuantityInStorage(load, prefix) ?? 0f;

                        ReserveBuffer buf = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.MaintenanceReserve);
                        buf.CurrentAmount = currentStock;
                        buf.MaxCapacity = Mathf.Max(0.001f, capacity);
                    }
                }
                else if (component is StorageLoader storageLoader)
                {
                    string? prefix = !storageLoader.sharedStorage ? storageLoader.subIdentifier : null;
                    foreach (StorageLoader.LoadEntry entry in storageLoader.loads)
                    {
                        if (entry.load is not { } load) continue;

                        ReserveBuffer buf = BufferHelper.GetOrAddReserveBuffer(snapshot.Buffers, load, BufferRole.MaintenanceReserve);
                        if (buf.MaxCapacity <= 0.001f)
                        {
                            float currentStock = industry.Storage?.QuantityInStorage(load, prefix) ?? 0f;
                            buf.CurrentAmount = currentStock;
                            buf.MaxCapacity = Mathf.Max(0.001f, load.NominalQuantityPerCarLoad * 10f);
                        }
                    }
                }
            }
        }

        public void EvaluateFacilityDiagnostics(Industry industry, FacilitySnapshot snapshot)
        {
            // MOW yards are passive stockpiles and staging tracks for work trains, not active manufacturing facilities.
            // Empty reserves are normal and do not halt operations.
            snapshot.DiagnosticAlert = string.Empty;
        }
    }
}
