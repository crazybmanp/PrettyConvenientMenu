using System.Collections.Generic;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.UI.Common;
using TMPro;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class FacilityHeaderSection
    {
        public static void Build(UIPanelBuilder builder, IndustrySnapshot snapshot)
        {
            if (snapshot.FacilityType == IndustryFacilityType.CommercialFreight)
            {
                BuildCommercialHeader(builder, snapshot);
                builder.Spacer(6f);
            }
        }

        private static void BuildCommercialHeader(UIPanelBuilder builder, IndustrySnapshot snapshot)
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
