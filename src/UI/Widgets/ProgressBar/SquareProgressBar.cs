using System;
using System.Collections.Generic;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets.ProgressBar
{
    public enum StackDirection
    {
        Forward,
        Backward
    }

    public sealed class SegmentStyle
    {
        public Color Color { get; set; }
        public bool IsStriped { get; set; }

        public static SegmentStyle Solid(Color color) => new SegmentStyle { Color = color, IsStriped = false };

        public static SegmentStyle Striped(Color color) => new SegmentStyle { Color = color, IsStriped = true };
    }

    public sealed class BarSegment
    {
        public float Width { get; }
        public SegmentStyle Style { get; }
        public StackDirection Direction { get; }

        public BarSegment(float width, SegmentStyle style, StackDirection direction)
        {
            Width = width;
            Style = style;
            Direction = direction;
        }
    }

    /// <summary>
    /// Custom multi-segment progress bar builder supporting forward/backward fills,
    /// transition needles, caliper projections, and multi-landing stacking bars.
    /// </summary>
    public sealed class SquareProgressBar
    {
        private readonly List<BarSegment> segments = new List<BarSegment>(4);
        private bool hasMarker;
        private float markerPosition;
        private Color markerColor = Color.white;
        private float markerWidth = 2f;

        private bool hasCaliper;
        private float caliperOrigin;
        private float? caliperLeftSpan;
        private Color caliperLeftColor = new Color(1.0f, 0.60f, 0.0f, 0.95f);
        private float? caliperRightSpan;
        private Color caliperRightColor = new Color(0.30f, 0.69f, 0.31f, 0.95f);

        // Lower-half stacking landing decorator
        private bool hasLandingStack;
        private readonly List<(float Ratio, Color Color, string Label)> landingStackSegments = new List<(float Ratio, Color Color, string Label)>(4);

        /// <summary>
        /// Adds a styled colored segment to the bar.
        /// </summary>
        public SquareProgressBar AddBar(float width, SegmentStyle style, StackDirection direction = StackDirection.Forward)
        {
            if (width > 0f)
            {
                segments.Add(new BarSegment(width, style, direction));
            }
            return this;
        }

        /// <summary>
        /// Adds a vertical marker or needle line across the fill area.
        /// </summary>
        public SquareProgressBar WithMarker(float position, Color color, float width = 2f)
        {
            hasMarker = true;
            markerPosition = Mathf.Clamp01(position);
            markerColor = color;
            markerWidth = width;
            return this;
        }

        /// <summary>
        /// Adds caliper brackets beneath the progress bar to visualize en-route shipments.
        /// </summary>
        public SquareProgressBar WithCaliper(
            float origin,
            float? leftSpan = null,
            Color? leftColor = null,
            float? rightSpan = null,
            Color? rightColor = null)
        {
            bool hasActiveSpan = (leftSpan.HasValue && leftSpan.Value > 0.001f) ||
                                 (rightSpan.HasValue && rightSpan.Value > 0.001f);
            hasCaliper = hasActiveSpan;
            if (!hasActiveSpan)
            {
                return this;
            }

            caliperOrigin = Mathf.Clamp01(origin);
            caliperLeftSpan = leftSpan;
            if (leftColor.HasValue)
            {
                caliperLeftColor = leftColor.Value;
            }
            caliperRightSpan = rightSpan;
            if (rightColor.HasValue)
            {
                caliperRightColor = rightColor.Value;
            }
            return this;
        }

        /// <summary>
        /// Configures the lower half of the progress bar as a multi-landing stacked breakdown.
        /// </summary>
        public SquareProgressBar WithLandingStack(IReadOnlyList<(float Ratio, Color Color, string Label)>? stack)
        {
            if (stack != null && stack.Count > 0)
            {
                hasLandingStack = true;
                landingStackSegments.Clear();
                landingStackSegments.AddRange(stack);
            }
            return this;
        }

        /// <summary>
        /// Builds and styles the progress bar widget within the provided UIPanelBuilder.
        /// </summary>
        public void Build(UIPanelBuilder b)
        {
            RectTransform barRt = b.AddProgressBar(0f);
            if (barRt == null)
            {
                return;
            }

            Slider slider = barRt.GetComponent<Slider>();
            if (slider == null)
            {
                return;
            }

            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            if (slider.handleRect != null)
            {
                slider.handleRect.gameObject.SetActive(false);
                slider.handleRect = null;
            }

            Transform handleArea = barRt.Find("Handle Slide Area");
            if (handleArea != null)
            {
                handleArea.gameObject.SetActive(false);
            }

            for (int c = 0; c < barRt.childCount; c++)
            {
                Transform child = barRt.GetChild(c);
                if (child.name.IndexOf("handle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    child.gameObject.SetActive(false);
                }
            }

            // Height determination based on active features
            float totalHeight = 8f;
            if (hasLandingStack && hasCaliper)
            {
                totalHeight = 26f;
            }
            else if (hasLandingStack)
            {
                totalHeight = 20f;
            }
            else if (hasCaliper)
            {
                totalHeight = 16f;
            }

            float caliperSpace = hasCaliper ? 6f : 0f;

            // Bar Background
            Transform bg = slider.transform.Find("Background");
            if (bg != null)
            {
                RectTransform? bgRt = bg as RectTransform;
                Image bgImg = bg.GetComponent<Image>();
                if (bgRt != null && bgImg != null)
                {
                    bgRt.anchorMin = new Vector2(0f, 0f);
                    bgRt.anchorMax = new Vector2(1f, 1f);
                    bgRt.offsetMin = new Vector2(0f, caliperSpace);
                    bgRt.offsetMax = Vector2.zero;
                    bgImg.color = new Color(0.18f, 0.18f, 0.18f, 0.85f);
                    bgImg.sprite = null;
                }
            }

            // Main Fill Area
            Transform fillArea = barRt.Find("Fill Area");
            if (fillArea != null)
            {
                RectTransform? fillAreaRt = fillArea as RectTransform;
                if (fillAreaRt != null)
                {
                    if (hasLandingStack)
                    {
                        fillAreaRt.anchorMin = new Vector2(0f, 0.52f);
                        fillAreaRt.anchorMax = new Vector2(1f, 1f);
                        fillAreaRt.offsetMin = new Vector2(0f, caliperSpace * 0.5f);
                        fillAreaRt.offsetMax = Vector2.zero;
                    }
                    else
                    {
                        fillAreaRt.anchorMin = new Vector2(0f, 0f);
                        fillAreaRt.anchorMax = new Vector2(1f, 1f);
                        fillAreaRt.offsetMin = new Vector2(0f, caliperSpace);
                        fillAreaRt.offsetMax = Vector2.zero;
                    }
                }
            }

            if (slider.fillRect != null)
            {
                slider.fillRect.gameObject.SetActive(false);
                slider.fillRect = null;
            }
            slider.enabled = false;

            // Render Main Stock Segments
            if (fillArea != null)
            {
                float cursor = 0f;
                for (int i = 0; i < segments.Count; i++)
                {
                    BarSegment seg = segments[i];
                    float from;
                    float to;

                    if (seg.Direction == StackDirection.Forward)
                    {
                        from = cursor;
                        to = Mathf.Clamp01(cursor + seg.Width);
                        cursor = to;
                    }
                    else
                    {
                        to = cursor;
                        from = Mathf.Clamp01(Mathf.Max(0f, cursor - seg.Width));
                        cursor = from;
                    }

                    if (to - from <= 0.0005f)
                    {
                        continue;
                    }

                    string segName = $"Segment_{i}_{seg.Direction}";
                    Transform segTransform = fillArea.Find(segName);
                    GameObject segObj;

                    if (seg.Style.IsStriped)
                    {
                        if (segTransform == null)
                        {
                            segObj = new GameObject(segName, typeof(RectTransform), typeof(TiledStripeImage));
                            segObj.transform.SetParent(fillArea, false);
                        }
                        else
                        {
                            segObj = segTransform.gameObject;
                            if (segObj.GetComponent<TiledStripeImage>() == null)
                            {
                                Image oldImg = segObj.GetComponent<Image>();
                                if (oldImg != null)
                                {
                                    UnityEngine.Object.DestroyImmediate(oldImg);
                                }
                                segObj.AddComponent<TiledStripeImage>();
                            }
                        }

                        TiledStripeImage stripeImg = segObj.GetComponent<TiledStripeImage>();
                        stripeImg.texture = TiledStripeImage.GetOrCreateStripedTexture();
                        stripeImg.material = null;
                        stripeImg.color = seg.Style.Color;
                    }
                    else
                    {
                        if (segTransform == null)
                        {
                            segObj = new GameObject(segName, typeof(RectTransform), typeof(Image));
                            segObj.transform.SetParent(fillArea, false);
                        }
                        else
                        {
                            segObj = segTransform.gameObject;
                        }

                        Image img = segObj.GetComponent<Image>();
                        img.color = seg.Style.Color;
                        img.sprite = null;
                    }

                    segObj.SetActive(true);
                    RectTransform segRt = segObj.GetComponent<RectTransform>();
                    segRt.anchorMin = new Vector2(from, 0f);
                    segRt.anchorMax = new Vector2(to, 1f);
                    segRt.offsetMin = Vector2.zero;
                    segRt.offsetMax = Vector2.zero;
                }

                // Marker / Needle
                Transform markerTransform = fillArea.Find("ActiveNeedle");
                if (hasMarker)
                {
                    GameObject markerObj = markerTransform == null
                        ? new GameObject("ActiveNeedle", typeof(RectTransform), typeof(Image))
                        : markerTransform.gameObject;
                    if (markerTransform == null)
                    {
                        markerObj.transform.SetParent(fillArea, false);
                    }
                    markerObj.SetActive(true);

                    RectTransform needleRt = markerObj.GetComponent<RectTransform>();
                    needleRt.anchorMin = new Vector2(markerPosition, 0f);
                    needleRt.anchorMax = new Vector2(markerPosition, 1f);
                    float halfW = markerWidth * 0.5f;
                    needleRt.offsetMin = new Vector2(-halfW, 0f);
                    needleRt.offsetMax = new Vector2(halfW, 0f);

                    Image needleImg = markerObj.GetComponent<Image>();
                    needleImg.color = markerColor;
                    needleImg.sprite = null;
                }
                else if (markerTransform != null)
                {
                    markerTransform.gameObject.SetActive(false);
                }
            }

            // Lower-Half Landing Stack Decorator
            ProgressBarLandingStack.Apply(barRt, landingStackSegments, hasLandingStack, bottomOffset: caliperSpace);

            // Calipers (pinned to bottom 5px)
            ProgressBarCaliper.Apply(
                barRt,
                caliperOrigin,
                caliperLeftSpan,
                caliperLeftColor,
                caliperRightSpan,
                caliperRightColor,
                hasCaliper
            );

            LayoutElement le = barRt.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredHeight = totalHeight;
            }
        }
    }
}
