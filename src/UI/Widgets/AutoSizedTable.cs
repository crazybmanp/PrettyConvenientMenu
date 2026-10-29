using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets
{
    internal static class AutoSizedTable
    {
        public static void ApplyColumnWidths(
            List<RectTransform[]>? headers,
            List<RectTransform[]>? rows,
            float[]? columnShares,
            float[]? columnMaxCaps)
        {
            if (headers == null || headers.Count == 0 || columnShares == null)
            {
                return;
            }

            int colCount = columnShares.Length;
            float[] colMinWidths = new float[colCount];

            for (int col = 0; col < colCount; col++)
            {
                float maxW = 0f;
                foreach (RectTransform[] headerRow in headers)
                {
                    if (col >= headerRow.Length) continue;
                    TMP_Text headerText = headerRow[col].GetComponent<TMP_Text>();
                    if (headerText != null)
                    {
                        maxW = Mathf.Max(maxW, headerText.preferredWidth);
                    }
                }

                if (rows != null)
                {
                    foreach (RectTransform[] row in rows)
                    {
                        if (col >= row.Length) continue;
                        TMP_Text cellText = row[col].GetComponent<TMP_Text>();
                        if (cellText != null)
                        {
                            maxW = Mathf.Max(maxW, cellText.preferredWidth);
                        }
                    }
                }

                float maxCap = (columnMaxCaps != null && col < columnMaxCaps.Length) ? columnMaxCaps[col] : 150f;
                colMinWidths[col] = Mathf.Min(Mathf.Ceil(maxW) + 6f, maxCap);
            }

            for (int col = 0; col < colCount; col++)
            {
                foreach (RectTransform[] headerRow in headers)
                {
                    if (col < headerRow.Length)
                    {
                        ApplyColumn(headerRow[col], colMinWidths[col], columnShares[col]);
                    }
                }

                if (rows == null) continue;
                foreach (RectTransform[] row in rows)
                {
                    if (col < row.Length)
                    {
                        ApplyColumn(row[col], colMinWidths[col], columnShares[col]);
                    }
                }
            }
        }

        public static void ApplyColumn(RectTransform rt, float minWidth, float flexibleShare)
        {
            if (rt == null)
            {
                return;
            }

            LayoutElement le = rt.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = rt.gameObject.AddComponent<LayoutElement>();
            }

            le.minWidth = minWidth;
            le.preferredWidth = minWidth;
            le.flexibleWidth = flexibleShare;
        }
    }
}
