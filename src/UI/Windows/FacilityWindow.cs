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
            RollingStockSection.UpdateScrollPadding(activeScrollRect, Window?.contentRectTransform);

            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
            }
            refreshCoroutine = StartCoroutine(PeriodicRefreshLoop());
        }

        private void OnRectTransformDimensionsChange()
        {
            RollingStockSection.UpdateScrollPadding(activeScrollRect, Window?.contentRectTransform);
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
            RollingStockSection.UpdateScrollPadding(activeScrollRect, Window?.contentRectTransform);

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
                        RollingStockSection.UpdateScrollPadding(activeScrollRect, Window?.contentRectTransform);

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
                builder.AddTitle(snapshot.DisplayName, snapshot.TierDescription);

                // Top-right floating button to open this industry in the Company Panel
                IConfigurableElement btnElem = builder.AddButtonCompact("Company Panel", () =>
                {
                    string? indId = currentIndustry?.identifier ?? snapshot.IndustryId;
                    if (!string.IsNullOrEmpty(indId) && CompanyWindow.Shared != null)
                    {
                        CompanyWindow.Shared.ShowIndustry(indId);
                    }
                });
                btnElem.Tooltip("Company Panel", "Open this industry in the Company Window.");
                RectTransform btnRt = btnElem.RectTransform;
                LayoutElement le = btnRt.GetComponent<LayoutElement>() ?? btnRt.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                btnRt.anchorMin = new Vector2(1f, 1f);
                btnRt.anchorMax = new Vector2(1f, 1f);
                btnRt.pivot = new Vector2(1f, 1f);
                btnRt.anchoredPosition = Vector2.zero;

                // 1. Facility-specific Status & Process Header
                FacilityHeaderSection.Build(builder, snapshot);

                // 2. Facility-specific Storage Buffers
                StorageBuffersSection.Build(builder, snapshot);

                // 3. Bottleneck / Facility Diagnostics
                FacilityDiagnosticsSection.Build(builder, snapshot);
                builder.Spacer(DiagnosticsBottomSpacing);

                // 4. Rolling Stock
                activeScrollRect = RollingStockSection.Build(builder, snapshot, savedAnchoredY);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to render FacilityWindow for '{snapshot.DisplayName}'.");
                builder.AddLabel("<color=#EF5350><b>Error rendering window content.</b> Check Player.log for details.</color>");
            }
        }
    }
}
