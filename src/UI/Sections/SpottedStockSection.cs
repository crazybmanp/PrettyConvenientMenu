using System.Collections.Generic;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets;
using TMPro;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class SpottedStockSection //TODO: Build is too long
    {
        public static ScrollRect? Build(
            UIPanelBuilder builder,
            IndustrySnapshot snapshot,
            float savedAnchoredY)
        {
            ScrollRect? activeScrollRect = null;

            builder.AddSection("Spotted Rolling Stock", b =>
            {
                int groupCount = snapshot.SpurGroups.Count;
                if (groupCount == 0)
                {
                    b.AddLabelEmptyState("No spotted cars detected at this facility.");
                    activeScrollRect = null;
                    return;
                }

                RectTransform scrollRt = b.VScrollView(scrollBuilder =>
                {
                    List<RectTransform[]> allHeaders = new List<RectTransform[]>(groupCount);
                    List<RectTransform[]> allRows = new List<RectTransform[]>(32);

                    for (int g = 0; g < groupCount; g++)
                    {
                        SpurCarGroup group = snapshot.SpurGroups[g];
                        (string direction, string headerColor) = StatusBadges.GetSpurGroupStyle(snapshot.FacilityType, group);

                        string spurTitle = group.IsEnRoute
                            ? $"<color={headerColor}><b>Arriving: {group.TrackName}</b></color>  ({direction})"
                            : $"<color={headerColor}><b>Spur: {group.TrackName}</b></color>  ({direction})";
                        scrollBuilder.AddLabel(spurTitle);
                        scrollBuilder.Spacer(4f);

                        if (group.Cars.Count == 0)
                        {
                            scrollBuilder.AddLabel("<i>No cars spotted on this track</i>");
                            scrollBuilder.Spacer(8f);
                            continue;
                        }

                        bool isRepairSpur = group.Role is SpurRole.LocomotiveRepair or SpurRole.EnRouteRepair;
                        RectTransform[] headerCells = new RectTransform[5];
                        scrollBuilder.HStack(h =>
                        {
                            h.ConfigureLayout(4f);
                            headerCells[0] = h.AddLabel("<b>Car</b>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
                            headerCells[1] = h.AddLabel("<b>Status</b>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
                            headerCells[2] = h.AddLabel(isRepairSpur ? "<b>Cond</b>" : "<b>Load</b>", t =>
                            {
                                t.alignment = TextAlignmentOptions.Center;
                                t.textWrappingMode = TextWrappingModes.NoWrap;
                            });
                            headerCells[3] = h.AddLabel(isRepairSpur ? "<b>Parts</b>" : "<b>Rate</b>", t =>
                            {
                                t.alignment = TextAlignmentOptions.Right;
                                t.textWrappingMode = TextWrappingModes.NoWrap;
                            });
                            headerCells[4] = h.AddLabel("<b>ETA</b>", t =>
                            {
                                t.alignment = TextAlignmentOptions.Right;
                                t.textWrappingMode = TextWrappingModes.NoWrap;
                            });
                        });
                        allHeaders.Add(headerCells);

                        scrollBuilder.AddHRule();
                        scrollBuilder.Spacer(3f);

                        foreach (SpottedCarInfo car in group.Cars)
                        {
                            RectTransform[] rowCells = new RectTransform[5];

                            SpottedCarInfo car1 = car;
                            scrollBuilder.HStack(row =>
                            {
                                row.ConfigureLayout(4f);
                                string? carId = car1.CarId ?? TrainController.Shared?.CarForString(car1.ReportingMark)?.id;
                                string carText = $"{car1.ReportingMark} ({car1.CarType})";
                                string carLabel = !string.IsNullOrEmpty(carId)
                                    ? $"<link=\"car:{carId}\"><style=ConsoleLink><noparse>{carText}</noparse></style></link>"
                                    : carText;

                                rowCells[0] = row.AddLabel(carLabel, t => t.textWrappingMode = TextWrappingModes.NoWrap);

                                string statusColor = StatusBadges.GetCarStatusColor(car1);
                                string statusText = !string.IsNullOrEmpty(car1.StatusDetail) ? car1.StatusDetail : car1.StatusState.ToString();
                                rowCells[1] = row.AddLabel($"<color={statusColor}>{statusText}</color>", t =>
                                {
                                    t.textWrappingMode = TextWrappingModes.NoWrap;
                                    t.overflowMode = TextOverflowModes.Ellipsis;
                                });

                                if (!string.IsNullOrEmpty(car1.CargoName))
                                {
                                    rowCells[1].Tooltip(car1.CargoName, string.Empty);
                                }

                                string col2Str;
                                string col3Str;
                                if (isRepairSpur)
                                {
                                    if (!string.IsNullOrEmpty(car1.CustomLoadString))
                                    {
                                        col2Str = car1.CustomLoadString!;
                                        col3Str = car1.StatusState == CarStatusState.Serviced
                                            ? "--"
                                            : FormatHelper.FormatWeight(car1.CurrentAmount);
                                    }
                                    else
                                    {
                                        col2Str = "--";
                                        col3Str = FormatHelper.FormatCarLoad(car1.CurrentAmount, car1.CurrentPercent, car1.Units);
                                    }
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(car1.CustomLoadString))
                                    {
                                        col2Str = car1.CustomLoadString!;
                                    }
                                    else if (snapshot.FacilityType == IndustryFacilityType.MaintenanceOfWay)
                                    {
                                        if (car1.CurrentAmount > 0.001f)
                                        {
                                            string shortAmount = FormatHelper.FormatQuantity(car1.CurrentAmount, car1.Units);
                                            col2Str = !string.IsNullOrEmpty(car1.CargoName) && car1.CargoName != "Cargo" && car1.CargoName != "Material"
                                                ? $"{shortAmount} {car1.CargoName}"
                                                : $"{shortAmount} ({car1.CurrentPercent:F0}%)";
                                        }
                                        else
                                        {
                                            col2Str = "Empty";
                                        }
                                    }
                                    else
                                    {
                                        col2Str = FormatHelper.FormatCarLoad(car1.CurrentAmount, car1.CurrentPercent, car1.Units);
                                    }

                                    col3Str = (car1.IsActiveTransfer && car1.TransferRatePerMinute > 0.0001f)
                                        ? FormatHelper.FormatTransferRate(car1.TransferRatePerMinute, car1.Units)
                                        : "--";
                                }

                                rowCells[2] = row.AddLabel(col2Str, t =>
                                {
                                    t.alignment = TextAlignmentOptions.Center;
                                    t.textWrappingMode = TextWrappingModes.NoWrap;
                                    t.overflowMode = TextOverflowModes.Ellipsis;
                                });

                                rowCells[3] = row.AddLabel(col3Str, t =>
                                {
                                    t.alignment = TextAlignmentOptions.Right;
                                    t.textWrappingMode = TextWrappingModes.NoWrap;
                                });

                                string etaStr = car1.EstimatedTimeToCompletion.HasValue
                                    ? FormatHelper.FormatDuration(car1.EstimatedTimeToCompletion)
                                    : "--";

                                rowCells[4] = row.AddLabel(etaStr, t =>
                                {
                                    t.alignment = TextAlignmentOptions.Right;
                                    t.textWrappingMode = TextWrappingModes.NoWrap;
                                });
                            });

                            allRows.Add(rowCells);
                            scrollBuilder.Spacer(3f);
                        }

                        scrollBuilder.AddHRule();
                        scrollBuilder.Spacer(12f);
                    }

                    if (allHeaders.Count > 0)
                    {
                        float[] colShares = new float[5] { 2.0f, 1.5f, 1.4f, 1.4f, 1.0f };
                        float[] colMaxCaps = new float[5] { 160f, 140f, 140f, 140f, 80f };
                        AutoSizedTable.ApplyColumnWidths(allHeaders, allRows, colShares, colMaxCaps);
                    }
                });

                if (scrollRt == null) return;

                activeScrollRect = scrollRt.GetComponent<ScrollRect>();
                if (activeScrollRect == null) return;

                float scrollbarWidth = 20f;
                if (activeScrollRect.verticalScrollbar != null)
                {
                    RectTransform? sbarRt = activeScrollRect.verticalScrollbar.transform as RectTransform;
                    if (sbarRt != null && sbarRt.rect.width > 0f)
                    {
                        scrollbarWidth = sbarRt.rect.width;
                    }
                }

                bool isScrollable = snapshot.SpurGroups.Count > 1 || snapshot.TotalCarCount >= 3;
                if (activeScrollRect.content == null) return;

                VerticalLayoutGroup vlg = activeScrollRect.content.GetComponent<VerticalLayoutGroup>();
                if (vlg != null)
                {
                    vlg.padding.right = isScrollable ? (int)Mathf.Ceil(scrollbarWidth + 4f) : 6;
                }

                if (Mathf.Abs(savedAnchoredY) > 0.01f)
                {
                    activeScrollRect.content.anchoredPosition = new Vector2(activeScrollRect.content.anchoredPosition.x, savedAnchoredY);
                }
            });

            return activeScrollRect;
        }
    }
}
