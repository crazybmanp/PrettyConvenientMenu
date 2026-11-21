using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.State;
using Model;
using Model.Definition.Data;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.SupplyFleet;
using PrettyConvenientMenu.Interaction;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets;
using PrettyConvenientMenu.UI.Widgets.ProgressBar;
using TMPro;
using PrettyConvenientMenu.UI.Windows;
using UI.Builder;
using UI.CarInspector;
using UI.Tooltips;
using UI.Windows;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.SupplyFleet
{
    internal enum SupplyFleetTab
    {
        FleetBoard,
        MowSupplies,
        FuelAndRepair,
        ManageFleet
    }

    internal enum FleetGroupingMode
    {
        Destination,
        WorkOrder,
        Location
    }

    /// <summary>
    /// Lightweight click handler for directly-clickable commodity bars.
    /// </summary>
    public sealed class ClickableBarHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action? OnClick { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                OnClick?.Invoke();
            }
        }
    }

    /// <summary>
    /// Main Supply &amp; MOW Fleet Manager window using pure vanilla Railroader UIPanelBuilder layout.
    /// </summary>
    [RequireComponent(typeof(Window))]
    public sealed class SupplyFleetWindow : BuilderWindowBase<SupplyFleetWindow>
    {
        private static readonly Vector2Int DefaultWindowSize = new Vector2Int(960, 660);
        private static readonly Vector2Int MinWindowSize = new Vector2Int(880, 500);
        private static readonly WaitForSeconds RefreshInterval = new WaitForSeconds(5.0f);

        // Scroll content right-edge padding (px): matches FacilityWindow so content
        // neither reserves scrollbar space when none is shown nor collides when visible.
        private const int BaseScrollRightPadding = 2;
        private const int ScrollbarInsetWidth = 10;
        private const int ActiveScrollRightPadding = BaseScrollRightPadding + ScrollbarInsetWidth;
        private const float ScrollbarOverflowTolerance = 0.5f;

        // Subtle Railroader-palette card background (warm charcoal-brown + muted bronze outline)
        private static readonly Color CardBgColor = new Color(0.14f, 0.13f, 0.11f, 0.72f);
        private static readonly Color CardOutlineColor = new Color(0.34f, 0.30f, 0.22f, 0.60f);

        private static SupplyFleetWindow? activeWindow;

        public override string Identifier => "PrettyConvenientMenu.SupplyFleetManager";
        public override WindowConfig Config => WindowConfig.Resizable(DefaultWindowSize, MinWindowSize);

        private readonly SupplyNetworkCollector collector = new SupplyNetworkCollector();
        private readonly SupplyNetworkSnapshot snapshot = new SupplyNetworkSnapshot();
        private readonly HashSet<string> selectedCarIds = new HashSet<string>(StringComparer.Ordinal);
        private string? lastClickedCarId;

        private SupplyFleetTab activeTab = SupplyFleetTab.MowSupplies;
        private FleetGroupingMode fleetGrouping = FleetGroupingMode.Destination;
        private string mowCommodityFilter = "All";

        // Manage Fleet inputs (reporting mark auto-filled from active railroad)
        private string rangeMarkInput = string.Empty;
        private string rangeStartInput = "100";
        private string rangeEndInput = "199";
        private string numberListInput = string.Empty;
        private string statusBanner = string.Empty;

        // Maintenance Job creation (inside Column 3)
        private bool showNewJobForm;
        private string newJobLabel = "Track Rehab";
        private int newJobWorkCarIndex;
        private string newJobBallastTons = "100";
        private string newJobTiesTons = "80";
        private string newJobRailTons = "0";

        // Per-facility Target Stock editor (toggled via "Targets" button in facility header)
        private string? editingTargetsFacilityId;
        private readonly Dictionary<string, string> pendingTargetTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private Coroutine? refreshCoroutine;
        private ScrollRect? activeScrollRect;
        private float savedScrollPos = 1f;

        public static void ToggleWindow()
        {
            if (activeWindow != null && activeWindow.Window != null && activeWindow.Window.IsShown)
            {
                activeWindow.Window.CloseWindow();
                return;
            }

            Open();
        }

        public static SupplyFleetWindow? Open()
        {
            try
            {
                if (activeWindow != null && activeWindow.Window != null && activeWindow.Window.IsShown)
                {
                    activeWindow.Window.OrderFront();
                    return activeWindow;
                }

                if (WindowManager.Shared == null)
                {
                    ModEntryPoint.Log.Error("Cannot open SupplyFleetWindow: WindowManager.Shared is unavailable.");
                    return null;
                }

                SupplyFleetWindow win = WindowManager.Shared.CreateWindowWithComponent<SupplyFleetWindow>();
                activeWindow = win;
                win.Initialize();
                return win;
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to open SupplyFleetWindow.");
                return null;
            }
        }

        private void Initialize()
        {
            Title = "Supply & MOW Fleet Manager";

            string? rrMark = StateManager.Shared?.Storage?.RailroadMark;
            if (!string.IsNullOrWhiteSpace(rrMark))
            {
                rangeMarkInput = rrMark!;
            }

            if (SupplyFleetStore.Shared != null)
            {
                SupplyFleetStore.Shared.OnStateChanged += HandleStoreChanged;
                collector.Populate(SupplyFleetStore.Shared, snapshot);
            }

            if (WorldCarPicker.Shared != null)
            {
                WorldCarPicker.Shared.OnSelectionChanged += HandleWorldPickerChanged;
            }

            Show();

            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
            }
            refreshCoroutine = StartCoroutine(PeriodicRefreshLoop());
        }

        private static RectTransform AddFlowToolbar(UIPanelBuilder parent, Action<UIPanelBuilder> build)
        {
            RectTransform rt = parent.VStack(build);
            if (rt == null) return null!;

            VerticalLayoutGroup? vlg = rt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                DestroyImmediate(vlg);
            }

            FlowLayoutGroup flow = rt.gameObject.AddComponent<FlowLayoutGroup>();
            flow.SpacingX = 6f;
            flow.SpacingY = 4f;
            flow.KeepGroupsTogether = false;
            flow.childAlignment = TextAnchor.MiddleLeft;
            return rt;
        }

        private void HandleStoreChanged()
        {
            RefreshAndRebuild();
        }

        private void HandleWorldPickerChanged()
        {
            if (Window != null && Window.IsShown)
            {
                RebuildView();
            }
        }

        private void RefreshAndRebuild()
        {
            if (SupplyFleetStore.Shared == null || Window == null || !Window.IsShown)
            {
                return;
            }

            if (activeScrollRect != null)
            {
                savedScrollPos = activeScrollRect.verticalNormalizedPosition;
            }

            collector.Populate(SupplyFleetStore.Shared, snapshot);
            RebuildView();

            if (activeScrollRect != null && !float.IsNaN(savedScrollPos) && !float.IsInfinity(savedScrollPos))
            {
                activeScrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedScrollPos);
            }
        }

        private void RebuildView()
        {
            if (activeScrollRect != null)
            {
                savedScrollPos = activeScrollRect.verticalNormalizedPosition;
            }

            RebuildPanel();
            UpdateScrollPadding();

            if (activeScrollRect != null && !float.IsNaN(savedScrollPos) && !float.IsInfinity(savedScrollPos))
            {
                activeScrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedScrollPos);
            }
        }

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
        }

        private IEnumerator PeriodicRefreshLoop()
        {
            yield return null;
            UpdateScrollPadding();

            while (Window != null && Window.IsShown)
            {
                yield return RefreshInterval;
                if (Window == null || !Window.IsShown) continue;

                if (!string.IsNullOrEmpty(editingTargetsFacilityId) ||
                    showNewJobForm ||
                    activeTab == SupplyFleetTab.ManageFleet ||
                    (WorldCarPicker.Shared != null && WorldCarPicker.Shared.IsActive))
                {
                    continue;
                }

                try
                {
                    RefreshAndRebuild();
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Error(ex, "Periodic refresh error in SupplyFleetWindow.");
                }
            }

            refreshCoroutine = null;
        }

        protected override void OnWindowDisable()
        {
            base.OnWindowDisable();

            if (SupplyFleetStore.Shared != null)
            {
                SupplyFleetStore.Shared.OnStateChanged -= HandleStoreChanged;
            }

            if (WorldCarPicker.Shared != null)
            {
                WorldCarPicker.Shared.OnSelectionChanged -= HandleWorldPickerChanged;
                if (WorldCarPicker.Shared.IsActive)
                {
                    WorldCarPicker.Shared.Cancel();
                }
            }

            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }

            if (activeWindow == this)
            {
                activeWindow = null;
            }

            snapshot.Clear();
            selectedCarIds.Clear();

            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        protected override void BuildContent(UIPanelBuilder builder)
        {
            try
            {
                builder.ConfigureLayout(6f);

                BuildTopHeaderBar(builder);

                if (WorldCarPicker.Shared != null && WorldCarPicker.Shared.IsActive)
                {
                    BuildWorldPickerBanner(builder);
                }

                if (!string.IsNullOrEmpty(statusBanner))
                {
                    builder.AddLabel($"<size=12><color=#81C784>{statusBanner}</color></size>");
                }

                RectTransform scrollRt = builder.VScrollView(scroll =>
                {
                    scroll.ConfigureLayout(6f);

                    switch (activeTab)
                    {
                        case SupplyFleetTab.FleetBoard:
                            BuildFleetBoardTab(scroll);
                            break;
                        case SupplyFleetTab.MowSupplies:
                            BuildMowSuppliesTab(scroll);
                            break;
                        case SupplyFleetTab.FuelAndRepair:
                            BuildFuelAndRepairTab(scroll);
                            break;
                        case SupplyFleetTab.ManageFleet:
                            BuildManageFleetTab(scroll);
                            break;
                    }
                });

                activeScrollRect = scrollRt != null ? scrollRt.GetComponent<ScrollRect>() : null;
                if (activeScrollRect != null && activeScrollRect.content != null)
                {
                    VerticalLayoutGroup? contentVlg = activeScrollRect.content.GetComponent<VerticalLayoutGroup>();
                    if (contentVlg != null)
                    {
                        contentVlg.childControlWidth = true;
                        contentVlg.childForceExpandWidth = true;
                    }
                }

                BuildSelectionFooter(builder);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to build SupplyFleetWindow.");
                builder.AddLabel("<color=#EF5350><b>Error rendering Supply Fleet Manager.</b> See Player.log for details.</color>");
            }
        }

        // =========================================================================
        // Vanilla Header, World Picker Banner & Selection Action Bar
        // =========================================================================

        private void BuildTopHeaderBar(UIPanelBuilder builder)
        {
            int totalCars = snapshot.FleetCars.Count;
            int unassigned = snapshot.FleetCars.Count(c => c.ActiveOrder == null);

            RectTransform headerRow = builder.HStack(top =>
            {
                top.ConfigureLayout(8f);

                RectTransform tabsRect = AddFlowToolbar(top, tabs =>
                {
                    tabs.AddButtonSelectable($"Fleet ({totalCars})", activeTab == SupplyFleetTab.FleetBoard, () => SwitchTab(SupplyFleetTab.FleetBoard));
                    tabs.AddButtonSelectable("MOW Supplies", activeTab == SupplyFleetTab.MowSupplies, () => SwitchTab(SupplyFleetTab.MowSupplies));
                    tabs.AddButtonSelectable("Coal, Diesel & Repair", activeTab == SupplyFleetTab.FuelAndRepair, () => SwitchTab(SupplyFleetTab.FuelAndRepair));
                    tabs.AddButtonSelectable("Manage Fleet", activeTab == SupplyFleetTab.ManageFleet, () => SwitchTab(SupplyFleetTab.ManageFleet));
                });
                LayoutElement tabsLayout = tabsRect.gameObject.GetComponent<LayoutElement>() ?? tabsRect.gameObject.AddComponent<LayoutElement>();
                tabsLayout.minWidth = 0f;
                tabsLayout.flexibleWidth = 1f;

                RectTransform rightRect = top.HStack(right =>
                {
                    right.ConfigureLayout(6f);
                    right.AddLabel($"<size=11><color=#B0BEC5>{totalCars} tracked · <b>{unassigned}</b> idle</color></size>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
                    right.AddButtonCompact("Refresh", RefreshAndRebuild);
                });
                HorizontalLayoutGroup? rightGroup = rightRect.GetComponent<HorizontalLayoutGroup>();
                if (rightGroup != null)
                {
                    rightGroup.childAlignment = TextAnchor.MiddleRight;
                    rightGroup.childControlWidth = true;
                    rightGroup.childForceExpandWidth = false;
                }
                LayoutElement rightLayout = rightRect.gameObject.GetComponent<LayoutElement>() ?? rightRect.gameObject.AddComponent<LayoutElement>();
                rightLayout.flexibleWidth = 0f;
            });

            HorizontalLayoutGroup? headerGroup = headerRow.GetComponent<HorizontalLayoutGroup>();
            if (headerGroup != null)
            {
                headerGroup.childAlignment = TextAnchor.MiddleLeft;
                headerGroup.childControlWidth = true;
                headerGroup.childControlHeight = true;
                headerGroup.childForceExpandWidth = false;
            }
        }

        private void BuildWorldPickerBanner(UIPanelBuilder builder)
        {
            WorldCarPicker picker = WorldCarPicker.Shared!;
            IReadOnlyList<Car> pending = picker.PendingCars;

            builder.AddSection("World Car Picker Active", sec =>
            {
                sec.HStack(row =>
                {
                    row.ConfigureLayout(6f);
                    row.AddLabel($"<size=12><color=#FFD54F>Click owned cars in the world (Shift+Click for coupled cut).</color> Selected: <b>{pending.Count}</b></size>");
                    row.Spacer();
                    row.AddButtonCompact($"Confirm Add ({pending.Count})", () => picker.Confirm());
                    row.AddButtonCompact("Cancel", () => picker.Cancel());
                });

                if (pending.Count > 0)
                {
                    string preview = string.Join(", ", pending.Take(14).Select(c => c.DisplayName));
                    if (pending.Count > 14) preview += $" (+{pending.Count - 14} more)";
                    sec.AddLabel($"<size=11><color=#ECEFF1>{preview}</color></size>");
                }
            });
        }

        private void SwitchTab(SupplyFleetTab tab)
        {
            activeTab = tab;
            statusBanner = string.Empty;
            editingTargetsFacilityId = null;
            RefreshAndRebuild();
        }

        private void BuildSelectionFooter(UIPanelBuilder builder)
        {
            List<SupplyCarInfo> selected = snapshot.FleetCars.Where(c => selectedCarIds.Contains(c.CarId)).ToList();
            if (selected.Count == 0 && selectedCarIds.Count > 0)
            {
                selectedCarIds.Clear();
            }

            RectTransform footerRt = builder.VStack(footer =>
            {
                footer.ConfigureLayout(4f);

                if (selected.Count == 0)
                {
                    footer.AddLabel("<size=11><color=#90A4AE>Click car to select · Shift+Click range · Ctrl+Click toggle · Double-click inspect</color></size>");
                    return;
                }

                List<SupplyCarInfo> withSuggestions = selected.Where(c => c.ActiveOrder == null && c.SuggestedJob != null).ToList();
                BuildSelectionToolbar(footer, selected, withSuggestions);

                if (withSuggestions.Count > 0)
                {
                    BuildDetectedJobsSummary(footer, withSuggestions);
                }
            });

            StyleCardContainer(footerRt, new RectOffset(8, 8, 5, 5), TextAnchor.MiddleLeft, minHeight: 32f);
        }

        private void BuildSelectionToolbar(UIPanelBuilder footer, List<SupplyCarInfo> selected, List<SupplyCarInfo> withSuggestions)
        {
            int assignedCount = selected.Count(c => c.ActiveOrder != null);
            List<SupplyCarInfo> loadedUnassigned = selected.Where(c => c.ActiveOrder == null && !c.Car.CarType.IsMaintenanceOfWay && c.IsLoaded).ToList();
            List<SupplyCarInfo> emptyUnassigned = selected.Where(c => c.ActiveOrder == null && !c.Car.CarType.IsMaintenanceOfWay && !c.IsLoaded && !c.IsOffMap).ToList();
            List<SupplyCarInfo> workTrainEditable = selected.Where(c => c.ActiveOrder == null || c.ActiveOrder.IsPassiveWorkTrain).ToList();

            bool canEdit = StateManager.HasTrainmasterAccess;

            AddFlowToolbar(footer, bar =>
            {
                bar.AddLabel($"<size=12><color=#FFD54F><b>{selected.Count} selected</b></color></size>", t => t.textWrappingMode = TextWrappingModes.NoWrap);

                if (canEdit && withSuggestions.Count > 0)
                {
                    IConfigurableElement sugBtn = bar.AddButtonCompact($"Accept Suggested ({withSuggestions.Count})", () => AcceptSuggestedJobs(withSuggestions));
                    string sugTooltip = string.Join("\n", withSuggestions.Take(6).Select(c => $"{c.DisplayName}: {c.SuggestedJob!.Summary}"));
                    sugBtn.Tooltip("Assign Suggested Job(s)", sugTooltip);
                }

                if (canEdit && assignedCount > 0)
                {
                    bar.AddButtonCompact($"Cancel {assignedCount} Order(s)", () => CancelOrdersForSelection(selected));
                }

                if (canEdit && emptyUnassigned.Count > 0)
                {
                    bar.AddButtonCompact($"Assign Order ({emptyUnassigned.Count})", () => OpenAssignWindowForSelection(AssignCarOrderMode.LoadAndDeliver, emptyUnassigned));
                }

                if (canEdit && loadedUnassigned.Count > 0)
                {
                    bar.AddButtonCompact($"Unload {loadedUnassigned.Count} Loaded", () => OpenAssignWindowForSelection(AssignCarOrderMode.DirectUnload, loadedUnassigned));
                }

                if (canEdit && workTrainEditable.Count > 0 && snapshot.AvailableWorkCars.Count > 0)
                {
                    bool allExistingWorkTrain = workTrainEditable.All(c => c.ActiveOrder != null);
                    string wtLabel = allExistingWorkTrain
                        ? $"Edit Work Train ({workTrainEditable.Count})"
                        : $"Work Train ({workTrainEditable.Count})";
                    bar.AddButtonCompact(wtLabel, () => OpenAssignWindowForSelection(AssignCarOrderMode.PassiveWorkTrain, workTrainEditable));
                }

                if (selected.Count == 1)
                {
                    BuildSingleCarButtons(bar, selected[0]);
                }

                if (canEdit)
                {
                    bar.AddButtonCompact("Remove", () => RemoveSelectionFromFleet(selected));
                }
                bar.AddButtonCompact("Clear", () =>
                {
                    selectedCarIds.Clear();
                    RebuildView();
                });
            });
        }

        private static void BuildSingleCarButtons(UIPanelBuilder bar, SupplyCarInfo car)
        {
            IConfigurableElement goToBtn = bar.AddButtonCompact("Go To", () => CameraSelector.shared?.FollowCar(car.Car));
            goToBtn.Tooltip("Follow Car", $"Move camera to follow {car.DisplayName}.");
            bar.AddButtonCompact("Inspect", () => CarInspector.Show(car.Car));
        }

        private void OpenAssignWindowForSelection(AssignCarOrderMode mode, List<SupplyCarInfo> targets)
        {
            AssignCarOrderWindow.Open(mode, targets, snapshot, msg =>
            {
                statusBanner = msg;
                selectedCarIds.Clear();
                RefreshAndRebuild();
            });
        }

        private void CancelOrdersForSelection(List<SupplyCarInfo> selected)
        {
            if (SupplyFleetStore.Shared == null) return;
            int cancelled = SupplyFleetStore.Shared.CancelWorkOrders(selected.Select(s => s.Car));
            statusBanner = $"Cancelled {cancelled} Work Order(s).";
            selectedCarIds.Clear();
            RefreshAndRebuild();
        }

        private void RemoveSelectionFromFleet(List<SupplyCarInfo> selected)
        {
            if (SupplyFleetStore.Shared == null) return;
            foreach (SupplyCarInfo s in selected) SupplyFleetStore.Shared.RemoveCar(s.Car);
            statusBanner = $"Removed {selected.Count} car(s) from supply fleet.";
            selectedCarIds.Clear();
            RefreshAndRebuild();
        }

        private static void BuildDetectedJobsSummary(UIPanelBuilder footer, List<SupplyCarInfo> withSuggestions)
        {
            List<string> groupedSummaries = withSuggestions
                .GroupBy(c => c.SuggestedJob!.Summary, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() > 1 ? $"{g.Count()}x {g.Key}" : g.Key)
                .ToList();

            string labelPrefix = withSuggestions.Count == 1 ? "Detected job" : "Detected jobs";
            footer.AddLabel($"<size=11><color=#81C784><b>{labelPrefix}:</b> {string.Join("  ·  ", groupedSummaries)}</color></size>");
        }

        private void AcceptSuggestedJobs(List<SupplyCarInfo> carsWithSuggestions)
        {
            SupplyFleetStore? store = SupplyFleetStore.Shared;
            if (store == null || carsWithSuggestions.Count == 0)
            {
                return;
            }

            int totalAssigned = 0;
            for (int i = 0; i < carsWithSuggestions.Count; i++)
            {
                SupplyCarInfo carInfo = carsWithSuggestions[i];
                SuggestedCarJob? job = carInfo.SuggestedJob;
                if (job == null) continue;

                switch (job.Kind)
                {
                    case SuggestedJobKind.PassiveWorkTrain:
                        if (job.TargetWorkCar != null)
                        {
                            totalAssigned += store.AssignPassiveWorkTrain(new[] { carInfo }, job.TargetWorkCar);
                        }
                        break;

                    case SuggestedJobKind.DirectUnload:
                        if (job.DestinationBar != null)
                        {
                            totalAssigned += store.DispatchDirectUnload(new[] { carInfo }, job.DestinationBar);
                        }
                        break;

                    case SuggestedJobKind.LoadAndDeliver:
                        if (job.DestinationBar != null &&
                            SupplyFleetStore.TryGetCarCapacityForLoad(carInfo.Car, job.DestinationBar.Load, out float cap))
                        {
                            EligibleCarCandidate cand = new EligibleCarCandidate(carInfo.Car, cap, 0f, false, 0f, carInfo.LocationArea);
                            totalAssigned += store.DispatchOrders(new[] { cand }, job.DestinationBar, job.SourceOrigin);
                        }
                        break;
                }
            }

            statusBanner = $"Assigned {totalAssigned} suggested job(s).";
            selectedCarIds.Clear();
            RefreshAndRebuild();
        }

        // =========================================================================
        // TAB 1: FLEET BOARD
        // =========================================================================

        private void BuildFleetBoardTab(UIPanelBuilder builder)
        {
            AddFlowToolbar(builder, bar =>
            {
                bar.AddLabel("<size=12><color=#B0BEC5>Group by:</color></size>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
                bar.AddButtonSelectable("Destination", fleetGrouping == FleetGroupingMode.Destination, () => { fleetGrouping = FleetGroupingMode.Destination; RebuildView(); });
                bar.AddButtonSelectable("Work Order", fleetGrouping == FleetGroupingMode.WorkOrder, () => { fleetGrouping = FleetGroupingMode.WorkOrder; RebuildView(); });
                bar.AddButtonSelectable("Location", fleetGrouping == FleetGroupingMode.Location, () => { fleetGrouping = FleetGroupingMode.Location; RebuildView(); });
            });
            builder.AddLabel("<size=10><color=#90A4AE>TL: W=Work T=Train · BL: D=Dest S=Src O=OffMap · TR: *=Suggested !=EmptySrc · Gold bar=Split cut</color></size>");

            if (snapshot.FleetCars.Count == 0)
            {
                builder.AddLabelEmptyState("No supply cars tracked yet. Open 'Manage Fleet' to add cars by range, numbers, or world picker.");
                return;
            }

            List<(string Key, List<SupplyCarInfo> Cars)> groups = snapshot.FleetCars
                .GroupBy(c => fleetGrouping switch
                {
                    FleetGroupingMode.Destination => c.DestinationName,
                    FleetGroupingMode.WorkOrder => c.ActiveOrder != null ? c.ActiveOrder.ShortSummary : "Unassigned (Ready)",
                    _ => c.LocationArea
                })
                .OrderBy(g => IsUnassignedGroupKey(g.Key) ? 0 : 1)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.ToList()))
                .ToList();

            List<(string Key, List<SupplyCarInfo> Cars)> col1Groups = new List<(string, List<SupplyCarInfo>)>();
            List<(string Key, List<SupplyCarInfo> Cars)> col2Groups = new List<(string, List<SupplyCarInfo>)>();
            List<(string Key, List<SupplyCarInfo> Cars)> col3Groups = new List<(string, List<SupplyCarInfo>)>();

            bool useSemanticColumns = fleetGrouping != FleetGroupingMode.Location;
            if (useSemanticColumns)
            {
                foreach ((string key, List<SupplyCarInfo> cars) in groups)
                {
                    int colIndex = ClassifyFleetGroupColumn(key, cars);
                    if (colIndex == 0) col1Groups.Add((key, cars));
                    else if (colIndex == 1) col2Groups.Add((key, cars));
                    else col3Groups.Add((key, cars));
                }
            }
            else
            {
                // Balanced 3-column masonry (shortest-column packing) for Location view
                int[] colHeights = new int[3];
                List<(string Key, List<SupplyCarInfo> Cars)>[] buckets = { col1Groups, col2Groups, col3Groups };

                foreach ((string key, List<SupplyCarInfo> cars) in groups)
                {
                    int shortest = 0;
                    if (colHeights[1] < colHeights[shortest]) shortest = 1;
                    if (colHeights[2] < colHeights[shortest]) shortest = 2;

                    buckets[shortest].Add((key, cars));
                    colHeights[shortest] += 2 + Mathf.CeilToInt(cars.Count / 7f);
                }
            }

            RectTransform rowRt = builder.HStack(columns =>
            {
                columns.ConfigureLayout(12f);

                RectTransform col1 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    if (useSemanticColumns)
                    {
                        v.AddLabel("<size=13><b>1. MOW STOCKPILES & ORDERS</b></size>");
                    }
                    RenderFleetColumnGroups(v, col1Groups, useSemanticColumns ? "No MOW stockpile cars." : null);
                });
                ConfigureColumn(col1);

                RectTransform col2 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    if (useSemanticColumns)
                    {
                        v.AddLabel("<size=13><b>2. COAL, DIESEL & REPAIR</b></size>");
                    }
                    RenderFleetColumnGroups(v, col2Groups, useSemanticColumns ? "No engine/repair supply cars." : null);
                });
                ConfigureColumn(col2);

                RectTransform col3 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    if (useSemanticColumns)
                    {
                        v.AddLabel("<size=13><b>3. WORK TRAINS & UNASSIGNED</b></size>");
                    }
                    RenderFleetColumnGroups(v, col3Groups, useSemanticColumns ? "No work trains or idle cars." : null);
                });
                ConfigureColumn(col3);
            });

            ConfigureTopAlignedRow(rowRt);
        }

        private static bool IsUnassignedGroupKey(string key)
        {
            return string.Equals(key, "Unassigned", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "Unassigned (Ready)", StringComparison.OrdinalIgnoreCase);
        }

        private static int ClassifyFleetGroupColumn(string key, List<SupplyCarInfo> cars)
        {
            if (IsUnassignedGroupKey(key))
            {
                return 2;
            }

            for (int i = 0; i < cars.Count; i++)
            {
                WorkOrder? order = cars[i].ActiveOrder;
                if (order != null)
                {
                    if (order.IsPassiveWorkTrain ||
                        order.DestinationKind == WorkOrderDestinationKind.WorkCar ||
                        order.DestinationKind == WorkOrderDestinationKind.PlannedRepair)
                    {
                        return 2;
                    }
                    if (order.DestinationKind == WorkOrderDestinationKind.EngineTerminal)
                    {
                        return 1;
                    }
                    if (order.DestinationKind == WorkOrderDestinationKind.MowYard)
                    {
                        return 0;
                    }
                }
                else if (cars[i].Shape == SupplyCarShape.WorkCar)
                {
                    return 2;
                }
            }

            string lower = key.ToLowerInvariant();
            if (lower.Contains("work train") || lower.Contains("passive")) return 2;
            if (lower.Contains("engine") || lower.Contains("repair") || lower.Contains("diesel") || lower.Contains("coal")) return 1;
            return 0;
        }

        private void RenderFleetColumnGroups(
            UIPanelBuilder col,
            List<(string Key, List<SupplyCarInfo> Cars)> groups,
            string? emptyMessage)
        {
            if (groups.Count == 0)
            {
                if (!string.IsNullOrEmpty(emptyMessage))
                {
                    col.AddLabel($"<size=11><color=#90A4AE><i>{emptyMessage}</i></color></size>");
                }
                return;
            }

            foreach ((string key, List<SupplyCarInfo> groupCars) in groups)
            {
                List<List<SupplyCarInfo>> strings = snapshot.SplitIntoCoupledStrings(groupCars);
                List<SupplyCarInfo> flatOrdered = strings.SelectMany(s => s).ToList();
                float totalPounds = groupCars.Where(c => c.Units == LoadUnits.Pounds).Sum(c => c.CurrentQuantity);
                string tonsSummary = totalPounds > 0.001f ? $" · {FormatHelper.FormatWeight(totalPounds)}" : string.Empty;

                BuildCardSection(col, $"{key} ({groupCars.Count} cars{tonsSummary})", sec =>
                {
                    CarIconRenderer.RenderCoupledStringStrip(
                        sec,
                        strings,
                        selectedCarIds,
                        onLeftClick: (car, shift, ctrl) => HandleCarLeftClick(car, flatOrdered, shift, ctrl),
                        onDoubleClick: HandleCarDoubleClick,
                        onRightClick: HandleCarRightClick);
                });
            }
        }

        // =========================================================================
        // TAB 2: MOW SUPPLIES (VANILLA 3-COLUMN LAYOUT)
        // =========================================================================

        private void BuildMowSuppliesTab(UIPanelBuilder builder)
        {
            AddFlowToolbar(builder, filterRow =>
            {
                filterRow.AddLabel("<size=12><color=#B0BEC5>Filter:</color></size>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
                filterRow.AddButtonSelectable("All", mowCommodityFilter == "All", () => { mowCommodityFilter = "All"; RebuildView(); });
                filterRow.AddButtonSelectable("Ballast", mowCommodityFilter == "ballast", () => { mowCommodityFilter = "ballast"; RebuildView(); });
                filterRow.AddButtonSelectable("Ties", mowCommodityFilter == "ties", () => { mowCommodityFilter = "ties"; RebuildView(); });
                filterRow.AddButtonSelectable("Rails", mowCommodityFilter == "rail", () => { mowCommodityFilter = "rail"; RebuildView(); });
                filterRow.AddButtonSelectable("Concrete", mowCommodityFilter == "concrete", () => { mowCommodityFilter = "concrete"; RebuildView(); });
                filterRow.AddButtonSelectable("Bridge", mowCommodityFilter == "bridge", () => { mowCommodityFilter = "bridge"; RebuildView(); });
            });

            RectTransform rowRt = builder.HStack(columns =>
            {
                columns.ConfigureLayout(12f);

                // Column 1: Origins
                RectTransform col1 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    v.AddLabel("<size=13><b>1. ORIGINS</b></size> <size=11><color=#90A4AE>(Unlocked)</color></size>");
                    List<SupplyOriginInfo> origins = snapshot.MowOrigins
                        .Where(o => !o.IsMowYardSource)
                        .Where(o => MatchesMowFilter(o.Load))
                        .ToList();
                    BuildGroupedOriginSections(v, origins);
                });
                ConfigureColumn(col1);

                // Column 2: MOW Stockpiles
                RectTransform col2 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    v.AddLabel("<size=13><b>2. MOW STOCKPILES</b></size> <size=11><color=#90A4AE>(MOW Yards)</color></size>");
                    if (snapshot.MowYards.Count == 0)
                    {
                        v.AddLabel("<size=11><color=#90A4AE><i>No unlocked MOW yards.</i></color></size>");
                    }
                    else
                    {
                        foreach (SupplyFacilityNode yard in snapshot.MowYards)
                        {
                            BuildFacilitySection(v, yard, filterMow: true);
                        }
                    }
                });
                ConfigureColumn(col2);

                // Column 3: Projects & Sinks
                RectTransform col3 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    RectTransform col3Hdr = v.HStack(hdr =>
                    {
                        hdr.AddLabel("<size=13><b>3. PROJECTS</b></size>");
                        hdr.Spacer();
                        if (StateManager.HasTrainmasterAccess)
                        {
                            hdr.AddButtonCompact(showNewJobForm ? "Close" : "+ Job", () =>
                            {
                                showNewJobForm = !showNewJobForm;
                                RebuildView();
                            });
                        }
                    });
                    LockHeaderRowHeight(col3Hdr, 28f);

                    if (showNewJobForm)
                    {
                        BuildNewMaintenanceJobForm(v);
                    }

                    if (snapshot.MowSinks.Count == 0)
                    {
                        v.AddLabel("<size=11><color=#90A4AE><i>No active construction projects or maintenance jobs.</i></color></size>");
                    }
                    else
                    {
                        foreach (SupplyProjectSink sink in snapshot.MowSinks)
                        {
                            BuildProjectSinkSection(v, sink);
                        }
                    }
                });
                ConfigureColumn(col3);
            });

            ConfigureTopAlignedRow(rowRt);
        }

        // =========================================================================
        // TAB 3: COAL, DIESEL & REPAIR PARTS (VANILLA 2-COLUMN LAYOUT)
        // =========================================================================

        private void BuildFuelAndRepairTab(UIPanelBuilder builder)
        {
            RectTransform rowRt = builder.HStack(columns =>
            {
                columns.ConfigureLayout(14f);

                RectTransform col1 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    v.AddLabel("<size=13><b>1. ORIGINS</b></size> <size=11><color=#90A4AE>(Coal, Diesel, Parts)</color></size>");
                    BuildGroupedOriginSections(v, snapshot.FuelOrigins);
                });
                ConfigureColumn(col1);

                RectTransform col2 = columns.VStack(v =>
                {
                    v.ConfigureLayout(6f);
                    v.AddLabel("<size=13><b>2. TERMINALS & REPAIR TRACKS</b></size> <size=11><color=#90A4AE>(Targets default to Full)</color></size>");
                    if (snapshot.FuelSinks.Count == 0)
                    {
                        v.AddLabel("<size=11><color=#90A4AE><i>No unlocked engine terminals found.</i></color></size>");
                    }
                    else
                    {
                        foreach (SupplyFacilityNode terminal in snapshot.FuelSinks)
                        {
                            BuildFacilitySection(v, terminal, filterMow: false);
                        }
                    }
                });
                ConfigureColumn(col2);
            });

            ConfigureTopAlignedRow(rowRt);
        }

        // =========================================================================
        // TAB 4: MANAGE FLEET
        // =========================================================================

        private void BuildManageFleetTab(UIPanelBuilder builder)
        {
            if (!StateManager.HasTrainmasterAccess)
            {
                builder.AddLabel("<size=12><color=#FFCC80>Trainmaster access is required to add or remove fleet cars in multiplayer.</color></size>");
            }
            else
            {
                builder.AddSection("Pick Cars in World", sec =>
            {
                sec.HStack(row =>
                {
                    row.ConfigureLayout(8f);
                    bool picking = WorldCarPicker.Shared != null && WorldCarPicker.Shared.IsActive;
                    row.AddButtonCompact(picking ? "Cancel Picker" : "Select Cars in World", () =>
                    {
                        if (WorldCarPicker.Shared == null) return;
                        if (WorldCarPicker.Shared.IsActive)
                        {
                            WorldCarPicker.Shared.Cancel();
                        }
                        else
                        {
                            WorldCarPicker.Shared.BeginPicking(confirmed =>
                            {
                                if (SupplyFleetStore.Shared != null && confirmed.Count > 0)
                                {
                                    int added = SupplyFleetStore.Shared.AddCars(confirmed);
                                    statusBanner = $"Added {added} car(s) picked from the world.";
                                }
                                RefreshAndRebuild();
                            });
                        }
                    });
                    row.AddLabel("<size=12><color=#B0BEC5>Click owned cars in the world (Shift+Click selects coupled cut), then Confirm.</color></size>");
                });
            });

            builder.AddSection("Add by Number Range", sec =>
            {
                sec.HStack(row =>
                {
                    row.ConfigureLayout(6f);
                    row.AddLabel("<size=12>Mark:</size>");
                    row.AddInputField(rangeMarkInput, v => rangeMarkInput = v, "Mark").Width(72f);
                    row.AddLabel("<size=12>From:</size>");
                    row.AddInputField(rangeStartInput, v => rangeStartInput = v, "Start").Width(78f);
                    row.AddLabel("<size=12>To:</size>");
                    row.AddInputField(rangeEndInput, v => rangeEndInput = v, "End").Width(78f);
                    row.AddButtonCompact("Add Range", () =>
                    {
                        if (SupplyFleetStore.Shared == null) return;
                        if (int.TryParse(rangeStartInput, out int start) && int.TryParse(rangeEndInput, out int end))
                        {
                            int added = SupplyFleetStore.Shared.AddCarsByNumberRange(rangeMarkInput, start, end);
                            statusBanner = added > 0
                                ? $"Added {added} car(s) from {rangeMarkInput} {start}-{end}."
                                : $"No new eligible cars found in {rangeMarkInput} {start}-{end}.";
                            RefreshAndRebuild();
                        }
                    });
                    row.Spacer();
                });
            });

            builder.AddSection("Add by Car Numbers", sec =>
            {
                sec.HStack(row =>
                {
                    row.ConfigureLayout(6f);
                    row.AddInputField(numberListInput, v => numberListInput = v, "Comma-separated car numbers (e.g. 4001, 4002, SYT 4010)").FlexibleWidth();
                    row.AddButtonCompact("Add Cars", () =>
                    {
                        if (SupplyFleetStore.Shared == null) return;
                        int added = SupplyFleetStore.Shared.AddCarsByNumberList(numberListInput);
                        statusBanner = added > 0
                            ? $"Added {added} car(s) to supply fleet."
                            : "No matching owned cars found.";
                        numberListInput = string.Empty;
                        RefreshAndRebuild();
                    });
                });
            });
            }

            builder.AddSection($"Tracked Supply Fleet ({snapshot.FleetCars.Count} cars)", sec =>
            {
                if (snapshot.FleetCars.Count == 0)
                {
                    sec.AddLabel("<size=12><color=#90A4AE><i>No cars tracked yet.</i></color></size>");
                }
                else
                {
                    List<List<SupplyCarInfo>> strings = snapshot.SplitIntoCoupledStrings(snapshot.FleetCars);
                    List<SupplyCarInfo> flatOrdered = strings.SelectMany(s => s).ToList();
                    CarIconRenderer.RenderCoupledStringStrip(
                        sec,
                        strings,
                        selectedCarIds,
                        onLeftClick: (car, shift, ctrl) => HandleCarLeftClick(car, flatOrdered, shift, ctrl),
                        onDoubleClick: HandleCarDoubleClick,
                        onRightClick: HandleCarRightClick);
                }
            });
        }

        // =========================================================================
        // Vanilla Section Content Builders
        // =========================================================================

        private void BuildGroupedOriginSections(UIPanelBuilder parent, List<SupplyOriginInfo> origins)
        {
            if (origins.Count == 0)
            {
                parent.AddLabel("<size=11><color=#90A4AE><i>No unlocked origins.</i></color></size>");
                return;
            }

            IEnumerable<IGrouping<string, SupplyOriginInfo>> grouped = origins
                .GroupBy(
                    o => o.IsOffMapSource
                        ? $"__interchange__{o.AreaName}"
                        : (o.Industry != null ? o.Industry.identifier : o.Identifier),
                    StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, SupplyOriginInfo> group in grouped)
            {
                List<SupplyOriginInfo> items = group.ToList();
                SupplyOriginInfo first = items[0];
                string title = first.IsOffMapSource
                    ? $"{first.AreaName} Interchange"
                    : (first.Industry != null ? first.Industry.DisplayName : first.DisplayName);
                string badge = first.IsOffMapSource ? "Interchange" : first.KindLabel;
                string suppliesList = string.Join(", ", items.Select(i => i.Load.description).Distinct(StringComparer.OrdinalIgnoreCase));
                List<SupplyCarInfo> activeCars = items.SelectMany(i => i.ActiveCars).Distinct().ToList();

                BuildCardSection(parent, $"{title}  <size=10><color=#90A4AE>[{badge}]</color></size>", sec =>
                {
                    sec.AddLabel($"<size=11><color=#CFD8DC>{suppliesList}</color></size>");
                    if (activeCars.Count > 0)
                    {
                        List<List<SupplyCarInfo>> strings = snapshot.SplitIntoCoupledStrings(activeCars);
                        List<SupplyCarInfo> flatOrdered = strings.SelectMany(s => s).ToList();
                        CarIconRenderer.RenderCoupledStringStrip(
                            sec,
                            strings,
                            selectedCarIds,
                            onLeftClick: (car, shift, ctrl) => HandleCarLeftClick(car, flatOrdered, shift, ctrl),
                            onDoubleClick: HandleCarDoubleClick,
                            onRightClick: HandleCarRightClick);
                    }
                });
            }
        }

        private void BuildFacilitySection(UIPanelBuilder parent, SupplyFacilityNode node, bool filterMow)
        {
            List<SupplyCommodityBar> visibleBars = filterMow
                ? node.Bars.Where(b => MatchesMowFilter(b.Load)).ToList()
                : node.Bars;

            if (visibleBars.Count == 0) return;

            string swatchHex = ColorUtility.ToHtmlStringRGB(node.AreaColor);
            bool isEditingTargets = string.Equals(editingTargetsFacilityId, node.FacilityId, StringComparison.OrdinalIgnoreCase);

            BuildCardSection(parent, $"<color=#{swatchHex}>■</color> {node.DisplayName}", sec =>
            {
                RectTransform hdrRt = sec.HStack(hdr =>
                {
                    hdr.ConfigureLayout(4f);
                    if (node.HasRepairAlert && !string.IsNullOrEmpty(node.AlertText))
                    {
                        hdr.AddLabel($"<size=11><color=#EF5350><b>{node.AlertText}</b></color></size>");
                    }
                    hdr.Spacer();
                    if (StateManager.HasTrainmasterAccess)
                    {
                        hdr.AddButtonCompact(isEditingTargets ? "Done" : "Targets", () => ToggleFacilityTargetEditor(node, visibleBars));
                    }
                    hdr.AddButtonCompact("Open", () => FacilityWindow.Open(node.Industry));
                });
                LockHeaderRowHeight(hdrRt, 26f);

                foreach (SupplyCommodityBar bar in visibleBars)
                {
                    BuildClickableCommodityBar(sec, bar);
                    if (isEditingTargets && bar.IsTargetEditable)
                    {
                        BuildInlineTargetInput(sec, bar);
                    }
                }

                if (node.ActiveCars.Count > 0)
                {
                    sec.Spacer(3f);
                    List<List<SupplyCarInfo>> strings = snapshot.SplitIntoCoupledStrings(node.ActiveCars);
                    List<SupplyCarInfo> flatOrdered = strings.SelectMany(s => s).ToList();
                    CarIconRenderer.RenderCoupledStringStrip(
                        sec,
                        strings,
                        selectedCarIds,
                        onLeftClick: (car, shift, ctrl) => HandleCarLeftClick(car, flatOrdered, shift, ctrl),
                        onDoubleClick: HandleCarDoubleClick,
                        onRightClick: HandleCarRightClick);
                }
            });
        }

        private void ToggleFacilityTargetEditor(SupplyFacilityNode node, List<SupplyCommodityBar> bars)
        {
            if (string.Equals(editingTargetsFacilityId, node.FacilityId, StringComparison.OrdinalIgnoreCase))
            {
                editingTargetsFacilityId = null;
                pendingTargetTexts.Clear();
                RefreshAndRebuild();
                return;
            }

            editingTargetsFacilityId = node.FacilityId;
            pendingTargetTexts.Clear();
            foreach (SupplyCommodityBar bar in bars)
            {
                float displayVal = bar.HasTarget
                    ? (bar.Load.units == LoadUnits.Pounds ? bar.TargetAmount / FormatHelper.ShortTonInPounds : bar.TargetAmount)
                    : 0f;
                pendingTargetTexts[bar.Load.id] = displayVal > 0.001f ? displayVal.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty;
            }
            RebuildView();
        }

        private void BuildInlineTargetInput(UIPanelBuilder parent, SupplyCommodityBar bar)
        {
            string unitLabel = bar.Load.units == LoadUnits.Pounds ? "T" : "gal";
            string currentText = pendingTargetTexts.TryGetValue(bar.Load.id, out string saved) ? saved : string.Empty;

            parent.HStack(edit =>
            {
                edit.ConfigureLayout(4f);
                edit.AddLabel($"<size=11><color=#B0BEC5>Target ({unitLabel}):</color></size>");
                edit.AddInputField(currentText, v => pendingTargetTexts[bar.Load.id] = v, $"None ({unitLabel})").Width(72f);
                edit.AddButtonCompact("Set", () =>
                {
                    if (SupplyFleetStore.Shared == null) return;
                    if (pendingTargetTexts.TryGetValue(bar.Load.id, out string valStr) &&
                        float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
                        parsed > 0.001f)
                    {
                        float nativeVal = bar.Load.units == LoadUnits.Pounds ? parsed * FormatHelper.ShortTonInPounds : parsed;
                        SupplyFleetStore.Shared.SetTargetStock(bar, nativeVal);
                    }
                    else
                    {
                        SupplyFleetStore.Shared.SetTargetStock(bar, 0f);
                    }
                    RefreshAndRebuild();
                });
                edit.Spacer();
            });
        }

        private void BuildProjectSinkSection(UIPanelBuilder parent, SupplyProjectSink sink)
        {
            string swatchHex = ColorUtility.ToHtmlStringRGB(sink.AreaColor);
            string progressTag = sink.IsConstructionMilestone
                ? $" <size=11><color=#FFD54F>({sink.ProgressRatio * 100f:F0}%)</color></size>"
                : " <size=11><color=#90A4AE>(Job)</color></size>";

            BuildCardSection(parent, $"<color=#{swatchHex}>■</color> {sink.DisplayName}{progressTag}", sec =>
            {
                if (!sink.IsConstructionMilestone)
                {
                    sec.HStack(hdr =>
                    {
                        hdr.AddLabel($"<size=11><color=#90A4AE>{sink.StagingLabel}</color></size>");
                        hdr.Spacer();
                        hdr.AddButtonCompact("Remove", () =>
                        {
                            SupplyFleetStore.Shared?.RemovePlannedRepair(sink);
                            RefreshAndRebuild();
                        });
                    });
                }
                else
                {
                    ProgressBarFactory.CreateMowBar(sec, sink.ProgressRatio);

                    if (snapshot.AvailableWorkCars.Count > 0)
                    {
                        List<string> workCarLabels = new List<string> { "Work Car: (None)" };
                        int currentIdx = 0;
                        for (int i = 0; i < snapshot.AvailableWorkCars.Count; i++)
                        {
                            Car wc = snapshot.AvailableWorkCars[i];
                            workCarLabels.Add($"Work Car: {wc.DisplayName}");
                            if (string.Equals(wc.id, sink.AssignedWorkCarId, StringComparison.Ordinal))
                            {
                                currentIdx = i + 1;
                            }
                        }

                        sec.AddDropdown(workCarLabels, currentIdx, idx =>
                        {
                            Car? chosenCar = idx > 0 ? snapshot.AvailableWorkCars[idx - 1] : null;
                            SupplyFleetStore.Shared?.SetAssignedWorkCar(sink, chosenCar);
                            RefreshAndRebuild();
                        });
                    }
                    else
                    {
                        sec.AddLabel($"<size=11><color=#90A4AE>{sink.StagingLabel}</color></size>");
                    }
                }

                List<SupplyCommodityBar> visibleBars = sink.Bars.Where(b => MatchesMowFilter(b.Load)).ToList();
                foreach (SupplyCommodityBar bar in visibleBars)
                {
                    BuildClickableCommodityBar(sec, bar);
                }

                if (sink.ActiveCars.Count > 0)
                {
                    sec.Spacer(3f);
                    List<List<SupplyCarInfo>> strings = snapshot.SplitIntoCoupledStrings(sink.ActiveCars);
                    List<SupplyCarInfo> flatOrdered = strings.SelectMany(s => s).ToList();
                    CarIconRenderer.RenderCoupledStringStrip(
                        sec,
                        strings,
                        selectedCarIds,
                        onLeftClick: (car, shift, ctrl) => HandleCarLeftClick(car, flatOrdered, shift, ctrl),
                        onDoubleClick: HandleCarDoubleClick,
                        onRightClick: HandleCarRightClick);
                }
            });
        }

        private void BuildClickableCommodityBar(UIPanelBuilder parent, SupplyCommodityBar bar)
        {
            RectTransform barContainer = parent.VStack(v =>
            {
                v.ConfigureLayout(2f);

                if (bar.IsSinkDemandOnly)
                {
                    string needText = FormatHelper.FormatQuantity(bar.RemainingDemand, bar.Load.units);
                    string onTrainText = bar.DeliveryAmount > 0.001f
                        ? $" <color=#66BB6A>(+{FormatHelper.FormatQuantity(bar.DeliveryAmount, bar.Load.units)} on train)</color>"
                        : string.Empty;
                    string inboundText = bar.FleetInboundAmount > 0.001f
                        ? $" <color=#81C784>({FormatHelper.FormatQuantity(bar.FleetInboundAmount, bar.Load.units)} ordered)</color>"
                        : string.Empty;
                    v.AddLabel($"<size=11><b>{bar.Load.description}</b>: {needText} needed{onTrainText}{inboundText}</size>");

                    float deliveryRatio = bar.RemainingDemand > 0.001f
                        ? Mathf.Clamp01(bar.DeliveryAmount / bar.RemainingDemand)
                        : 0f;
                    float orderedRatio = bar.RemainingDemand > 0.001f
                        ? Mathf.Clamp01(bar.FleetInboundAmount / bar.RemainingDemand)
                        : 0f;
                    ProgressBarFactory.CreateMowBar(v, stockRatio: 0f, deliveryRatio: deliveryRatio, outgoingRatio: 0f, committedRatio: 0f, inboundRatio: orderedRatio);
                }
                else
                {
                    string curText = FormatHelper.FormatQuantity(bar.CurrentAmount, bar.Load.units);
                    string onTrackPart = bar.DeliveryAmount > 0.001f
                        ? $" <color=#66BB6A>(+{FormatHelper.FormatQuantity(bar.DeliveryAmount, bar.Load.units)} on track)</color>"
                        : string.Empty;
                    string maxText = FormatHelper.FormatQuantity(bar.MaxCapacity, bar.Load.units);
                    string targetSuffix = string.Empty;
                    string deltaMarkup = string.Empty;

                    if (bar.HasTarget && bar.TargetAmount > 0.001f)
                    {
                        string tgtText = FormatHelper.FormatQuantity(bar.TargetAmount, bar.Load.units);
                        float delta = (bar.CurrentAmount + bar.DeliveryAmount + bar.FleetInboundAmount - bar.TotalOutboundAmount) - bar.TargetAmount;
                        bool met = delta >= -0.001f;
                        string deltaColor = met ? "#66BB6A" : "#FFB74D";
                        targetSuffix = $" <color={deltaColor}>(Tgt {tgtText})</color>";

                        string formattedAbs = FormatHelper.FormatQuantity(Mathf.Abs(delta), bar.Load.units);
                        deltaMarkup = met
                            ? $"<color=#66BB6A>+{formattedAbs}</color>"
                            : $"<color=#FFB74D>-{formattedAbs}</color>";
                    }
                    else
                    {
                        targetSuffix = " <color=#78909C>(No Tgt)</color>";
                    }

                    RectTransform labelRow = v.HStack(hdr =>
                    {
                        hdr.ConfigureLayout(4f);
                        RectTransform leftRt = hdr.AddLabel($"<size=11><b>{bar.Load.description}</b>: {curText}{onTrackPart} / {maxText}{targetSuffix}</size>");
                        if (leftRt != null)
                        {
                            LayoutElement leftLe = leftRt.GetComponent<LayoutElement>() ?? leftRt.gameObject.AddComponent<LayoutElement>();
                            leftLe.minWidth = 0f;
                            leftLe.flexibleWidth = 1f;
                        }

                        if (!string.IsNullOrEmpty(deltaMarkup))
                        {
                            RectTransform rightRt = hdr.AddLabel($"<size=11><b>{deltaMarkup}</b></size>", t => t.textWrappingMode = TextWrappingModes.NoWrap);
                            if (rightRt != null)
                            {
                                LayoutElement rightLe = rightRt.GetComponent<LayoutElement>() ?? rightRt.gameObject.AddComponent<LayoutElement>();
                                rightLe.flexibleWidth = 0f;
                            }
                        }
                    });
                    if (labelRow != null)
                    {
                        HorizontalLayoutGroup? hlg = labelRow.GetComponent<HorizontalLayoutGroup>();
                        if (hlg != null)
                        {
                            hlg.childControlWidth = true;
                            hlg.childControlHeight = true;
                            hlg.childForceExpandWidth = false;
                            hlg.childForceExpandHeight = false;
                            hlg.childAlignment = TextAnchor.LowerLeft;
                        }
                        LayoutElement rowLe = labelRow.GetComponent<LayoutElement>() ?? labelRow.gameObject.AddComponent<LayoutElement>();
                        rowLe.flexibleHeight = 0f;
                    }

                    ProgressBarFactory.CreateMowBar(
                        v,
                        stockRatio: bar.StockRatio,
                        deliveryRatio: bar.DeliveryRatio,
                        outgoingRatio: bar.OutgoingRatio,
                        committedRatio: bar.CommittedOutboundRatio,
                        inboundRatio: bar.FleetInboundRatio,
                        targetRatio: bar.TargetRatio,
                        storageCapRatio: bar.StorageCapRatio);

                    if (bar.OutgoingAmount > 0.001f)
                    {
                        v.AddLabel($"<size=10><color=#FF9800>-{FormatHelper.FormatQuantity(bar.OutgoingAmount, bar.Load.units)} outgoing</color></size>");
                    }
                    if (bar.CommittedOutboundAmount > 0.001f)
                    {
                        v.AddLabel($"<size=10><color=#FF9800>-{FormatHelper.FormatQuantity(bar.CommittedOutboundAmount, bar.Load.units)} expected outgoing</color></size>");
                    }
                    if (bar.NonFleetInboundAmount > 0.001f)
                    {
                        v.AddLabel($"<size=10><color=#90A4AE>+{FormatHelper.FormatQuantity(bar.NonFleetInboundAmount, bar.Load.units)} non-fleet en-route</color></size>");
                    }
                }
            });

            if (barContainer != null && StateManager.HasTrainmasterAccess)
            {
                Image hitTarget = barContainer.gameObject.GetComponent<Image>() ?? barContainer.gameObject.AddComponent<Image>();
                hitTarget.color = new Color(0f, 0f, 0f, 0f);
                hitTarget.raycastTarget = true;

                ClickableBarHandler clicker = barContainer.gameObject.AddComponent<ClickableBarHandler>();
                clicker.OnClick = () => OrderCarWindow.Open(bar, snapshot, RefreshAndRebuild);

                UITooltipProvider tt = barContainer.gameObject.AddComponent<UITooltipProvider>();
                tt.TooltipInfo = new TooltipInfo(
                    $"Order {bar.Load.description}",
                    $"Click to dispatch supply cars for {bar.Load.description} to {bar.FacilityDisplayName}.");
            }
        }

        private void BuildNewMaintenanceJobForm(UIPanelBuilder parent)
        {
            BuildCardSection(parent, "New Maintenance Job (Tons)", sec =>
            {
                sec.AddInputField(newJobLabel, v => newJobLabel = v, "Job Label").FlexibleWidth();

                List<string> workCarOpts = new List<string> { "Work Car: (None)" };
                foreach (Car wc in snapshot.AvailableWorkCars)
                {
                    workCarOpts.Add($"Work Car: {wc.DisplayName}");
                }
                newJobWorkCarIndex = Mathf.Clamp(newJobWorkCarIndex, 0, workCarOpts.Count - 1);
                sec.AddDropdown(workCarOpts, newJobWorkCarIndex, idx => newJobWorkCarIndex = idx);

                RectTransform r2Rt = sec.HStack(r2 =>
                {
                    r2.ConfigureLayout(4f);
                    r2.AddLabel("<size=11>Bal:</size>");
                    r2.AddInputField(newJobBallastTons, v => newJobBallastTons = v, "0").Width(54f);
                    r2.AddLabel("<size=11>Ties:</size>");
                    r2.AddInputField(newJobTiesTons, v => newJobTiesTons = v, "0").Width(54f);
                    r2.AddLabel("<size=11>Rail:</size>");
                    r2.AddInputField(newJobRailTons, v => newJobRailTons = v, "0").Width(54f);
                });
                ConfigureCompactRow(r2Rt);

                RectTransform r3Rt = sec.HStack(r3 =>
                {
                    r3.Spacer();
                    r3.AddButtonCompact("Create Job", () =>
                    {
                        if (SupplyFleetStore.Shared == null) return;
                        PlannedRepairDemand demand = new PlannedRepairDemand
                        {
                            Label = string.IsNullOrWhiteSpace(newJobLabel) ? "Maintenance Job" : newJobLabel.Trim(),
                            StagingWorkCarId = newJobWorkCarIndex > 0 && newJobWorkCarIndex - 1 < snapshot.AvailableWorkCars.Count
                                ? snapshot.AvailableWorkCars[newJobWorkCarIndex - 1].id
                                : null
                        };

                        TryAddTonDemand(demand, "rr/ballast", newJobBallastTons);
                        TryAddTonDemand(demand, "rr/ties", newJobTiesTons);
                        TryAddTonDemand(demand, "rr/rails", newJobRailTons);

                        SupplyFleetStore.Shared.AddOrUpdatePlannedRepair(demand);
                        showNewJobForm = false;
                        statusBanner = $"Created maintenance job '{demand.Label}'.";
                        RefreshAndRebuild();
                    });
                });
                ConfigureCompactRow(r3Rt);
            });
        }

        private static void ConfigureCompactRow(RectTransform? rowRt)
        {
            if (rowRt == null) return;
            HorizontalLayoutGroup? hlg = rowRt.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandHeight = false;
                hlg.childControlHeight = true;
            }
            LayoutElement le = rowRt.gameObject.GetComponent<LayoutElement>() ?? rowRt.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 0f;
        }

        private static void TryAddTonDemand(PlannedRepairDemand demand, string loadId, string tonsText)
        {
            if (float.TryParse(tonsText, NumberStyles.Float, CultureInfo.InvariantCulture, out float tons) && tons > 0.001f)
            {
                demand.LoadDemands[loadId] = tons * FormatHelper.ShortTonInPounds;
            }
        }

        private static void LockHeaderRowHeight(RectTransform? rowRt, float height)
        {
            if (rowRt == null) return;
            rowRt.ChildrenKeepHeight();
            LayoutElement le = rowRt.GetComponent<LayoutElement>() ?? rowRt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        private static void BuildCardSection(UIPanelBuilder parent, string title, Action<UIPanelBuilder> content)
        {
            RectTransform cardRt = parent.VStack(v =>
            {
                v.ConfigureLayout(4f);
                v.AddSection(title, content);
            });

            if (cardRt == null) return;
            StyleCardContainer(cardRt, new RectOffset(8, 8, 6, 8), TextAnchor.UpperLeft);

            // Ensure AddSection's inner VStack also controls and expands child widths to match the card
            for (int i = 0; i < cardRt.childCount; i++)
            {
                VerticalLayoutGroup? innerVlg = cardRt.GetChild(i).GetComponent<VerticalLayoutGroup>();
                if (innerVlg != null)
                {
                    innerVlg.childControlWidth = true;
                    innerVlg.childForceExpandWidth = true;
                    innerVlg.childForceExpandHeight = false;
                }
            }
        }

        private static void StyleCardContainer(RectTransform? cardRt, RectOffset padding, TextAnchor alignment, float minHeight = 0f)
        {
            if (cardRt == null) return;

            LayoutElement le = cardRt.gameObject.GetComponent<LayoutElement>() ?? cardRt.gameObject.AddComponent<LayoutElement>();
            if (minHeight > 0f)
            {
                le.minHeight = minHeight;
            }
            le.flexibleHeight = 0f;

            Image bg = cardRt.gameObject.GetComponent<Image>() ?? cardRt.gameObject.AddComponent<Image>();
            bg.color = CardBgColor;
            bg.raycastTarget = false;

            Outline outline = cardRt.gameObject.GetComponent<Outline>() ?? cardRt.gameObject.AddComponent<Outline>();
            outline.effectColor = CardOutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup? vlg = cardRt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.padding = padding;
                vlg.childAlignment = alignment;
                vlg.childForceExpandHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
            }
        }

        private static void ConfigureColumn(RectTransform? colRt)
        {
            if (colRt == null) return;
            colRt.EqualFlexibleWidth();

            LayoutElement le = colRt.GetComponent<LayoutElement>() ?? colRt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;

            VerticalLayoutGroup? vlg = colRt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childForceExpandHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
            }
        }

        private static void ConfigureTopAlignedRow(RectTransform? rowRt)
        {
            if (rowRt == null) return;

            HorizontalLayoutGroup? hlg = rowRt.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.UpperLeft;
                hlg.childForceExpandHeight = false;
                hlg.childForceExpandWidth = true;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
            }
        }

        // =========================================================================
        // Car Icon Click / Multi-Select / Right-Click Handlers
        // =========================================================================

        private void HandleCarLeftClick(SupplyCarInfo car, IReadOnlyList<SupplyCarInfo> groupList, bool shift, bool ctrl)
        {
            if (shift && !string.IsNullOrEmpty(lastClickedCarId))
            {
                int startIdx = -1;
                int endIdx = -1;
                for (int i = 0; i < groupList.Count; i++)
                {
                    if (groupList[i].CarId == lastClickedCarId) startIdx = i;
                    if (groupList[i].CarId == car.CarId) endIdx = i;
                }

                if (startIdx >= 0 && endIdx >= 0)
                {
                    int lo = Mathf.Min(startIdx, endIdx);
                    int hi = Mathf.Max(startIdx, endIdx);
                    for (int i = lo; i <= hi; i++)
                    {
                        selectedCarIds.Add(groupList[i].CarId);
                    }
                    RebuildView();
                    return;
                }
            }

            if (ctrl)
            {
                if (!selectedCarIds.Add(car.CarId))
                {
                    selectedCarIds.Remove(car.CarId);
                }
            }
            else
            {
                if (selectedCarIds.Count == 1 && selectedCarIds.Contains(car.CarId))
                {
                    selectedCarIds.Clear();
                }
                else
                {
                    selectedCarIds.Clear();
                    selectedCarIds.Add(car.CarId);
                }
            }

            lastClickedCarId = car.CarId;
            RebuildView();
        }

        private void HandleCarDoubleClick(SupplyCarInfo car)
        {
            if (car?.Car != null)
            {
                CarInspector.Show(car.Car);
            }
        }

        private void HandleCarRightClick(SupplyCarInfo car)
        {
            if (car == null) return;

            if (!selectedCarIds.Contains(car.CarId))
            {
                selectedCarIds.Clear();
                selectedCarIds.Add(car.CarId);
            }
            lastClickedCarId = car.CarId;
            RebuildView();
        }

        private bool MatchesMowFilter(Load? load)
        {
            if (load == null || mowCommodityFilter == "All") return true;
            return (load.id ?? string.Empty).IndexOf(mowCommodityFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
