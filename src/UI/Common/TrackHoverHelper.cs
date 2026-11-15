using System.Collections.Generic;
using TMPro;
using UI;
using UnityEngine;

namespace PrettyConvenientMenu.UI.Common
{
    /// <summary>
    /// Attaches native track-span hover highlighting to UI labels.
    /// </summary>
    internal static class TrackHoverHelper
    {
        /// <summary>
        /// Configures pointer raycasting and <see cref="LocationIndicatorHoverArea"/> on the target label
        /// when one or more track span IDs are present.
        /// </summary>
        public static void Attach(RectTransform rect, IReadOnlyList<string> spanIds)
        {
            if (spanIds == null || spanIds.Count == 0)
            {
                return;
            }

            if (rect.TryGetComponent<TMP_Text>(out TMP_Text label))
            {
                label.raycastTarget = true;
            }

            LocationIndicatorHoverArea hoverArea = rect.gameObject.AddComponent<LocationIndicatorHoverArea>();
            hoverArea.spanIds.AddRange(spanIds);
        }
    }
}
