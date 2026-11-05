using Model.Definition.Data;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Base class representing a physical or logical storage buffer at an industry facility.
    /// Tracks current stock, capacity, and active/scheduled material movements.
    /// </summary>
    public abstract class BufferInfo
    {
        public string CargoType { get; set; } = string.Empty;
        public string CargoDisplayName { get; set; } = string.Empty;
        public string? StoragePrefix { get; set; }
        public string? Qualifier { get; set; }
        public LoadUnits Units { get; set; } = LoadUnits.Pounds;
        public BufferRole Role { get; set; } = BufferRole.CommercialInput;
        public virtual float CurrentAmount { get; set; }
        public virtual float MaxCapacity { get; set; }

        // Color Swatch for Multi-Landing Buffers
        public Color? SwatchColor { get; set; }
        public string? SwatchHex { get; set; }

        // 2x2 Transfer Model (Active vs Scheduled)
        public virtual float ActiveInbound { get; set; }
        public virtual float ActiveOutbound { get; set; }
        public virtual float ScheduledInbound { get; set; }
        public virtual float CommittedOutbound { get; set; }

        public float TotalInbound => ActiveInbound + ScheduledInbound;
        public float TotalOutbound => ActiveOutbound + CommittedOutbound;

        // Normalized 0..1 Ratios
        public float FillRatio => MaxCapacity > 0f ? Mathf.Clamp01(CurrentAmount / MaxCapacity) : 0f;
        public float ActiveInboundRatio => MaxCapacity > 0f ? Mathf.Clamp01(ActiveInbound / MaxCapacity) : 0f;
        public float ActiveOutboundRatio => MaxCapacity > 0f ? Mathf.Clamp01(ActiveOutbound / MaxCapacity) : 0f;
        public float ScheduledInboundRatio => MaxCapacity > 0f ? Mathf.Clamp01(ScheduledInbound / MaxCapacity) : 0f;
        public float CommittedOutboundRatio => MaxCapacity > 0f ? Mathf.Clamp01(CommittedOutbound / MaxCapacity) : 0f;
    }
}
