using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets.ProgressBar
{
    /// <summary>
    /// Represents a single landing sub-buffer segment within the lower landing stack.
    /// </summary>
    public readonly struct LandingSegment
    {
        public readonly float Ratio;
        public readonly float OutboundRatio;
        public readonly float InboundRatio;
        public readonly Color Color;
        public readonly string Label;

        public LandingSegment(float ratio, float outboundRatio, float inboundRatio, Color color, string label)
        {
            Ratio = ratio;
            OutboundRatio = outboundRatio;
            InboundRatio = inboundRatio;
            Color = color;
            Label = label;
        }
    }

    /// <summary>
    /// Renders color-coded landing segments across the lower portion of a multi-landing commodity progress bar.
    /// Supports active transfer stripe shading and centered bold text labels.
    /// </summary>
    internal static class ProgressBarLandingStack
    {
        private const float MinWidthForText = 12f;

        /// <summary>
        /// Derives a high-contrast two-tone stripe color directly from the landing's own swatch hue.
        /// Guarantees the stripe never clashes with any palette color regardless of swatch choice.
        /// </summary>
        public static Color GetStripeShade(Color baseColor)
        {
            float luminance = (0.299f * baseColor.r) + (0.587f * baseColor.g) + (0.114f * baseColor.b);
            return luminance > 0.40f
                ? Color.Lerp(baseColor, Color.black, 0.55f)
                : Color.Lerp(baseColor, Color.white, 0.55f);
        }

        /// <summary>
        /// Applies or updates the landing stack decorator on the progress bar.
        /// </summary>
        /// <param name="barRt">Root progress bar RectTransform.</param>
        /// <param name="segments">List of landing segments containing fill ratios, active transfer ratios, color, and label.</param>
        /// <param name="hasLandingStack">Whether the landing stack should be visible.</param>
        /// <param name="bottomOffset">Vertical pixel offset from bottom (reserved for caliper space if active).</param>
        public static void Apply(
            RectTransform barRt,
            IReadOnlyList<LandingSegment>? segments,
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
            trackRt.anchorMax = new Vector2(1f, 0.50f);
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
                LandingSegment seg = segments[i];
                float ratio = seg.Ratio;
                float outRatio = seg.OutboundRatio;
                Color color = seg.Color;
                string label = seg.Label;

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

                // Active loading stripe overlay (carves backward from the right of this landing slice)
                string stripeName = $"Stripe_{i}";
                Transform stripeTransform = sliceObj.transform.Find(stripeName);
                if (outRatio > 0.0005f && (to - from) > 0.0005f)
                {
                    GameObject stripeObj;
                    if (stripeTransform == null)
                    {
                        stripeObj = new GameObject(stripeName, typeof(RectTransform), typeof(TiledStripeImage));
                        stripeObj.transform.SetParent(sliceObj.transform, false);
                    }
                    else
                    {
                        stripeObj = stripeTransform.gameObject;
                        if (stripeObj.GetComponent<TiledStripeImage>() == null)
                        {
                            Image oldImg = stripeObj.GetComponent<Image>();
                            if (oldImg != null)
                            {
                                UnityEngine.Object.DestroyImmediate(oldImg);
                            }
                            stripeObj.AddComponent<TiledStripeImage>();
                        }
                    }
                    stripeObj.SetActive(true);

                    TiledStripeImage stripeImg = stripeObj.GetComponent<TiledStripeImage>();
                    stripeImg.texture = TiledStripeImage.GetOrCreateStripedTexture();
                    stripeImg.material = null;
                    stripeImg.color = GetStripeShade(color);

                    float stripeFraction = Mathf.Clamp01(outRatio / (to - from));
                    RectTransform stripeRt = stripeObj.GetComponent<RectTransform>();
                    stripeRt.anchorMin = new Vector2(1f - stripeFraction, 0f);
                    stripeRt.anchorMax = new Vector2(1f, 1f);
                    stripeRt.offsetMin = Vector2.zero;
                    stripeRt.offsetMax = Vector2.zero;
                }
                else if (stripeTransform != null)
                {
                    stripeTransform.gameObject.SetActive(false);
                }

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

                    textObj.transform.SetAsLastSibling();
                }
                else if (textTransform != null)
                {
                    textTransform.gameObject.SetActive(false);
                }
            }
        }
    }
}
