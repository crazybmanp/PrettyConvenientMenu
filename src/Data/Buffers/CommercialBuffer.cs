using System;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Storage buffer for commercial factories. Tracks recipe roles, daily rate, and runway.
    /// </summary>
    public sealed class CommercialBuffer : BufferInfo
    {
        public bool IsInput => Role == BufferRole.CommercialInput;
        public float DailyRate { get; set; }
        public TimeSpan? RunwayDuration { get; set; }
    }
}
