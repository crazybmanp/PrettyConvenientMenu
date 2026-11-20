namespace PrettyConvenientMenu.Data.Spurs
{
    /// <summary>
    /// Explicit operational role of a rolling stock grouping at a facility or en route.
    /// </summary>
    public enum CarGroupRole
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
        Interchange,
        InterchangeBardo,
        EnRouteWaybill,
        EnRouteRepair,
        Unknown
    }
}
