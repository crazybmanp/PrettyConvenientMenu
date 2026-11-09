namespace PrettyConvenientMenu.Data.Facilities
{
    /// <summary>
    /// Represents an operational notice or advisory displayed in the facility monitor window.
    /// Stores the notification message and an optional override color (e.g. amber for warnings).
    /// </summary>
    public readonly struct FacilityAdvisory
    {
        public string Message { get; }
        public string? OverrideColor { get; }

        public FacilityAdvisory(string message, string? overrideColor = null)
        {
            Message = message;
            OverrideColor = overrideColor;
        }
    }
}
