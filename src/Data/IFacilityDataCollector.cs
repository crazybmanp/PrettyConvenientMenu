using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Data
{
    /// <summary>
    /// Defines the functional contract for collecting and calculating snapshot data for a specific facility archetype.
    /// </summary>
    public interface IFacilityDataCollector
    {
        /// <summary>The facility archetype handled by this collector implementation.</summary>
        FacilityType FacilityType { get; }

        /// <summary>
        /// Populates facility identification, active contract tier, and baseline operational status text.
        /// </summary>
        void PopulateFacilityMetadata(Industry industry, FacilitySnapshot snapshot);

        /// <summary>
        /// Computes storage inventory, nominal throughput rates, maximum capacities, and runway durations for all facility storage buffers.
        /// </summary>
        void PopulateBuffers(Industry industry, FacilitySnapshot snapshot);

        /// <summary>
        /// Evaluates overall facility health (starved, blocked, running, or idle) and generates user diagnostics based on populated buffer and car states.
        /// </summary>
        void EvaluateFacilityDiagnostics(Industry industry, FacilitySnapshot snapshot);
    }
}
