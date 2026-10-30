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

        public static string GetCarStatusColor(SpottedCarInfo car) => car.Status switch
        {
            CarStatus.EnRoute => "#4DD0E1",
            CarStatus.ActiveTransfer => "#4CAF50",
            CarStatus.Completed or CarStatus.Serviced => "#81C784",
            CarStatus.Blocked => "#EF5350",
            CarStatus.WrongDestination => "#FFA726",
            CarStatus.Idle or CarStatus.Unassigned => "#9E9E9E",
            CarStatus.Queued => "#B0BEC5",
            CarStatus.ProgramOverhaul or CarStatus.ProgramRepair => "#FF9800",
            CarStatus.SpotRepair => "#4DD0E1",
            _ => "#9E9E9E"
        };

        public static (string direction, string headerColor) GetCarGroupStyle(FacilityType facilityType, FacilityCarGroup group) => group.Role switch
        {
            CarGroupRole.EnRouteWaybill => ("Waybilled to Facility", "#4DD0E1"),
            CarGroupRole.EnRouteRepair => ("Program Maintenance — Inbound", "#FF9800"),
            CarGroupRole.DepotHouseTrack => ("House Track — Local Freight", "#FFD54F"),
            CarGroupRole.TeamTrack => ("Team Track — Public Delivery", "#FFD54F"),
            CarGroupRole.LocomotiveRepair => ("Service & Repair Track", "#B0BEC5"),
            CarGroupRole.LocomotiveFueling => ("Inbound — Fuel & Supply Servicing", "#90CAF9"),
            CarGroupRole.MowStaging => ("Work Equipment & Staging Track", "#FFA726"),
            CarGroupRole.MowMaterialHandling => ("Material Transfer Track", "#FFA726"),
            CarGroupRole.CommercialInbound => ("Inbound — Unloading", "#90CAF9"),
            CarGroupRole.CommercialOutbound => ("Outbound — Loading", "#A5D6A7"),
            CarGroupRole.CommercialDual => ("Dual — Inbound & Outbound", "#80CBC4"),
            CarGroupRole.Unknown => ("<color=#FF5252>[UNKNOWN SPUR]</color>", "#FF5252"),
            _ => throw new ArgumentOutOfRangeException(nameof(group.Role), group.Role, null)
        };
    }
}
