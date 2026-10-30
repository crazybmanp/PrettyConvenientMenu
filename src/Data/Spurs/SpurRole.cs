namespace PrettyConvenientMenu.Data.Spurs
{
    /// <summary>
    /// Explicit operational purpose of a spur track or rolling stock grouping.
    /// </summary>
    public enum SpurRole
    {
        CommercialInbound,
        CommercialOutbound,
        CommercialDual,
        LocomotiveFueling,
        LocomotiveRepair,
        MowStaging,
        MowMaterialHandling,
        DepotHouseTrack,
        TeamTrack,
        EnRouteWaybill,
        EnRouteRepair,
        Unknown
    }
}
