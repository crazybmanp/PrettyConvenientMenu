using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets
{
    /// <summary>
    /// Optional tag placed on children of <see cref="FlowLayoutGroup"/> to keep coupled strings
    /// together when possible and toggle continuation markers when a string wraps across rows.
    /// </summary>
    public sealed class FlowGroupMember : MonoBehaviour
    {
        public int GroupId { get; set; } = -1;
        public GameObject? LeftContinuationMarker { get; set; }
        public GameObject? RightContinuationMarker { get; set; }
    }

    /// <summary>
    /// Self-adapting Unity <see cref="LayoutGroup"/> that wraps children across rows based on
    /// the live container width, eliminating hardcoded pixel-width or per-row item guesses.
    /// </summary>
    public sealed class FlowLayoutGroup : LayoutGroup
    {
        public float SpacingX { get; set; } = 2f;
        public float SpacingY { get; set; } = 2f;
        public float GroupGapX { get; set; } = 12f;
        public bool KeepGroupsTogether { get; set; } = true;

        private readonly List<Vector2> computedPositions = new List<Vector2>(32);
        private readonly List<Vector2> computedSizes = new List<Vector2>(32);
        private readonly List<int> computedRows = new List<int>(32);
        private float lastComputedHeight;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();

            float maxChildWidth = 0f;

            for (int i = 0; i < rectChildren.Count; i++)
            {
                RectTransform child = rectChildren[i];
                float w = Mathf.Max(LayoutUtility.GetMinWidth(child), LayoutUtility.GetPreferredWidth(child));
                maxChildWidth = Mathf.Max(maxChildWidth, w);
            }

            float minW = padding.horizontal + maxChildWidth;
            // Report minW as preferredWidth too (with flexibleWidth = 1) so FlowLayoutGroup
            // fills its assigned column width and wraps instead of inflating parent column widths.
            SetLayoutInputForAxis(minW, minW, 1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            float availableWidth = rectTransform.rect.width - padding.horizontal;
            if (availableWidth <= 1f)
            {
                availableWidth = Mathf.Max(100f, GetTotalPreferredSize(0) - padding.horizontal);
            }

            ComputeLayout(availableWidth);
            SetLayoutInputForAxis(lastComputedHeight, lastComputedHeight, 0f, 1);
        }

        public override void SetLayoutHorizontal()
        {
            float availableWidth = rectTransform.rect.width - padding.horizontal;
            if (availableWidth <= 1f)
            {
                availableWidth = Mathf.Max(100f, GetTotalPreferredSize(0) - padding.horizontal);
            }

            ComputeLayout(availableWidth);

            for (int i = 0; i < rectChildren.Count && i < computedPositions.Count; i++)
            {
                SetChildAlongAxis(rectChildren[i], 0, computedPositions[i].x, computedSizes[i].x);
            }

            UpdateContinuationMarkers();
        }

        public override void SetLayoutVertical()
        {
            for (int i = 0; i < rectChildren.Count && i < computedPositions.Count; i++)
            {
                SetChildAlongAxis(rectChildren[i], 1, computedPositions[i].y, computedSizes[i].y);
            }
        }

        private void ComputeLayout(float availableWidth)
        {
            computedPositions.Clear();
            computedSizes.Clear();
            computedRows.Clear();

            float x = 0f;
            float y = padding.top;
            float rowHeight = 0f;
            int rowIndex = 0;
            int prevGroup = -1;

            for (int i = 0; i < rectChildren.Count; i++)
            {
                RectTransform child = rectChildren[i];
                float w = Mathf.Max(LayoutUtility.GetMinWidth(child), LayoutUtility.GetPreferredWidth(child));
                float h = Mathf.Max(LayoutUtility.GetMinHeight(child), LayoutUtility.GetPreferredHeight(child));
                int group = GetGroupId(child);

                bool isNewGroup = KeepGroupsTogether && group >= 0 && prevGroup >= 0 && group != prevGroup;
                float gap = x > 0.001f ? (isNewGroup ? GroupGapX : SpacingX) : 0f;

                bool wrapForGroup = false;
                if (x > 0.001f && isNewGroup)
                {
                    float groupWidth = MeasureContiguousGroupWidth(i, group);
                    if (x + gap + groupWidth > availableWidth + 0.5f && groupWidth <= availableWidth + 0.5f)
                    {
                        wrapForGroup = true;
                    }
                }

                if (wrapForGroup || (x > 0.001f && x + gap + w > availableWidth + 0.5f))
                {
                    x = 0f;
                    y += rowHeight + SpacingY;
                    rowHeight = 0f;
                    rowIndex++;
                    gap = 0f;
                }

                computedPositions.Add(new Vector2(padding.left + x + gap, y));
                computedSizes.Add(new Vector2(w, h));
                computedRows.Add(rowIndex);

                x += gap + w;
                rowHeight = Mathf.Max(rowHeight, h);
                prevGroup = group;
            }

            lastComputedHeight = rectChildren.Count > 0
                ? y + rowHeight + padding.bottom
                : padding.vertical;
        }

        private float MeasureContiguousGroupWidth(int startIndex, int groupId)
        {
            float total = 0f;
            for (int i = startIndex; i < rectChildren.Count; i++)
            {
                if (GetGroupId(rectChildren[i]) != groupId)
                {
                    break;
                }

                if (i > startIndex)
                {
                    total += SpacingX;
                }

                RectTransform child = rectChildren[i];
                total += Mathf.Max(LayoutUtility.GetMinWidth(child), LayoutUtility.GetPreferredWidth(child));
            }
            return total;
        }

        private void UpdateContinuationMarkers()
        {
            for (int i = 0; i < rectChildren.Count && i < computedRows.Count; i++)
            {
                FlowGroupMember? member = rectChildren[i].GetComponent<FlowGroupMember>();
                if (member == null) continue;

                int g = member.GroupId;
                int row = computedRows[i];

                bool splitFromPrev = g >= 0 && i > 0 && GetGroupId(rectChildren[i - 1]) == g && computedRows[i - 1] != row;
                bool splitToNext = g >= 0 && i + 1 < rectChildren.Count && GetGroupId(rectChildren[i + 1]) == g && computedRows[i + 1] != row;

                if (member.LeftContinuationMarker != null && member.LeftContinuationMarker.activeSelf != splitFromPrev)
                {
                    member.LeftContinuationMarker.SetActive(splitFromPrev);
                }
                if (member.RightContinuationMarker != null && member.RightContinuationMarker.activeSelf != splitToNext)
                {
                    member.RightContinuationMarker.SetActive(splitToNext);
                }
            }
        }

        private static int GetGroupId(RectTransform child)
        {
            FlowGroupMember? member = child.GetComponent<FlowGroupMember>();
            return member != null ? member.GroupId : -1;
        }
    }
}
