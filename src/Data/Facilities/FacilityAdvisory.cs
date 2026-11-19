namespace PrettyConvenientMenu.Data.Facilities
{
    /// <summary>
    /// Operational severity level for facility advisories, following railroad dispatching terminology.
    /// </summary>
    public enum AdvisoryLevel
    {
        Hold,
        Caution,
        Notice
    }

    /// <summary>
    /// Represents an operational notice or advisory displayed in the facility monitor window.
    /// </summary>
    public readonly struct FacilityAdvisory
    {
        public string Message { get; }
        public AdvisoryLevel Level { get; }

        public FacilityAdvisory(string message, AdvisoryLevel level = AdvisoryLevel.Notice)
        {
            Message = message;
            Level = level;
        }
    }
}
