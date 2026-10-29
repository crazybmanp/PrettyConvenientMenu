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

        public SquareProgressBar AddBar(float width, SegmentStyle style, StackDirection direction = StackDirection.Forward)
        {
            if (width > 0.0001f)
            {
                segments.Add(new BarSegment(width, style, direction));
            }
            return this;
        }

        public SquareProgressBar AddBar(float width, Color color)
        {
            return AddBar(width, SegmentStyle.Solid(color), StackDirection.Forward);
        }

        public SquareProgressBar WithMarker(float position, Color color, float width = 2f)
        {
            hasMarker = true;
            markerPosition = Mathf.Clamp01(position);
            markerColor = color;
            markerWidth = width;
            return this;
        }

        public SquareProgressBar WithCaliper(
            float origin,
            float? leftSpan = null,
            Color? leftColor = null,
            float? rightSpan = null,
            Color? rightColor = null)
        {
            hasCaliper = (leftSpan.HasValue && leftSpan.Value > 0.001f) || (rightSpan.HasValue && rightSpan.Value > 0.001f);
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

            float barBottomY = hasCaliper ? 0.38f : 0f;

            Transform fillArea = barRt.Find("Fill Area");
            if (fillArea != null)
            {
                RectTransform? fillAreaRt = fillArea as RectTransform;
                if (fillAreaRt != null)
                {
                    fillAreaRt.anchorMin = new Vector2(0f, barBottomY);
                    fillAreaRt.anchorMax = new Vector2(1f, 1f);
                    fillAreaRt.offsetMin = Vector2.zero;
                    fillAreaRt.offsetMax = Vector2.zero;
                }
            }

            Transform bg = slider.transform.Find("Background");
            if (bg != null)
            {
                RectTransform? bgRt = bg as RectTransform;
                Image bgImg = bg.GetComponent<Image>();
                if (bgRt != null && bgImg != null)
                {
                    bgRt.anchorMin = new Vector2(0f, barBottomY);
                    bgRt.anchorMax = new Vector2(1f, 1f);
                    bgRt.offsetMin = Vector2.zero;
                    bgRt.offsetMax = Vector2.zero;
                    bgImg.color = new Color(0.18f, 0.18f, 0.18f, 0.85f);
                    bgImg.sprite = null;
                }
            }

            if (slider.fillRect != null)
            {
                slider.fillRect.gameObject.SetActive(false);
                slider.fillRect = null;
            }
            slider.enabled = false;

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

            // Caliper sub-track
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
                le.preferredHeight = hasCaliper ? 16f : 8f;
            }
        }
    }
}
