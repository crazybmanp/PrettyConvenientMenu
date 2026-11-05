using System;
using System.Collections;
using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.UI.Sections;
using UI.Builder;
using UI.CompanyWindow;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Windows
{
    [RequireComponent(typeof(Window))]
    public class FacilityWindow : BuilderWindowBase<FacilityWindow>
    {
        private static readonly Vector2Int DefaultWindowSize = new Vector2Int(680, 620);
        private static readonly Vector2Int MinWindowSize = new Vector2Int(560, 400);

        private const float RefreshIntervalSeconds = 3.0f;
        private const int MaxCascadeSteps = 8;
        private const float CascadeStepOffset = 15f;
        private const float ScrollPositionRestoreEpsilon = 0.01f;
        private const float DiagnosticsBottomSpacing = 6f;

        // Scroll content right-edge padding (px):
        // - BaseScrollRightPadding: matches the 12px visual gap on the left side of the window when no scrollbar is shown.
        // - ScrollbarInsetWidth: horizontal space occupied by the game's vertical scrollbar handle inside the content rect.
        // - ActiveScrollRightPadding: preserves the same 12px visual gap to the left of the scrollbar handle.
        private const int BaseScrollRightPadding = 2;
        private const int ScrollbarInsetWidth = 10;
        private const int ActiveScrollRightPadding = BaseScrollRightPadding + ScrollbarInsetWidth;
        private const float ScrollbarOverflowTolerance = 0.5f;

        private static readonly Dictionary<string, FacilityWindow> ActiveWindows = new Dictionary<string, FacilityWindow>();
        private static readonly WaitForSeconds RefreshInterval = new WaitForSeconds(RefreshIntervalSeconds);
        private static int cascadeCount = 0;

        public override string Identifier => identifier;
        public override WindowConfig Config => WindowConfig.Resizable(DefaultWindowSize, MinWindowSize);

        private string identifier = "PrettyConvenientMenu.Facility";
        private readonly FacilitySnapshot snapshot = new FacilitySnapshot();
        private Industry? currentIndustry;
        private Coroutine? refreshCoroutine;
        private ScrollRect? activeScrollRect;
        private RectTransform? companyButtonRect;
        private float savedScrollPos = 1.0f;
        private float savedAnchoredY = 0f;

        public static FacilityWindow? Open(Industry industry)
        {
            string indId = industry.identifier ?? string.Empty;

            try
            {
                if (ActiveWindows.TryGetValue(indId, out FacilityWindow existingWindow) &&
                    existingWindow != null &&
                    existingWindow.Window != null &&
                    existingWindow.Window.IsShown)
                {
                    existingWindow.Window.OrderFront();
                    return existingWindow;
                }

                if (WindowManager.Shared == null)
                {
                    ModEntryPoint.Log.Error("Cannot open FacilityWindow: WindowManager.Shared is unavailable.");
                    return null;
                }

                FacilityWindow window = WindowManager.Shared.CreateWindowWithComponent<FacilityWindow>();
                window.identifier = $"PrettyConvenientMenu.Facility.{indId}";
                ActiveWindows[indId] = window;
                window.InitializeForIndustry(industry);

                if (window.Window == null) return window;
                Vector2 basePos = window.Window.GetPosition();
                float offset = (cascadeCount % MaxCascadeSteps) * CascadeStepOffset;
                cascadeCount++;
                if (offset > 0.001f)
                {
                    window.Window.SetPositionRestoring(new Vector2(basePos.x + offset, basePos.y - offset));
                }

                return window;
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to open FacilityWindow for '{indId}'.");
                return null;
            }
        }

        private void InitializeForIndustry(Industry industry)
        {
            currentIndustry = industry;
            Title = $"{industry.DisplayName} — Facility Monitor";

            FacilityDataService.PopulateSnapshot(industry, snapshot);
            Show();
            UpdateScrollPadding();

            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
            }
            refreshCoroutine = StartCoroutine(PeriodicRefreshLoop());
        }

        private void OnRectTransformDimensionsChange()
        {
            UpdateScrollPadding();
        }

        protected override void OnWindowDisable()
        {
            base.OnWindowDisable();

            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }

            if (currentIndustry != null && !string.IsNullOrEmpty(currentIndustry.identifier))
            {
                ActiveWindows.Remove(currentIndustry.identifier);
            }

            currentIndustry = null;
            activeScrollRect = null;
            companyButtonRect = null;
            savedScrollPos = 1.0f;
            savedAnchoredY = 0f;
            snapshot.Clear();

            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        private IEnumerator PeriodicRefreshLoop()
        {
            yield return null;
            UpdateScrollPadding();

            while (Window != null && Window.IsShown)
            {
                yield return RefreshInterval;

                if (currentIndustry == null || Window == null || !Window.IsShown) continue;
                try
                {
                    if (activeScrollRect != null && activeScrollRect.content != null)
                    {
                        savedScrollPos = activeScrollRect.verticalNormalizedPosition;
                        savedAnchoredY = activeScrollRect.content.anchoredPosition.y;
                    }

                    FacilityDataService.PopulateSnapshot(currentIndustry, snapshot);
                    RebuildPanel();

                    if (activeScrollRect != null && activeScrollRect.content != null)
                    {
                        UpdateScrollPadding();

                        if (!float.IsNaN(savedScrollPos) && !float.IsInfinity(savedScrollPos))
                        {
                            activeScrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedScrollPos);
                        }

                        if (Mathf.Abs(savedAnchoredY) > ScrollPositionRestoreEpsilon)
                        {
                            activeScrollRect.content.anchoredPosition = new Vector2(activeScrollRect.content.anchoredPosition.x, savedAnchoredY);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Error(ex, $"Periodic refresh error in FacilityWindow for '{snapshot.DisplayName}'.");
                }
            }

            refreshCoroutine = null;
        }

        protected override void BuildContent(UIPanelBuilder builder)
        {
            try
            {
                RectTransform scrollRt = builder.VScrollView(scrollBuilder =>
                {
                    scrollBuilder.AddTitle(snapshot.DisplayName, snapshot.TierDescription);

                    // Top-right floating button to open this industry in the Company Panel
                    IConfigurableElement btnElem = scrollBuilder.AddButtonCompact("Company Panel", () =>
                    {
                        string? indId = currentIndustry?.identifier ?? snapshot.IndustryId;
                        if (!string.IsNullOrEmpty(indId) && CompanyWindow.Shared != null)
                        {
                            CompanyWindow.Shared.ShowIndustry(indId);
                        }
                    });
                    btnElem.Tooltip("Company Panel", "Open this industry in the Company Window.");
                    companyButtonRect = btnElem.RectTransform;
                    LayoutElement le = companyButtonRect.GetComponent<LayoutElement>() ?? companyButtonRect.gameObject.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                    companyButtonRect.anchorMin = new Vector2(1f, 1f);
                    companyButtonRect.anchorMax = new Vector2(1f, 1f);
                    companyButtonRect.pivot = new Vector2(1f, 1f);
                    companyButtonRect.anchoredPosition = new Vector2(-BaseScrollRightPadding, 0f);

                    // 1. Facility-specific Status & Process Header
                    FacilityHeaderSection.Build(scrollBuilder, snapshot);

                    // 2. Facility-specific Storage Buffers
                    StorageBuffersSection.Build(scrollBuilder, snapshot);

                    // 3. Bottleneck / Facility Diagnostics
                    FacilityDiagnosticsSection.Build(scrollBuilder, snapshot);
                    scrollBuilder.Spacer(DiagnosticsBottomSpacing);

                    // 4. Rolling Stock
                    RollingStockSection.Build(scrollBuilder, snapshot);
                });

                activeScrollRect = scrollRt != null ? scrollRt.GetComponent<ScrollRect>() : null;
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to render FacilityWindow for '{snapshot.DisplayName}'.");
                builder.AddLabel("<color=#EF5350><b>Error rendering window content.</b> Check Player.log for details.</color>");
            }
        }

        /// <summary>
        /// Adjusts scroll content padding and the floating Company Panel button position
        /// so that content never collides with the vertical scrollbar handle when visible,
        /// and ensures the left and right margins against the window edges are identical when closed.
        /// </summary>
        private void UpdateScrollPadding()
        {
            if (activeScrollRect == null || activeScrollRect.content == null)
            {
                return;
            }

            if (Window != null)
            {
                RectTransform windowRt = Window.GetComponent<RectTransform>();
                if (windowRt != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(windowRt);
                }
                if (Window.contentRectTransform != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(Window.contentRectTransform);
                }
            }

            RectTransform content = activeScrollRect.content;
            RectTransform viewport = activeScrollRect.viewport != null
                ? activeScrollRect.viewport
                : (RectTransform)activeScrollRect.transform;

            LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            bool isScrollbarVisible = content.rect.height > (viewport.rect.height + ScrollbarOverflowTolerance);
            int rightPadding = isScrollbarVisible ? ActiveScrollRightPadding : BaseScrollRightPadding;

            if (layout.padding.right != rightPadding)
            {
                layout.padding = new RectOffset(layout.padding.left, rightPadding, layout.padding.top, layout.padding.bottom);
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }

            if (companyButtonRect != null)
            {
                companyButtonRect.anchoredPosition = new Vector2(-rightPadding, 0f);
            }
        }
    }
}
