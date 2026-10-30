using System;
using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Collectors;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Data
{
    /// <summary>
    /// Central orchestration service that compiles the complete operational state of an <see cref="Industry"/>
    /// into an <see cref="IndustrySnapshot"/> consumed by the production monitoring UI.
    /// Coordinates facility classification, buffer calculation, track spur classification, 
    /// en-route traffic detection, and operational diagnostics through a deterministic lifecycle.
    /// </summary>
    public static class IndustryDataService
    {
        private static readonly Dictionary<IndustryFacilityType, IFacilityDataCollector> Collectors = new Dictionary<IndustryFacilityType, IFacilityDataCollector>
        {
            { IndustryFacilityType.CommercialFreight, new CommercialFreightCollector() },
            { IndustryFacilityType.EngineService, new EngineServiceCollector() },
            { IndustryFacilityType.MaintenanceOfWay, new MowYardCollector() },
            { IndustryFacilityType.Depot, new DepotCollector() }
        };

        /// <summary>
        /// Populates an operational snapshot for the given industry. 
        /// If an existing snapshot is provided, it is reset and mutated in-place (avoiding allocations during UI refresh loops).
        /// If omitted or null, a new snapshot instance is created and returned.
        /// </summary>
        /// <param name="industry">The target industry.</param>
        /// <param name="snapshot">Optional existing snapshot instance to mutate in-place.</param>
        /// <returns>The populated snapshot instance.</returns>
        public static IndustrySnapshot PopulateSnapshot(Industry industry, IndustrySnapshot? snapshot = null)
        {
            snapshot ??= new IndustrySnapshot();
            IFacilityDataCollector collector = GetCollector(FacilityClassifier.Classify(industry));
            ExecuteLifecycle(collector, industry, snapshot);
            return snapshot;
        }

        /// <summary>
        /// Retrieves the registered collector implementation responsible for gathering data for the specified facility type.
        /// </summary>
        /// <param name="facilityType">The facility archetype.</param>
        /// <returns>The registered <see cref="IFacilityDataCollector"/> instance.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if no collector is mapped for the facility type.</exception>
        public static IFacilityDataCollector GetCollector(IndustryFacilityType facilityType)
        {
            if (Collectors.TryGetValue(facilityType, out IFacilityDataCollector collector))
            {
                return collector;
            }

            throw new ArgumentOutOfRangeException(nameof(facilityType), facilityType, $"No collector registered for facility type '{facilityType}'.");
        }

        /// <summary>
        /// Executes the deterministic 5-stage snapshot collection lifecycle in strict order:
        /// 1. Reset state &amp; identity
        /// 2. Metadata &amp; baseline contract state
        /// 3. Commodity buffer stock &amp; capacities
        /// 4. Physical spur grouping &amp; car classification
        /// 5. En-route traffic detection
        /// 6. Facility diagnostic evaluation
        /// </summary>
        /// <param name="collector">The specialized facility collector.</param>
        /// <param name="industry">The game industry instance.</param>
        /// <param name="snapshot">The snapshot being assembled.</param>
        public static void ExecuteLifecycle(IFacilityDataCollector collector, Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.Reset(industry, collector.FacilityType);
            collector.PopulateFacilityMetadata(industry, snapshot);
            collector.PopulateBuffers(industry, snapshot);
            SpurClassifier.ClassifyAllSpurs(industry, snapshot, OpsController.Shared);
            collector.ScanEnRouteTraffic(industry, snapshot);
            collector.EvaluateFacilityDiagnostics(industry, snapshot);
        }
    }
}
