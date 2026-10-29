using TMPro;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Widgets
{
    internal static class SplitInfoRow
    {
        public static void Add(
            UIPanelBuilder builder,
            string? leftText,
            string? rightText,
            float leftWidth = 0f,
            float rightWidth = 180f,
            float spacing = 8f)
        {
            if (string.IsNullOrEmpty(leftText) && string.IsNullOrEmpty(rightText))
            {
                return;
            }

            builder.HStack(row =>
            {
                row.ConfigureLayout(spacing);

                if (leftWidth > 0f)
                {
                    row.AddLabel($"<size=12><color=#B0BEC5>{leftText}</color></size>", t =>
                    {
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).Width(leftWidth);
                }
                else
                {
                    row.AddLabel($"<size=12><color=#B0BEC5>{leftText}</color></size>", t =>
                    {
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).FlexibleWidth(1f);
                }

                if (rightWidth > 0f)
                {
                    row.AddLabel($"<size=12><color=#B0BEC5>{rightText}</color></size>", t =>
                    {
                        t.alignment = TextAlignmentOptions.Right;
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).Width(rightWidth);
                }
                else
                {
                    row.AddLabel($"<size=12><color=#B0BEC5>{rightText}</color></size>", t =>
                    {
                        t.alignment = TextAlignmentOptions.Right;
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                    }).FlexibleWidth(1f);
                }
            });
        }
    }
}
