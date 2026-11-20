using System.Collections.Generic;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets;
using TMPro;
using UI.Builder;
using UnityEngine;

namespace PrettyConvenientMenu.UI.Sections
{
    /// <summary>
    /// Renders the Rolling Stock section of the facility monitor.
    /// Dispatches rendering between specialized Repair Track and Transfer Track tables.
    /// </summary>
    internal static class RollingStockSection
    {
        private const int ColumnCount = 5;

        // Relative flexible width shares for 5-column layout: [Car, Status, Load/Cond, Rate/Parts, ETA]
        private static readonly float[] ColumnShares = new float[ColumnCount] { 2.0f, 1.5f, 1.4f, 1.4f, 1.0f };

        // Maximum initial text-fitted width caps (px) per column before flexible expansion
        private static readonly float[] ColumnMaxCaps = new float[ColumnCount] { 160f, 140f, 140f, 140f, 80f };

        // 3-column Interchange layout (no Rate, no ETA): [Car, Status, Load]
        private const int Interchange3ColCount = 3;
        private static readonly float[] Interchange3ColShares = new float[Interchange3ColCount] { 1.6f, 2.2f, 1.8f };
        private static readonly float[] Interchange3ColMaxCaps = new float[Interchange3ColCount] { 130f, 160f, 140f };

        // 4-column Interchange Bardo layout (no Rate, includes ETA): [Car, Status, Load, ETA]
        private const int Interchange4ColCount = 4;
        private static readonly float[] Interchange4ColShares = new float[Interchange4ColCount] { 1.6f, 2.2f, 1.6f, 0.9f };
        private static readonly float[] Interchange4ColMaxCaps = new float[Interchange4ColCount] { 115f, 125f, 115f, 65f };

        // Table spacing constants (px)
        private const float ColumnSpacing = 4f;
        private const float RowSpacing = 3f;
        private const float SpurTitleBottomSpacing = 4f;
        private const float EmptyTrackBottomSpacing = 8f;
        private const float GroupBottomSpacing = 12f;
        private const int InitialRowCapacity = 32;

        /// <summary>
        /// Builds the rolling stock section for the facility monitor window.
        /// </summary>
        public static void Build(
            UIPanelBuilder builder,
            FacilitySnapshot snapshot)
        {
            builder.AddSection("Rolling Stock", b =>
            {
                int groupCount = snapshot.CarGroups.Count;
                if (groupCount == 0)
                {
                    b.AddLabelEmptyState("No rolling stock detected at this facility.");
                    return;
                }

                List<RectTransform[]> allHeaders = new List<RectTransform[]>(groupCount);
                List<RectTransform[]> allRows = new List<RectTransform[]>(InitialRowCapacity);

                for (int g = 0; g < groupCount; g++)
                {
                    RenderCarGroup(b, snapshot.CarGroups[g], snapshot.FacilityType, allHeaders, allRows);
                }

                if (allHeaders.Count > 0)
                {
                    if (snapshot.FacilityType == FacilityType.Interchange)
                    {
                        List<RectTransform[]> h3 = new List<RectTransform[]>();
                        List<RectTransform[]> r3 = new List<RectTransform[]>();
                        List<RectTransform[]> h4 = new List<RectTransform[]>();
                        List<RectTransform[]> r4 = new List<RectTransform[]>();

                        foreach (RectTransform[] h in allHeaders)
                        {
                            (h.Length == Interchange3ColCount ? h3 : h4).Add(h);
                        }
                        foreach (RectTransform[] r in allRows)
                        {
                            (r.Length == Interchange3ColCount ? r3 : r4).Add(r);
                        }

                        if (h3.Count > 0) AutoSizedTable.ApplyColumnWidths(h3, r3, Interchange3ColShares, Interchange3ColMaxCaps);
                        if (h4.Count > 0) AutoSizedTable.ApplyColumnWidths(h4, r4, Interchange4ColShares, Interchange4ColMaxCaps);
                    }
                    else
                    {
                        AutoSizedTable.ApplyColumnWidths(allHeaders, allRows, ColumnShares, ColumnMaxCaps);
                    }
                }
            });
        }

