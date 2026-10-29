using System;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets.ProgressBar;
using TMPro;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Widgets
{
    public enum BufferCardMode
    {
        Commercial,
        EngineSupply,
        MowReserve
    }

    internal static class BufferCard
    {
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

            string headerLine;
            if (buf is CommercialBuffer commercial)
            {
                headerLine = $"{StatusBadges.GetBufferRoleBadge(commercial.IsInput)} <b>{buf.CargoDisplayName}</b>:  {currentText} / {maxText}  ({pctText})";
            }
            else if (mode == BufferCardMode.EngineSupply)
            {
                headerLine = $"{StatusBadges.GetSupplyRoleBadge()} <b>{buf.CargoDisplayName}</b>:  {currentText} / {maxText}  ({pctText})";
            }
            else
            {
                headerLine = $"<b>{buf.CargoDisplayName}</b>:  {currentText} / {maxText}  ({pctText})";
            }
            builder.AddLabel(headerLine);

            // 2. Available Amount Line (for Supply & Reserve modes)
            if (mode == BufferCardMode.EngineSupply || mode == BufferCardMode.MowReserve)
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

            // 3. Progress Bar with Overlays & Calipers
            float cap = buf.MaxCapacity;
            float loadRatio = cap > 0f ? buf.ActiveOutbound / cap : 0f;
            float commRatio = cap > 0f ? buf.CommittedOutbound / cap : 0f;
            float unloadRatio = cap > 0f ? buf.ActiveInbound / cap : 0f;
            float inbRatio = cap > 0f ? buf.ScheduledInbound / cap : 0f;

            switch (mode)
            {
                case BufferCardMode.Commercial:
                    ProgressBarFactory.CreateCommercialBar(builder, buf.FillRatio, unloadRatio, loadRatio, commRatio, inbRatio);
                    break;
                case BufferCardMode.EngineSupply:
                    if (buf is ReserveBuffer repairBuf && repairBuf.IsRepairParts)
                    {
                        float baseRatio = buf.FillRatio;
                        float unloadingRatio = cap > 0f ? buf.ActiveInbound / cap : 0f;
                        float programRatio = cap > 0f ? repairBuf.ProgramDemand / cap : 0f;
                        float spotRatio = cap > 0f ? repairBuf.SpotDemand / cap : 0f;
                        float arrivingRatio = cap > 0f ? buf.ScheduledInbound / cap : 0f;
                        ProgressBarFactory.CreateRepairPartsBar(builder, baseRatio, unloadingRatio, programRatio, spotRatio, arrivingRatio);
                    }
                    else
                    {
                        ProgressBarFactory.CreateEngineSupplyBar(builder, buf.FillRatio, unloadRatio, loadRatio, commRatio, inbRatio);
                    }
                    break;
                case BufferCardMode.MowReserve:
                    ProgressBarFactory.CreateMowBar(builder, buf.FillRatio, unloadRatio, loadRatio, commRatio, inbRatio);
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
            if (!isRepairPartsBuffer && (mode == BufferCardMode.EngineSupply || mode == BufferCardMode.MowReserve))
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
