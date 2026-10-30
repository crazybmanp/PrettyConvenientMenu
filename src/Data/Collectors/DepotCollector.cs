using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class DepotCollector : IFacilityDataCollector
    {
        public FacilityType FacilityType => FacilityType.Depot;

        public void PopulateFacilityMetadata(Industry industry, FacilitySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Station & House Track";
            snapshot.State = ProductionState.Idle;
            snapshot.StatusMessage = "House track open for local freight shipments and car spotting.";
            snapshot.DiagnosticAlert = string.Empty;
        }

        public void PopulateBuffers(Industry industry, FacilitySnapshot snapshot)
        {
            snapshot.Buffers.Clear();
        }

        public void EvaluateFacilityDiagnostics(Industry industry, FacilitySnapshot snapshot)
        {
        }
    }
}
