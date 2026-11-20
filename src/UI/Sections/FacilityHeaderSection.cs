using System;
using System.Collections.Generic;
using Game;
using Game.State;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.UI.Common;
using TMPro;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class FacilityHeaderSection
    {
        public static void Build(UIPanelBuilder builder, FacilitySnapshot snapshot)
        {
            if (snapshot.FacilityType == FacilityType.CommercialFreight)
            {
                BuildCommercialHeader(builder, snapshot);
                builder.Spacer(6f);
            }
            else if (snapshot.FacilityType == FacilityType.Interchange && snapshot.InterchangeInfo != null)
            {
                BuildInterchangeHeader(builder, snapshot.InterchangeInfo);
                builder.Spacer(6f);
            }
        }

        private static void BuildInterchangeHeader(UIPanelBuilder builder, InterchangeSummary info)
        {
            if (info.IsDisabled)
            {
                builder.AddLabel("<b>SERVICE:</b> [<color=#EF5350><b>DISABLED</b></color>]");
            }
            else
            {
                GameDateTime now = TimeWeather.Now;
                string styleLabel = info.ServiceStyle == Model.Ops.Interchange.NextServiceStyle.Extra ? "Extra" : "Daily";
                double hoursUntil = Math.Max(0.0, (info.NextServiceTime - now) * (1.0 / 3600.0));
                string etaStr = FormatHelper.FormatDuration(TimeSpan.FromHours(hoursUntil));
                builder.AddLabel($"<b>NEXT SERVICE:</b> <color=#90CAF9>{info.NextServiceTime}</color> ({etaStr} · {styleLabel})");
            }

            int balance = StateManager.Shared != null ? StateManager.Shared.Balance : 0;
            string dueColor = info.OnTrackTotalCost > 0 && balance < info.OnTrackTotalCost ? "#EF5350" : "#FFD54F";
            string costLine = info.OnTrackTotalCost > 0
                ? $"<b>COST DUE:</b> <color={dueColor}><b>{info.OnTrackTotalCost:C0}</b></color> ({info.OnTrackGoodsCost:C0} goods + {info.OnTrackHandlingCost:C0} handling · {info.OnTrackOrderCount} car{(info.OnTrackOrderCount == 1 ? "" : "s")})"
                : "<b>COST DUE:</b> <color=#B0BEC5>$0</color> (no load orders on track)";

            if (info.EnRouteTotalCost > 0)
            {
                costLine += $"  ·  <color=#4DD0E1>+{info.EnRouteTotalCost:C0} en-route ({info.EnRouteOrderCount})</color>";
            }

            builder.AddLabel(costLine);
        }

        private static void BuildCommercialHeader(UIPanelBuilder builder, FacilitySnapshot snapshot)
        {
            string statusBanner = StatusBadges.GetProductionStateBadge(snapshot.State);
            builder.AddLabel(statusBanner);

            List<string> inLines = new List<string>(4);
            List<string> outLines = new List<string>(4);

            foreach (BufferInfo comBuffer in snapshot.Buffers)
            {
                if (comBuffer is CommercialBuffer { DailyRate: > 0.001f } comm)
                {
                    string line = $"{FormatHelper.FormatDailyRate(comm.DailyRate, comm.Units)} {comm.CargoDisplayName}";
                    if (comm.IsInput)
                    {
                        inLines.Add(line);
                    }
                    else
                    {
                        outLines.Add(line);
                    }
                }
            }

            if (inLines.Count > 0 || outLines.Count > 0)
            {
                builder.AddLabel("<b>RECIPE:</b>");
                builder.HStack(recipeRow =>
                {
                    recipeRow.ConfigureLayout(12f);

                    recipeRow.VStack(inCol =>
                    {
                        foreach (string line in inLines)
                        {
                            inCol.AddLabel(line, t =>
                            {
                                t.alignment = TextAlignmentOptions.Right;
                                t.textWrappingMode = TextWrappingModes.NoWrap;
                            });
                        }
                    }).FlexibleWidth(1f);

                    recipeRow.VStack(arrowCol =>
                    {
                        arrowCol.AddLabel("──▶", t =>
                        {
                            t.alignment = TextAlignmentOptions.Center;
                            t.textWrappingMode = TextWrappingModes.NoWrap;
                        });
                    }).Width(40f);

                    recipeRow.VStack(outCol =>
                    {
                        foreach (string line in outLines)
                        {
                            outCol.AddLabel(line, t =>
                            {
                                t.alignment = TextAlignmentOptions.Left;
                                t.textWrappingMode = TextWrappingModes.NoWrap;
                            });
                        }
                    }).FlexibleWidth(1f);
                });
            }
            else if (!string.IsNullOrEmpty(snapshot.StatusMessage))
            {
                builder.AddLabel($"<b>THROUGHPUT:</b> {snapshot.StatusMessage}");
            }
        }
    }
}
