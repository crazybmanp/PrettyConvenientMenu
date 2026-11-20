using Game;
using Game.State;
using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Data.Collectors
{
    /// <summary>
    /// Data collector for Interchange facilities: next scheduled service, off-map Bardo returns,
    /// captive load ordering costs, and funds diagnostics.
    /// </summary>
    public sealed class InterchangeCollector : IFacilityDataCollector
    {
        public FacilityType FacilityType => FacilityType.Interchange;

        public void PopulateFacilityMetadata(Industry industry, FacilitySnapshot snapshot)
        {
            Interchange? interchange = industry.GetComponentInChildren<Interchange>();
            if (interchange == null)
            {
                snapshot.TierDescription = "Interchange";
                return;
            }

            GameDateTime nextService = interchange.GetNextServiceTime(TimeWeather.Now, out Interchange.NextServiceStyle style);
            snapshot.InterchangeInfo = new InterchangeSummary
            {
                NextServiceTime = nextService,
                ServiceStyle = style,
                IsDisabled = interchange.Disabled
            };

            snapshot.TierDescription = interchange.Disabled
                ? "Interchange (Disabled)"
                : "Railroad Interchange";
        }

        public void PopulateBuffers(Industry industry, FacilitySnapshot snapshot)
        {
            // Interchanges do not have physical storage buffer tanks/bins.
        }

        public void EvaluateFacilityDiagnostics(Industry industry, FacilitySnapshot snapshot)
        {
            InterchangeSummary? info = snapshot.InterchangeInfo;
            if (info == null)
            {
                return;
            }

            if (info.IsDisabled)
            {
                snapshot.AddAlert("This interchange is currently disabled.", AdvisoryLevel.Notice);
            }

            if (info.OnTrackTotalCost > 0 && StateManager.Shared != null)
            {
                int balance = StateManager.Shared.Balance;
                if (balance < info.OnTrackTotalCost)
                {
                    int shortfall = info.OnTrackTotalCost - balance;
                    snapshot.AddAlert(
                        $"Insufficient funds for on-track interchange orders: need {info.OnTrackTotalCost:C0} (short {shortfall:C0}).",
                        AdvisoryLevel.Caution);
                }
            }
        }
    }
}
