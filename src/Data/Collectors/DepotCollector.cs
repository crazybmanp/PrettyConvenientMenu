using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.Helpers;

namespace PrettyConvenientMenu.Data.Collectors
{
    public sealed class DepotCollector : IFacilityDataCollector
    {
        public IndustryFacilityType FacilityType => IndustryFacilityType.Depot;

        public void Collect(Industry industry, IndustrySnapshot snapshot)
        {
            snapshot.CurrentTier = 0;
            snapshot.TierDescription = "Station & House Track";
            snapshot.State = ProductionState.Idle;
            snapshot.StatusMessage = "House track open for local freight shipments and car spotting.";
            snapshot.DiagnosticAlert = string.Empty;

            snapshot.Buffers.Clear();

            List<IndustryComponent>? components = industry.Components;
            if (components == null)
            {
                return;
            }

            OpsController? opsController = OpsController.Shared;

            for (int i = 0; i < components.Count; i++)
            {
                IndustryComponent component = components[i];
                if (component is TeamTrack teamTrack && opsController != null)
                {
                    CarTrackingHelper.CollectTeamTrackCars(opsController, teamTrack, component, industry, snapshot);
                }
            }
        }
    }
}
