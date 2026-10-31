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
        private const int ColumnCount = 5;

        // Relative flexible width shares for the 5 columns: [Car, Status, Load/Cond, Rate/Parts, ETA]
        private static readonly float[] ColumnShares = new float[ColumnCount] { 2.0f, 1.5f, 1.4f, 1.4f, 1.0f };

        // Maximum initial text-fitted width caps (px) per column before flexible expansion
        private static readonly float[] ColumnMaxCaps = new float[ColumnCount] { 160f, 140f, 140f, 140f, 80f };

        // Scroll content right-edge padding (px):
        // - BaseScrollRightPadding: matches the visual gap on the left side of the window when no scrollbar is shown.
        // - ScrollbarInsetWidth: horizontal space occupied by the game's vertical scrollbar handle inside the content rect.
        // - ActiveScrollRightPadding: preserves the same BaseScrollRightPadding visual gap to the left of the scrollbar handle.
        private const int BaseScrollRightPadding = 2;
        private const int ScrollbarInsetWidth = 10;
        private const int ActiveScrollRightPadding = BaseScrollRightPadding + ScrollbarInsetWidth;
        private const float ScrollbarOverflowTolerance = 0.5f;
        private const float ScrollPositionRestoreEpsilon = 0.01f;

        // Table spacing constants (px)
        private const float ColumnSpacing = 4f;
        private const float RowSpacing = 3f;
        private const float SpurTitleBottomSpacing = 4f;
        private const float EmptyTrackBottomSpacing = 8f;
        private const float GroupBottomSpacing = 12f;
        private const int InitialRowCapacity = 32;

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
                    List<RectTransform[]> allRows = new List<RectTransform[]>(InitialRowCapacity);

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

                InitializeScrollContent(activeScrollRect, savedAnchoredY);
            });

            return activeScrollRect;
        }

        /// <summary>
        /// Updates the scroll content's right padding based on whether the content height overflows the viewport,
        /// keeping the table's right gap identical against either the window border or the scrollbar handle.
        /// </summary>
        public static void UpdateScrollPadding(ScrollRect? scrollRect, RectTransform? windowContentRoot)
        {
            if (scrollRect == null || scrollRect.content == null) return;

            if (windowContentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(windowContentRoot);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);

            VerticalLayoutGroup vlg = scrollRect.content.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) return;

            RectTransform viewRt = scrollRect.viewport != null
                ? scrollRect.viewport
                : (RectTransform)scrollRect.transform;
            bool scrollbarActive = scrollRect.content.rect.height > viewRt.rect.height + ScrollbarOverflowTolerance;

            int targetPadding = scrollbarActive ? ActiveScrollRightPadding : BaseScrollRightPadding;
            if (vlg.padding.right != targetPadding)
            {
                vlg.padding.right = targetPadding;
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            }
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
            scrollBuilder.AddLabel(spurTitle, t =>
            {
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            });
            scrollBuilder.Spacer(SpurTitleBottomSpacing);

            if (group.Cars.Count == 0)
            {
                scrollBuilder.AddLabel("<i>No cars spotted on this track</i>");
                scrollBuilder.Spacer(EmptyTrackBottomSpacing);
                return;
            }

            bool isRepairSpur = group.Role is CarGroupRole.LocomotiveRepair or CarGroupRole.EnRouteRepair;

            // Render Header
            RectTransform[] headerCells = new RectTransform[ColumnCount];
            scrollBuilder.HStack(h =>
            {
                h.ConfigureLayout(ColumnSpacing);
                string col3Title = isRepairSpur ? "<b>Cond</b>" : "<b>Load</b>";
                string col4Title = isRepairSpur ? "<b>Parts</b>" : "<b>Rate</b>";
                RenderHeaders(h, headerCells, col3Title, col4Title);
            });
            allHeaders.Add(headerCells);

            scrollBuilder.AddHRule();
            scrollBuilder.Spacer(RowSpacing);

            // Render Car Rows
            foreach (SpottedCarInfo car in group.Cars)
            {
                RectTransform[] rowCells = new RectTransform[ColumnCount];

                scrollBuilder.HStack(row =>
                {
                    row.ConfigureLayout(ColumnSpacing);
                    RenderRow(row, car, isRepairSpur, facilityType, rowCells);
                });

                allRows.Add(rowCells);
                scrollBuilder.Spacer(RowSpacing);
            }

            scrollBuilder.AddHRule();
            scrollBuilder.Spacer(GroupBottomSpacing);
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

        private static void InitializeScrollContent(ScrollRect activeScrollRect, float savedAnchoredY)
        {
            if (activeScrollRect.content == null) return;

            // VScrollView assigns a shared static RectOffset to every scroll view; clone it so per-window padding is isolated.
            VerticalLayoutGroup vlg = activeScrollRect.content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.padding = new RectOffset(vlg.padding.left, BaseScrollRightPadding, vlg.padding.top, vlg.padding.bottom);
            }

            if (Mathf.Abs(savedAnchoredY) > ScrollPositionRestoreEpsilon)
            {
                activeScrollRect.content.anchoredPosition = new Vector2(activeScrollRect.content.anchoredPosition.x, savedAnchoredY);
            }
        }
    }
}
