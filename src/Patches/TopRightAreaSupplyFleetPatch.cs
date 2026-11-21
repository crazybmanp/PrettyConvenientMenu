using System;
using HarmonyLib;
using PrettyConvenientMenu.UI.SupplyFleet;
using UI;
using UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.Patches
{
    /// <summary>
    /// Adds a native-styled Supply &amp; MOW Fleet Manager icon button to Railroader's Top-Right menu bar,
    /// positioned immediately after the Company Window button.
    /// </summary>
    [HarmonyPatch(typeof(TopRightArea))]
    internal static class TopRightAreaSupplyFleetPatch
    {
        private const string ButtonObjectName = "PCM_SupplyFleetButton";
        private static Sprite? cachedIconSprite;

        [HarmonyPatch("OnEnable")]
        [HarmonyPostfix]
        private static void OnEnablePostfix(TopRightArea __instance)
        {
            if (__instance == null)
            {
                return;
            }

            try
            {
                EnsureButton(__instance);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to inject Supply Fleet button into TopRightArea.");
            }
        }

        private static void EnsureButton(TopRightArea topRightArea)
        {
            Button[] existingButtons = topRightArea.GetComponentsInChildren<Button>(includeInactive: true);
            if (existingButtons == null || existingButtons.Length == 0)
            {
                return;
            }

            for (int i = 0; i < existingButtons.Length; i++)
            {
                if (existingButtons[i] != null && existingButtons[i].gameObject.name == ButtonObjectName)
                {
                    existingButtons[i].gameObject.SetActive(true);
                    return;
                }
            }

            // Locate the Company Window button so we can clone its exact style and place ours right after it
            Button? companyButton = FindCompanyButton(existingButtons);
            Button template = companyButton ?? existingButtons[0];

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            clone.name = ButtonObjectName;
            clone.SetActive(true);

            // Place immediately after the Company Window button
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            Button btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(SupplyFleetWindow.ToggleWindow);
            }

            // Replace the cloned button's icon sprite cleanly (avoid leaving the cloned icon underneath)
            Sprite icon = GetOrCreateMowIconSprite();
            Image[] images = clone.GetComponentsInChildren<Image>(includeInactive: true);
            Image? targetIconImage = null;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject != clone)
                {
                    targetIconImage = images[i];
                    break;
                }
            }

            if (targetIconImage == null)
            {
                targetIconImage = clone.GetComponent<Image>();
            }

            if (targetIconImage != null)
            {
                targetIconImage.sprite = icon;
                targetIconImage.color = Color.white;
                targetIconImage.preserveAspect = true;
            }

            UITooltipProvider tooltip = clone.GetComponent<UITooltipProvider>() ?? clone.AddComponent<UITooltipProvider>();
            tooltip.TooltipInfo = new TooltipInfo(
                "Supply & MOW Fleet Manager",
                "Manage MOW & locomotive supply rolling stock, stockpiles, and Work Orders.");
        }

        private static Button? FindCompanyButton(Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                Button b = buttons[i];
                if (b == null) continue;

                int persistentCount = b.onClick.GetPersistentEventCount();
                for (int p = 0; p < persistentCount; p++)
                {
                    string method = b.onClick.GetPersistentMethodName(p);
                    if (string.Equals(method, "ClickedCompany", StringComparison.OrdinalIgnoreCase))
                    {
                        return b;
                    }
                }

                if (b.gameObject.name.IndexOf("Company", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>
        /// Draws a crisp 32x32 white outline icon (a hopper/MOW supply car on track) matching Railroader's top-bar style.
        /// </summary>
        private static Sprite GetOrCreateMowIconSprite()
        {
            if (cachedIconSprite != null)
            {
                return cachedIconSprite;
            }

            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            void Plot(int x, int y)
            {
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    px[y * size + x] = white;
                }
            }

            void DrawRectOutline(int x0, int y0, int x1, int y1, int thickness = 2)
            {
                for (int t = 0; t < thickness; t++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Plot(x, y0 + t);
                        Plot(x, y1 - t);
                    }
                    for (int y = y0; y <= y1; y++)
                    {
                        Plot(x0 + t, y);
                        Plot(x1 - t, y);
                    }
                }
            }

            void DrawLine(int x0, int y0, int x1, int y1)
            {
                int dx = Mathf.Abs(x1 - x0);
                int dy = Mathf.Abs(y1 - y0);
                int sx = x0 < x1 ? 1 : -1;
                int sy = y0 < y1 ? 1 : -1;
                int err = dx - dy;
                while (true)
                {
                    Plot(x0, y0);
                    Plot(x0 + 1, y0);
                    Plot(x0, y0 + 1);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
            }

            // Track rail at bottom
            DrawLine(2, 4, 29, 4);

            // Supply hopper / work car body (crisp outline)
            DrawRectOutline(4, 14, 27, 25, 2);
            // Tapered hopper bays underneath
            DrawLine(6, 14, 9, 9);
            DrawLine(15, 14, 12, 9);
            DrawLine(9, 9, 12, 9);
            DrawLine(16, 14, 19, 9);
            DrawLine(25, 14, 22, 9);
            DrawLine(19, 9, 22, 9);
            // Load mound peeking above rim
            DrawLine(7, 25, 12, 28);
            DrawLine(12, 28, 19, 28);
            DrawLine(19, 28, 24, 25);
            // Wheels
            DrawRectOutline(7, 6, 10, 9, 1);
            DrawRectOutline(21, 6, 24, 9, 1);

            tex.SetPixels32(px);
            tex.Apply();

            cachedIconSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return cachedIconSprite;
        }
    }
}
