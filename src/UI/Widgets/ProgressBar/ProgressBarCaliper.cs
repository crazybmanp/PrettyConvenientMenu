using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets.ProgressBar
{
    internal static class ProgressBarCaliper
    {
        public static void Apply(
            RectTransform barRt,
            float origin,
            float? leftSpan,
            Color leftColor,
            float? rightSpan,
            Color rightColor,
            bool hasCaliper)
        {
            Transform caliperTransform = barRt.Find("CaliperArea");
            if (!hasCaliper)
            {
                if (caliperTransform != null)
                {
                    caliperTransform.gameObject.SetActive(false);
                }
                return;
            }

            GameObject caliperObj = caliperTransform == null
                ? new GameObject("CaliperArea", typeof(RectTransform))
                : caliperTransform.gameObject;
            if (caliperTransform == null)
            {
                caliperObj.transform.SetParent(barRt, false);
            }
            caliperObj.SetActive(true);

            RectTransform caliperRt = caliperObj.GetComponent<RectTransform>();
            caliperRt.anchorMin = new Vector2(0f, 0f);
            caliperRt.anchorMax = new Vector2(1f, 0.32f);
            caliperRt.offsetMin = Vector2.zero;
            caliperRt.offsetMax = Vector2.zero;

            // Needle stem tick in caliper
            Transform tickTransform = caliperObj.transform.Find("CaliperTick");
            GameObject tickObj = tickTransform == null
                ? new GameObject("CaliperTick", typeof(RectTransform), typeof(Image))
                : tickTransform.gameObject;
            if (tickTransform == null)
            {
                tickObj.transform.SetParent(caliperObj.transform, false);
            }
            tickObj.SetActive(true);

            RectTransform tickRt = tickObj.GetComponent<RectTransform>();
            tickRt.anchorMin = new Vector2(origin, 0f);
            tickRt.anchorMax = new Vector2(origin, 1f);
            tickRt.offsetMin = new Vector2(-0.75f, 0f);
            tickRt.offsetMax = new Vector2(0.75f, 0f);
            tickObj.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f, 0.8f);

            // Left Caliper Bracket (e.g. Committed)
            Transform commLineTransform = caliperObj.transform.Find("CommLine");
            if (leftSpan.HasValue && leftSpan.Value > 0.001f)
            {
                float leftEdge = Mathf.Clamp01(Mathf.Max(0f, origin - leftSpan.Value));
                GameObject commLineObj = commLineTransform == null
                    ? new GameObject("CommLine", typeof(RectTransform), typeof(Image))
                    : commLineTransform.gameObject;
                if (commLineTransform == null)
                {
                    commLineObj.transform.SetParent(caliperObj.transform, false);
                }
                commLineObj.SetActive(true);

                RectTransform cLineRt = commLineObj.GetComponent<RectTransform>();
                cLineRt.anchorMin = new Vector2(leftEdge, 0.25f);
                cLineRt.anchorMax = new Vector2(origin, 0.75f);
                cLineRt.offsetMin = Vector2.zero;
                cLineRt.offsetMax = Vector2.zero;
                commLineObj.GetComponent<Image>().color = leftColor;

                // Left End-Cap Tick
                Transform commCapTransform = caliperObj.transform.Find("CommCap");
                GameObject commCapObj = commCapTransform == null
                    ? new GameObject("CommCap", typeof(RectTransform), typeof(Image))
                    : commCapTransform.gameObject;
                if (commCapTransform == null)
                {
                    commCapObj.transform.SetParent(caliperObj.transform, false);
                }
                commCapObj.SetActive(true);

                RectTransform cCapRt = commCapObj.GetComponent<RectTransform>();
                cCapRt.anchorMin = new Vector2(leftEdge, 0f);
                cCapRt.anchorMax = new Vector2(leftEdge, 1f);
                cCapRt.offsetMin = new Vector2(-1f, 0f);
                cCapRt.offsetMax = new Vector2(1f, 0f);
                commCapObj.GetComponent<Image>().color = new Color(leftColor.r, leftColor.g, leftColor.b, 1f);
            }
            else if (commLineTransform != null)
            {
                commLineTransform.gameObject.SetActive(false);
                Transform commCapTransform = caliperObj.transform.Find("CommCap");
                if (commCapTransform != null)
                {
                    commCapTransform.gameObject.SetActive(false);
                }
            }

            // Right Caliper Bracket (e.g. Inbound)
            Transform inbLineTransform = caliperObj.transform.Find("InbLine");
            if (rightSpan.HasValue && rightSpan.Value > 0.001f)
            {
                float rightEdge = Mathf.Clamp01(origin + rightSpan.Value);
                GameObject inbLineObj = inbLineTransform == null
                    ? new GameObject("InbLine", typeof(RectTransform), typeof(Image))
                    : inbLineTransform.gameObject;
                if (inbLineTransform == null)
                {
                    inbLineObj.transform.SetParent(caliperObj.transform, false);
                }
                inbLineObj.SetActive(true);

                RectTransform iLineRt = inbLineObj.GetComponent<RectTransform>();
                iLineRt.anchorMin = new Vector2(origin, 0.25f);
                iLineRt.anchorMax = new Vector2(rightEdge, 0.75f);
                iLineRt.offsetMin = Vector2.zero;
                iLineRt.offsetMax = Vector2.zero;
                inbLineObj.GetComponent<Image>().color = rightColor;

                // Right End-Cap Tick
                Transform inbCapTransform = caliperObj.transform.Find("InbCap");
                GameObject inbCapObj = inbCapTransform == null
                    ? new GameObject("InbCap", typeof(RectTransform), typeof(Image))
                    : inbCapTransform.gameObject;
                if (inbCapTransform == null)
                {
                    inbCapObj.transform.SetParent(caliperObj.transform, false);
                }
                inbCapObj.SetActive(true);

                RectTransform iCapRt = inbCapObj.GetComponent<RectTransform>();
                iCapRt.anchorMin = new Vector2(rightEdge, 0f);
                iCapRt.anchorMax = new Vector2(rightEdge, 1f);
                iCapRt.offsetMin = new Vector2(-1f, 0f);
                iCapRt.offsetMax = new Vector2(1f, 0f);
                inbCapObj.GetComponent<Image>().color = new Color(rightColor.r, rightColor.g, rightColor.b, 1f);
            }
            else if (inbLineTransform != null)
            {
                inbLineTransform.gameObject.SetActive(false);
                Transform inbCapTransform = caliperObj.transform.Find("InbCap");
                if (inbCapTransform != null)
                {
                    inbCapTransform.gameObject.SetActive(false);
                }
            }
        }
    }
}
