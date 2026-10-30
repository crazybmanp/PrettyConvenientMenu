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
        private static readonly Dictionary<string, FacilityWindow> ActiveWindows = new Dictionary<string, FacilityWindow>();
        private static int cascadeCount = 0;

        public override string Identifier => identifier;
        public override WindowConfig Config => WindowConfig.Resizable(
            new Vector2Int(680, 620),
            new Vector2Int(560, 400)
        );

        private string identifier = "PrettyConvenientMenu.Facility";
        private readonly FacilitySnapshot snapshot = new FacilitySnapshot();
        private Industry? currentIndustry;
        private Coroutine? refreshCoroutine;
        private static readonly WaitForSeconds RefreshInterval = new WaitForSeconds(3.0f);
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
                float offset = (cascadeCount % 8) * 15f;
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

            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
            }
            refreshCoroutine = StartCoroutine(PeriodicRefreshLoop());
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
                        RectTransform rootRt = GetComponent<RectTransform>();
                        if (rootRt != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
                        }
                        Canvas.ForceUpdateCanvases();

                        // Dynamically update right padding if scrollbar is present or not
                        VerticalLayoutGroup vlg = activeScrollRect.content.GetComponent<VerticalLayoutGroup>();
                        if (vlg != null)
                        {
                            float scrollbarWidth = 20f;
                            if (activeScrollRect.verticalScrollbar != null)
                            {
                                RectTransform? sbarRt = activeScrollRect.verticalScrollbar.transform as RectTransform;
                                if (sbarRt != null && sbarRt.rect.width > 0f)
                                {
                                    scrollbarWidth = sbarRt.rect.width;
                                }
                            }

                            float viewH = activeScrollRect.viewport != null ? activeScrollRect.viewport.rect.height : 240f;
                            bool scrollbarActive = activeScrollRect.content.rect.height > viewH;
                            int targetPad = scrollbarActive ? (int)Mathf.Ceil(scrollbarWidth + 4f) : 6;
                            if (vlg.padding.right != targetPad)
                            {
                                vlg.padding.right = targetPad;
                                LayoutRebuilder.ForceRebuildLayoutImmediate(activeScrollRect.content);
                            }
                        }

                        if (!float.IsNaN(savedScrollPos) && !float.IsInfinity(savedScrollPos))
                        {
                            activeScrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedScrollPos);
                        }

                        if (Mathf.Abs(savedAnchoredY) > 0.01f)
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
                btnRt.anchoredPosition = new Vector2(0f, 0f);

                // 1. Facility-specific Status & Process Header
                FacilityHeaderSection.Build(builder, snapshot);

                // 2. Facility-specific Storage Buffers
                StorageBuffersSection.Build(builder, snapshot);

                // 3. Bottleneck / Facility Diagnostics
                FacilityDiagnosticsSection.Build(builder, snapshot);
                builder.Spacer(6f);

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