        /// <summary>
        /// Renders a single spur or en-route car group, prefixing matching landing swatches if assigned.
        /// </summary>
        private static void RenderCarGroup(
            UIPanelBuilder scrollBuilder,
            FacilityCarGroup group,
            FacilityType facilityType,
            List<RectTransform[]> allHeaders,
            List<RectTransform[]> allRows)
        {
            (string direction, string headerColor) = StatusBadges.GetCarGroupStyle(facilityType, group);

            string swatchPrefix = !string.IsNullOrEmpty(group.SwatchHex)
                ? $"<color={group.SwatchHex}>■</color> "
                : string.Empty;

            string spurTitle = group.Role == CarGroupRole.InterchangeBardo
                ? $"{swatchPrefix}<color={headerColor}><b>{group.TrackName}</b></color>  ({direction})"
                : (group.IsEnRoute
                    ? $"{swatchPrefix}<color={headerColor}><b>Arriving: {group.TrackName}</b></color>  ({direction})"
                    : $"{swatchPrefix}<color={headerColor}><b>Spur: {group.TrackName}</b></color>  ({direction})");
            RectTransform labelRect = scrollBuilder.AddLabel(spurTitle, t =>
            {
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            });

            if (!group.IsEnRoute && group.Role != CarGroupRole.InterchangeBardo)
            {
                TrackHoverHelper.Attach(labelRect, group.TrackSpanIds);
            }
            scrollBuilder.Spacer(SpurTitleBottomSpacing);

            if (group.Cars.Count == 0)
            {
                scrollBuilder.AddLabel("<i>No cars spotted on this track</i>");
                scrollBuilder.Spacer(EmptyTrackBottomSpacing);
                return;
            }

            bool isRepairSpur = group.Role is CarGroupRole.LocomotiveRepair or CarGroupRole.EnRouteRepair;
            bool includeRateColumn = facilityType != FacilityType.Interchange;
            bool includeEtaColumn = facilityType != FacilityType.Interchange || group.Role == CarGroupRole.InterchangeBardo;
            int colCount = 3 + (includeRateColumn ? 1 : 0) + (includeEtaColumn ? 1 : 0);

            // Render Header
            RectTransform[] headerCells = new RectTransform[colCount];
            scrollBuilder.HStack(h =>
            {
                h.ConfigureLayout(ColumnSpacing);
                string col3Title = isRepairSpur ? "<b>Cond</b>" : "<b>Load</b>";
                string col4Title = isRepairSpur ? "<b>Parts</b>" : "<b>Rate</b>";
                RenderHeaders(h, headerCells, col3Title, col4Title, includeRateColumn, includeEtaColumn);
            });
            allHeaders.Add(headerCells);

            scrollBuilder.AddHRule();
            scrollBuilder.Spacer(RowSpacing);

            // Render Car Rows
            foreach (SpottedCarInfo car in group.Cars)
            {
                RectTransform[] rowCells = new RectTransform[colCount];

                scrollBuilder.HStack(row =>
                {
                    row.ConfigureLayout(ColumnSpacing);
                    RenderRow(row, car, isRepairSpur, facilityType, includeRateColumn, includeEtaColumn, rowCells);
                });

                allRows.Add(rowCells);
                scrollBuilder.Spacer(RowSpacing);
            }

            scrollBuilder.AddHRule();
            scrollBuilder.Spacer(GroupBottomSpacing);
        }

        private static void RenderHeaders(UIPanelBuilder row, RectTransform[] cells, string col3Title, string col4Title, bool includeRateColumn, bool includeEtaColumn)
        {
            cells[0] = row.AddLabel("<b>Car</b>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
            cells[1] = row.AddLabel("<b>Status</b>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
            cells[2] = row.AddLabel(col3Title, t =>
            {
                t.alignment = includeEtaColumn ? TextAlignmentOptions.Center : TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            });

            int idx = 3;
            if (includeRateColumn)
            {
                cells[idx++] = row.AddLabel(col4Title, t =>
                {
                    t.alignment = TextAlignmentOptions.Right;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                });
            }
            if (includeEtaColumn)
            {
                cells[idx] = row.AddLabel("<b>ETA</b>", t =>
                {
                    t.alignment = TextAlignmentOptions.Right;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                });
            }
        }

        private static void RenderRow(
            UIPanelBuilder row,
            SpottedCarInfo car,
            bool isRepairSpur,
            FacilityType facilityType,
            bool includeRateColumn,
            bool includeEtaColumn,
            RectTransform[] rowCells)
        {
            // Col 1: Car Link / Identifier
            string carLabel = SpurCellFormatter.BuildCarLinkLabel(car);
            rowCells[0] = row.AddLabel(carLabel, t =>
            {
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            });

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
                t.alignment = includeEtaColumn ? TextAlignmentOptions.Center : TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
            });

            int idx = 3;
            if (includeRateColumn)
            {
                rowCells[idx++] = row.AddLabel(col4Str, t =>
                {
                    t.alignment = TextAlignmentOptions.Right;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                });
            }

            if (includeEtaColumn)
            {
                string etaStr = SpurCellFormatter.FormatEta(car);
                rowCells[idx] = row.AddLabel(etaStr, t =>
                {
                    t.alignment = TextAlignmentOptions.Right;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                });
            }
        }
    }
}
