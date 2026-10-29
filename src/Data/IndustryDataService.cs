using System;
using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Collectors;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Data
{
    public static class IndustryDataService
    {
        private static readonly Dictionary<IndustryFacilityType, IFacilityDataCollector> Collectors = new Dictionary<IndustryFacilityType, IFacilityDataCollector>
        {
            { IndustryFacilityType.CommercialFreight, new CommercialFreightCollector() },
            { IndustryFacilityType.EngineService, new EngineServiceCollector() },
            { IndustryFacilityType.MaintenanceOfWay, new MowYardCollector() },
            { IndustryFacilityType.Depot, new DepotCollector() }
        };

        public static IndustryFacilityType DetermineFacilityType(Industry industry) => FacilityClassifier.Classify(industry);

        public static void PopulateSnapshot(Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.Clear();
            snapshot.IndustryId = industry.identifier ?? string.Empty;
            snapshot.DisplayName = industry.DisplayName ?? "Unknown Industry";
            snapshot.FacilityType = DetermineFacilityType(industry);

            if (Collectors.TryGetValue(snapshot.FacilityType, out IFacilityDataCollector collector))
            {
                collector.Collect(industry, snapshot);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(snapshot.FacilityType), snapshot.FacilityType, $"No collector registered for facility type '{snapshot.FacilityType}'.");
            }
        }
    }
}
