using System;
using System.Collections.Generic;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets.ProgressBar;
using TMPro;
using UI.Builder;
using UnityEngine;

namespace PrettyConvenientMenu.UI.Widgets
{
    public enum BufferCardMode
    {
        Commercial,
        EngineSupply,
        MowReserve
    }

    /// <summary>
    /// Renders an individual storage buffer card with header info, progress bar, transfer rates, and runway metrics.
    /// </summary>
    internal static class BufferCard
    {
        /// <summary>
        /// Renders the buffer card into the provided UI builder.
        /// </summary>
        /// <param name="builder">Target UI builder.</param>
        /// <param name="buf">Buffer data to render.</param>
        /// <param name="mode">Display mode (Commercial, EngineSupply, or MowReserve).</param>
        public static void Render(UIPanelBuilder builder, BufferInfo? buf, BufferCardMode mode = BufferCardMode.Commercial)
        {
            if (buf == null)
            {
                return;
            }

            // 1. Header Line
            string currentText = FormatHelper.FormatQuantity(buf.CurrentAmount, buf.Units);
            string maxText = FormatHelper.FormatQuantity(buf.MaxCapacity, buf.Units);
            string pctText = $"{buf.FillRatio * 100f:F0}%";

            string badge = (mode, buf.Role) switch
            {
                (BufferCardMode.Commercial, BufferRole.CommercialInput) => $"{StatusBadges.GetBufferRoleBadge(true)} ",
                (BufferCardMode.Commercial, BufferRole.CommercialOutput) => $"{StatusBadges.GetBufferRoleBadge(false)} ",
                (BufferCardMode.Commercial, BufferRole.MaintenanceReserve) => $"{StatusBadges.GetReserveRoleBadge()} ",
                (BufferCardMode.EngineSupply, _) => $"{StatusBadges.GetSupplyRoleBadge()} ",
                _ => string.Empty
            };
            builder.AddLabel($"{badge}<b>{buf.CargoDisplayName}</b>:  {currentText} / {maxText}  ({pctText})");

            // 2. Available Amount Line (for Supply & Reserve modes or maintenance reserves)
            if (mode == BufferCardMode.EngineSupply || mode == BufferCardMode.MowReserve || buf.Role == BufferRole.MaintenanceReserve)
            {
                float avail = buf is ReserveBuffer res ? res.AvailableAmount : Math.Max(0f, buf.CurrentAmount - buf.TotalOutbound);
                string availText = FormatHelper.FormatQuantity(avail, buf.Units);
                string availLine = $"<size=12><color=#B0BEC5>Available: <color=#ECEFF1>{availText}</color>";
                if (buf.ActiveInbound > 0.001f)
                {
                    availLine += $" <color=#4CAF50>({FormatHelper.FormatQuantity(buf.ActiveInbound, buf.Units)} unloading)</color>";
                }
                if (buf.ScheduledInbound > 0.001f)
                {
                    availLine += $" <color=#4CAF50>({FormatHelper.FormatQuantity(buf.ScheduledInbound, buf.Units)} scheduled inbound)</color>";
                }
                availLine += "</color></size>";
                builder.AddLabel(availLine);
            }

            // Prepare lower-half landing stack if sub-buffers exist
            List<LandingSegment>? landingStack = null;
            if (buf is ICompositeBuffer composite && composite.SubBuffers.Count > 0 && buf.MaxCapacity > 0f)
            {
                landingStack = new List<LandingSegment>(composite.SubBuffers.Count);
                for (int i = 0; i < composite.SubBuffers.Count; i++)
                {
                    BufferInfo landing = composite.SubBuffers[i];
                    float ratio = Mathf.Clamp01(landing.CurrentAmount / buf.MaxCapacity);
                    float outRatio = Mathf.Clamp01(landing.ActiveOutbound / buf.MaxCapacity);
                    float inRatio = Mathf.Clamp01(landing.ActiveInbound / buf.MaxCapacity);
                    Color color = landing.SwatchColor ?? Color.white;
                    string label = landing.Qualifier ?? landing.StoragePrefix ?? string.Empty;
                    landingStack.Add(new LandingSegment(ratio, outRatio, inRatio, color, label));
                }
            }

            // 3. Progress Bar with Overlays, Calipers & Optional Landing Stack
            switch ((mode, buf.Role))
            {
                case (BufferCardMode.Commercial, BufferRole.MaintenanceReserve):
                case (BufferCardMode.MowReserve, _):
                    ProgressBarFactory.CreateMowBar(builder, buf.FillRatio, buf.ActiveInboundRatio, buf.ActiveOutboundRatio, buf.CommittedOutboundRatio, buf.ScheduledInboundRatio);
                    break;
                case (BufferCardMode.Commercial, _):
                    ProgressBarFactory.CreateCommercialBar(
                        builder,
                        buf.FillRatio,
                        buf.ActiveInboundRatio,
                        buf.ActiveOutboundRatio,
                        buf.CommittedOutboundRatio,
                        buf.ScheduledInboundRatio,
                        landingStack);
                    break;
                case (BufferCardMode.EngineSupply, _):
                    if (buf is ReserveBuffer repairBuf && repairBuf.IsRepairParts)
                    {
                        float cap = buf.MaxCapacity;
                        float programRatio = cap > 0f ? repairBuf.ProgramDemand / cap : 0f;
                        float spotRatio = cap > 0f ? repairBuf.SpotDemand / cap : 0f;
                        ProgressBarFactory.CreateRepairPartsBar(builder, buf.FillRatio, buf.ActiveInboundRatio, programRatio, spotRatio, buf.ScheduledInboundRatio);
                    }
                    else
                    {
                        ProgressBarFactory.CreateEngineSupplyBar(builder, buf.FillRatio, buf.ActiveInboundRatio, buf.ActiveOutboundRatio, buf.CommittedOutboundRatio, buf.ScheduledInboundRatio);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            // 4. Active Transfer Row (Loading / Unloading) - Skipped for repair parts since metric rows display Unloading
            bool isRepairPartsBuffer = buf is ReserveBuffer rb && rb.IsRepairParts;
            if (!isRepairPartsBuffer)
            {
                bool hasActiveRow = buf.ActiveOutbound > 0.001f || buf.ActiveInbound > 0.001f;
                if (hasActiveRow)
                {
                    string leftText = buf.ActiveOutbound > 0.001f
                        ? $"<color=#FF9800>Loading: {FormatHelper.FormatQuantity(buf.ActiveOutbound, buf.Units)}</color>"
                        : string.Empty;

                    string rightText = buf.ActiveInbound > 0.001f
                        ? $"<color=#4CAF50>Unloading: {FormatHelper.FormatQuantity(buf.ActiveInbound, buf.Units)}</color>"
                        : string.Empty;

                    SplitInfoRow.Add(builder, leftText, rightText, 0f, 180f);
                }
            }

            // 5. En-Route Row (Committed / Inbound) - Skipped for repair parts since metric rows display Arriving
            if (!isRepairPartsBuffer && (mode == BufferCardMode.EngineSupply || mode == BufferCardMode.MowReserve || buf.Role == BufferRole.MaintenanceReserve))
            {
                bool hasEnRouteRow = buf.CommittedOutbound > 0.001f || buf.ScheduledInbound > 0.001f;
                if (hasEnRouteRow)
                {
                    string leftText = buf.CommittedOutbound > 0.001f
                        ? $"<color=#B26A00>Committed: {FormatHelper.FormatQuantity(buf.CommittedOutbound, buf.Units)} (En Route)</color>"
                        : string.Empty;

                    string rightText = buf.ScheduledInbound > 0.001f
                        ? $"<color=#2E7D32>Inbound: {FormatHelper.FormatQuantity(buf.ScheduledInbound, buf.Units)} (En Route)</color>"
                        : string.Empty;

                    SplitInfoRow.Add(builder, leftText, rightText, 0f, 180f);
                }
            }

            // 5b. Repair Parts Demand & Advisory Rows
            if (buf is ReserveBuffer reserve && reserve.IsRepairParts)
            {
                float currentStock = reserve.CurrentAmount + reserve.ActiveInbound;
                float totalStock = currentStock + reserve.ScheduledInbound;

                // Row 1: Program (Orange, left) and Unloading (Green, right)
                builder.HStack(row =>
                {
                    row.ConfigureLayout(4f);
                    row.AddLabel($"<size=12><color=#FF9800>Program: {FormatHelper.FormatWeight(reserve.ProgramDemand)}</color></size>", t =>
                    {
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).FlexibleWidth(1f);

                    row.AddLabel($"<size=12><color=#4CAF50>Unloading: {FormatHelper.FormatWeight(reserve.ActiveInbound)}</color></size>", t =>
                    {
                        t.alignment = TextAlignmentOptions.Right;
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).FlexibleWidth(1f);
                });

                // Row 2: Spot (Cyan, left) and Arriving (Green, right)
                builder.HStack(row =>
                {
                    row.ConfigureLayout(4f);
                    row.AddLabel($"<size=12><color=#4DD0E1>Spot: {FormatHelper.FormatWeight(reserve.SpotDemand)}</color></size>", t =>
                    {
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).FlexibleWidth(1f);

                    row.AddLabel($"<size=12><color=#4CAF50>Arriving: {FormatHelper.FormatWeight(reserve.ScheduledInbound)}</color></size>", t =>
                    {
                        t.alignment = TextAlignmentOptions.Right;
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).FlexibleWidth(1f);
                });

                // Advisory Row
                // Mode 1: Insufficient parts for program demand
                if (totalStock < reserve.ProgramDemand - 0.001f)
                {
                    builder.AddLabel("<size=12><color=#FFD54F><b>[ADVISORY]</b> Insufficient parts for program demand.</color></size>", t =>
                    {
                        t.textWrappingMode = TextWrappingModes.Normal;
                    });
                }
                // Mode 2: Spot repairs may consume program demand
                else if (totalStock - reserve.ProgramDemand < reserve.SpotDemand - 0.001f)
                {
                    builder.AddLabel("<size=12><color=#FFD54F><b>[ADVISORY]</b> Spot repairs may consume program demand.</color></size>", t =>
                    {
                        t.textWrappingMode = TextWrappingModes.Normal;
                    });
                }
            }

            // 6. Commercial Footer Row (Rate / Runway Duration)
            if (mode == BufferCardMode.Commercial && buf is CommercialBuffer comm)
            {
                string rateStr = $"Rate: {FormatHelper.FormatDailyRate(comm.DailyRate, comm.Units)}";
                string durationStr = FormatHelper.FormatDuration(comm.RunwayDuration);
                string runwayStr = comm.IsInput
                    ? $"Runway: {durationStr} remaining (without deliveries)"
                    : $"Full in: {durationStr} (without shipments)";

                SplitInfoRow.Add(builder, rateStr, runwayStr, 220f, 0f);
            }

            builder.Spacer(4f);
        }
    }
}
