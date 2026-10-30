using System;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.UI.Common
{
    internal static class StatusBadges
    {
        public static string GetBufferRoleBadge(bool isInput) => isInput ? "<color=#90CAF9>[INPUT]</color>" : "<color=#A5D6A7>[OUTPUT]</color>";

        public static string GetSupplyRoleBadge() => "<color=#FFE082>[SUPPLY]</color>";

        public static string GetProductionStateBadge(ProductionState state)
        {
            string stateColor = state switch
            {
                ProductionState.Idle => "#9E9E9E",
                ProductionState.Running => "#4CAF50",
                ProductionState.Blocked => "#EF5350",
                ProductionState.Starved => "#FFA726",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
            };

            return $"<b>PROCESS:</b> [<color={stateColor}><b>{state.ToString().ToUpperInvariant()}</b></color>]";
        }

        public static string GetCarStatusColor(SpottedCarInfo car) => car.StatusState switch
        {
            CarStatusState.EnRoute => "#4DD0E1",
            CarStatusState.ActiveTransfer => "#4CAF50",
            CarStatusState.Completed or CarStatusState.Serviced => "#81C784",
            CarStatusState.Blocked => "#EF5350",
            CarStatusState.WrongDestination => "#FFA726",
            CarStatusState.Idle or CarStatusState.Unassigned => "#9E9E9E",
            CarStatusState.Queued => "#B0BEC5",
            CarStatusState.ProgramOverhaul or CarStatusState.ProgramRepair => "#FF9800",
            CarStatusState.SpotRepair => "#4DD0E1",
            _ => "#9E9E9E"
        };

        public static (string direction, string headerColor) GetSpurGroupStyle(IndustryFacilityType facilityType, SpurCarGroup group) => group.Role switch
        {
            SpurRole.EnRouteWaybill => ("Waybilled to Facility", "#4DD0E1"),
            SpurRole.EnRouteRepair => ("Program Maintenance — Inbound", "#FF9800"),
            SpurRole.DepotHouseTrack => ("House Track — Local Freight", "#FFD54F"),
            SpurRole.TeamTrack => ("Team Track — Public Delivery", "#FFD54F"),
            SpurRole.LocomotiveRepair => ("Service & Repair Track", "#B0BEC5"),
            SpurRole.LocomotiveFueling => ("Inbound — Fuel & Supply Servicing", "#90CAF9"),
            SpurRole.MowStaging => ("Work Equipment & Staging Track", "#FFA726"),
            SpurRole.MowMaterialHandling => ("Material Transfer Track", "#FFA726"),
            SpurRole.CommercialInbound => ("Inbound — Unloading", "#90CAF9"),
            SpurRole.CommercialOutbound => ("Outbound — Loading", "#A5D6A7"),
            SpurRole.CommercialDual => ("Dual — Inbound & Outbound", "#80CBC4"),
            SpurRole.Unknown => ("<color=#FF5252>[UNKNOWN SPUR]</color>", "#FF5252"),
            _ => throw new ArgumentOutOfRangeException(nameof(group.Role), group.Role, null)
        };
    }
}
