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
    /// <summary>
    /// Renders the Rolling Stock section of the facility monitor.
    /// Dispatches rendering between specialized Repair Track and Transfer Track tables.
    /// </summary>
    internal static class RollingStockSection
    {
        private static readonly float[] ColumnShares = new float[5] { 2.0f, 1.5f, 1.4f, 1.4f, 1.0f };
        private static readonly float[] ColumnMaxCaps = new float[5] { 160f, 140f, 140f, 140f, 80f };

        public static ScrollRect? Build(
            UIPanelBuilder builder,
            FacilitySnapshot snapshot,
            float savedAnchoredY)
        {
            ScrollRect? activeScrollRect = null;

            builder.AddSection("Rolling Stock", b =>
            {
                int groupCount = snapshot.CarGroups.Count;
                if (groupCount == 0)
                {
                    b.AddLabelEmptyState("No rolling stock detected at this facility.");
                    activeScrollRect = null;
                    return;
                }

                RectTransform scrollRt = b.VScrollView(scrollBuilder =>
                {
                    List<RectTransform[]> allHeaders = new List<RectTransform[]>(groupCount);
                    List<RectTransform[]> allRows = new List<RectTransform[]>(32);

                    for (int g = 0; g < groupCount; g++)
                    {
                        RenderCarGroup(scrollBuilder, snapshot.CarGroups[g], snapshot.FacilityType, allHeaders, allRows);
                    }

                    if (allHeaders.Count > 0)
                    {
                        AutoSizedTable.ApplyColumnWidths(allHeaders, allRows, ColumnShares, ColumnMaxCaps);
                    }
                });

                if (scrollRt == null) return;

                activeScrollRect = scrollRt.GetComponent<ScrollRect>();
                if (activeScrollRect == null) return;

                ConfigureScrollPaddingAndPosition(activeScrollRect, snapshot, savedAnchoredY);
            });

            return activeScrollRect;
        }

        private static void RenderCarGroup(
            UIPanelBuilder scrollBuilder,
            FacilityCarGroup group,
            FacilityType facilityType,
            List<RectTransform[]> allHeaders,
            List<RectTransform[]> allRows)
        {
            (string direction, string headerColor) = StatusBadges.GetCarGroupStyle(facilityType, group);

            string spurTitle = group.IsEnRoute
                ? $"<color={headerColor}><b>Arriving: {group.TrackName}</b></color>  ({direction})"
                : $"<color={headerColor}><b>Spur: {group.TrackName}</b></color>  ({direction})";
            scrollBuilder.AddLabel(spurTitle);
            scrollBuilder.Spacer(4f);

            if (group.Cars.Count == 0)
            {
                scrollBuilder.AddLabel("<i>No cars spotted on this track</i>");
                scrollBuilder.Spacer(8f);
                return;
            }

            bool isRepairSpur = group.Role is CarGroupRole.LocomotiveRepair or CarGroupRole.EnRouteRepair;

            // Render Header
            RectTransform[] headerCells = new RectTransform[5];
            scrollBuilder.HStack(h =>
            {
                h.ConfigureLayout(4f);
                string col3Title = isRepairSpur ? "<b>Cond</b>" : "<b>Load</b>";
                string col4Title = isRepairSpur ? "<b>Parts</b>" : "<b>Rate</b>";
                RenderHeaders(h, headerCells, col3Title, col4Title);
            });
            allHeaders.Add(headerCells);

            scrollBuilder.AddHRule();
            scrollBuilder.Spacer(3f);

            // Render Car Rows
            foreach (SpottedCarInfo car in group.Cars)
            {
                RectTransform[] rowCells = new RectTransform[5];

                scrollBuilder.HStack(row =>
                {
                    row.ConfigureLayout(4f);
                    RenderRow(row, car, isRepairSpur, facilityType, rowCells);
                });

                allRows.Add(rowCells);
                scrollBuilder.Spacer(3f);
            }

            scrollBuilder.AddHRule();
            scrollBuilder.Spacer(12f);
        }

        private static void RenderHeaders(UIPanelBuilder row, RectTransform[] cells, string col3Title, string col4Title)
        {
            cells[0] = row.AddLabel("<b>Car</b>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
            cells[1] = row.AddLabel("<b>Status</b>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
            cells[2] = row.AddLabel(col3Title, t =>
            {
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            });
            cells[3] = row.AddLabel(col4Title, t =>
            {
                t.alignment = TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            });
            cells[4] = row.AddLabel("<b>ETA</b>", t =>
            {
                t.alignment = TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            });
        }

        private static void RenderRow(
            UIPanelBuilder row,
            SpottedCarInfo car,
            bool isRepairSpur,
            FacilityType facilityType,
            RectTransform[] rowCells)
        {
            // Col 1: Car Link / Identifier
            string carLabel = SpurCellFormatter.BuildCarLinkLabel(car);
            rowCells[0] = row.AddLabel(carLabel, t => t.textWrappingMode = TextWrappingModes.NoWrap);

            // Col 2: Status Badge
            (string statusText, string statusColor) = SpurCellFormatter.GetStatusDisplay(car);
            rowCells[1] = row.AddLabel($"<color={statusColor}>{statusText}</color>", t =>
            {
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            });
            if (!string.IsNullOrEmpty(car.CargoName))
            {
                rowCells[1].Tooltip(car.CargoName, string.Empty);
            }

            // Col 3 & 4: (Cond, Parts) or (Load, Rate)
            (string col3Str, string col4Str) = isRepairSpur
                ? SpurCellFormatter.FormatRepairCells(car)
                : SpurCellFormatter.FormatTransferCells(car, facilityType);

            rowCells[2] = row.AddLabel(col3Str, t =>
            {
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            });

            rowCells[3] = row.AddLabel(col4Str, t =>
            {
                t.alignment = TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            });

            // Col 5: ETA
            string etaStr = SpurCellFormatter.FormatEta(car);
            rowCells[4] = row.AddLabel(etaStr, t =>
            {
                t.alignment = TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            });
        }

        private static void ConfigureScrollPaddingAndPosition(
            ScrollRect activeScrollRect,
            FacilitySnapshot snapshot,
            float savedAnchoredY)
        {
            float scrollbarWidth = 20f;
            if (activeScrollRect.verticalScrollbar != null)
            {
                RectTransform? sbarRt = activeScrollRect.verticalScrollbar.transform as RectTransform;
                if (sbarRt != null && sbarRt.rect.width > 0f)
                {
                    scrollbarWidth = sbarRt.rect.width;
                }
            }

            bool isScrollable = snapshot.CarGroups.Count > 1 || snapshot.TotalCarCount >= 3;
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
        }
    }
}
