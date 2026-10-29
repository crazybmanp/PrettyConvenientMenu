namespace PrettyConvenientMenu.Data.RollingStock
{
    /// <summary>
    /// Explicit operational state of a rolling stock car at a facility or en route.
    /// </summary>
    public enum CarStatusState
    {
        EnRoute,
        ActiveTransfer,
        Queued,
        Blocked,
        Completed,
        Serviced,
        WrongDestination,
        Idle,
        ProgramOverhaul,
        ProgramRepair,
        SpotRepair
    }
}
