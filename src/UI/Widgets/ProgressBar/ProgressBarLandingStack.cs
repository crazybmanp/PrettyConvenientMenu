using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets.ProgressBar
{
    /// <summary>
    /// Renders color-coded landing segments across the lower portion of a multi-landing commodity progress bar.
    /// Centered bold text labels are displayed inside each slice, gracefully hiding text when slices are too narrow or empty.
    /// </summary>
    internal static class ProgressBarLandingStack
    {
        private const float MinWidthForText = 12f;

        /// <summary>
        /// Applies or updates the landing stack decorator on the progress bar.
        /// </summary>
        /// <param name="barRt">Root progress bar RectTransform.</param>
        /// <param name="segments">List of landing segments containing ratio of total capacity, color, and label.</param>
        /// <param name="hasLandingStack">Whether the landing stack should be visible.</param>
        /// <param name="bottomOffset">Vertical pixel offset from bottom (reserved for caliper space if active).</param>
        public static void Apply(
            RectTransform barRt,
            IReadOnlyList<(float Ratio, Color Color, string Label)>? segments,
            bool hasLandingStack,
            float bottomOffset = 0f)
        {
            Transform trackTransform = barRt.Find("LandingStackTrack");
            if (!hasLandingStack || segments == null || segments.Count == 0)
            {
                if (trackTransform != null)
                {
                    trackTransform.gameObject.SetActive(false);
                }
                return;
            }

            GameObject trackObj = trackTransform == null
                ? new GameObject("LandingStackTrack", typeof(RectTransform))
                : trackTransform.gameObject;

            if (trackTransform == null)
            {
                trackObj.transform.SetParent(barRt, false);
            }
            trackObj.SetActive(true);

            RectTransform trackRt = trackObj.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0f);
            trackRt.anchorMax = new Vector2(1f, 0.46f);
            trackRt.offsetMin = new Vector2(0f, bottomOffset);
            trackRt.offsetMax = Vector2.zero;

            // Hide previous children on rebuild
            for (int c = 0; c < trackRt.childCount; c++)
            {
                trackRt.GetChild(c).gameObject.SetActive(false);
            }

            float barPixelWidth = barRt.rect.width > 10f ? barRt.rect.width : 500f;
            float cursor = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                (float ratio, Color color, string label) = segments[i];

                if (ratio <= 0.0005f)
                {
                    // Zero-stock landing: render a small colored vertical tick line at current cursor
                    string tickName = $"LandingZeroTick_{i}";
                    Transform tickTransform = trackRt.Find(tickName);
                    GameObject tickObj = tickTransform == null
                        ? new GameObject(tickName, typeof(RectTransform), typeof(Image))
                        : tickTransform.gameObject;

                    if (tickTransform == null)
                    {
                        tickObj.transform.SetParent(trackRt, false);
                    }
                    tickObj.SetActive(true);

                    Image tickImg = tickObj.GetComponent<Image>();
                    tickImg.color = color;
                    tickImg.sprite = null;

                    RectTransform tickRt = tickObj.GetComponent<RectTransform>();
                    tickRt.anchorMin = new Vector2(cursor, 0f);
                    tickRt.anchorMax = new Vector2(cursor, 1f);
                    tickRt.offsetMin = new Vector2(-1f, 0f);
                    tickRt.offsetMax = new Vector2(1f, 0f);
                    continue;
                }

                float from = cursor;
                float to = Mathf.Clamp01(cursor + ratio);
                cursor = to;

                string sliceName = $"LandingSlice_{i}";
                Transform sliceTransform = trackRt.Find(sliceName);
                GameObject sliceObj;
                if (sliceTransform == null)
                {
                    sliceObj = new GameObject(sliceName, typeof(RectTransform), typeof(Image));
                    sliceObj.transform.SetParent(trackRt, false);
                }
                else
                {
                    sliceObj = sliceTransform.gameObject;
                }

                sliceObj.SetActive(true);
                Image sliceImg = sliceObj.GetComponent<Image>();
                sliceImg.color = color;
                sliceImg.sprite = null;

                RectTransform sliceRt = sliceObj.GetComponent<RectTransform>();
                sliceRt.anchorMin = new Vector2(from, 0f);
                sliceRt.anchorMax = new Vector2(to, 1f);
                sliceRt.offsetMin = Vector2.zero;
                sliceRt.offsetMax = Vector2.zero;

                // Check pixel width of slice
                float slicePixelWidth = (to - from) * barPixelWidth;
                string textName = $"Label_{i}";
                Transform textTransform = sliceObj.transform.Find(textName);

                if (slicePixelWidth >= MinWidthForText && !string.IsNullOrEmpty(label))
                {
                    GameObject textObj = textTransform == null
                        ? new GameObject(textName, typeof(RectTransform), typeof(TextMeshProUGUI))
                        : textTransform.gameObject;

                    if (textTransform == null)
                    {
                        textObj.transform.SetParent(sliceObj.transform, false);
                    }
                    textObj.SetActive(true);

                    TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
                    tmp.text = $"<b>{label}</b>";
                    tmp.fontSize = 9f;
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 6.5f;
                    tmp.fontSizeMax = 9f;
                    tmp.color = Color.white;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.overflowMode = TextOverflowModes.Truncate;
                    tmp.margin = Vector4.zero;

                    RectTransform textRt = textObj.GetComponent<RectTransform>();
                    textRt.anchorMin = Vector2.zero;
                    textRt.anchorMax = Vector2.one;
                    textRt.offsetMin = Vector2.zero;
                    textRt.offsetMax = Vector2.zero;
                }
                else if (textTransform != null)
                {
                    textTransform.gameObject.SetActive(false);
                }
            }
        }
    }
}
