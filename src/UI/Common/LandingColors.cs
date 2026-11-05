using UnityEngine;

namespace PrettyConvenientMenu.UI.Common
{
    /// <summary>
    /// Deterministic color palette for multi-landing buffers and matching spur swatches.
    /// Provides consistent, high-contrast swatches across dark UI backgrounds.
    /// </summary>
    internal static class LandingColors
    {
        private static readonly (Color Color, string Hex)[] Palette = new[]
        {
            (new Color(0.00f, 0.74f, 0.83f, 1f), "#00BCD4"), // Cyan (L1)
            (new Color(1.00f, 0.70f, 0.00f, 1f), "#FFB300"), // Amber (L2)
            (new Color(0.67f, 0.28f, 0.74f, 1f), "#AB47BC"), // Purple (L3)
            (new Color(0.15f, 0.65f, 0.60f, 1f), "#26A69A"), // Teal (L4)
            (new Color(1.00f, 0.44f, 0.26f, 1f), "#FF7043"), // Coral (L5)
            (new Color(0.26f, 0.65f, 0.96f, 1f), "#42A5F5")  // Blue (L6)
        };

        public static (Color Color, string Hex) GetSwatch(int index)
        {
            if (index < 0) index = 0;
            return Palette[index % Palette.Length];
        }
    }
}
