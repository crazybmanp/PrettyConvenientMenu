using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.Definition.Data;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.SupplyFleet;
using PrettyConvenientMenu.UI.Common;
using UI.Builder;
using UI.Windows;
using UnityEngine;

namespace PrettyConvenientMenu.UI.SupplyFleet
{
    /// <summary>
    /// Tonnage-driven bulk Work Order dispatch dialog opened by clicking any commodity bar
    /// in <see cref="SupplyFleetWindow"/>. Creates one explicit per-car Work Order for each selected car.
    /// </summary>
    [RequireComponent(typeof(Window))]
    public sealed class OrderCarWindow : BuilderWindowBase<OrderCarWindow>
    {
        private static readonly Vector2Int DefaultWindowSize = new Vector2Int(640, 540);
        private static readonly Vector2Int MinWindowSize = new Vector2Int(520, 420);

        private static OrderCarWindow? activeWindow;

        public override string Identifier => "PrettyConvenientMenu.OrderCar";
        public override WindowConfig Config => WindowConfig.Resizable(DefaultWindowSize, MinWindowSize);

        private SupplyCommodityBar? targetBar;
        private List<SupplyOriginInfo> availableSources = new List<SupplyOriginInfo>();
        private int selectedSourceIndex;
        private float targetQuantityNative;
        private string targetInputText = string.Empty;
        private List<EligibleCarCandidate> candidates = new List<EligibleCarCandidate>();
        private readonly HashSet<string> selectedCarIds = new HashSet<string>(StringComparer.Ordinal);
        private Action? onDispatchedCallback;

        public static void Open(SupplyCommodityBar bar, SupplyNetworkSnapshot snapshot, Action? onDispatched = null)
        {
            if (bar == null || bar.Load == null || WindowManager.Shared == null)
            {
                return;
            }

            try
            {
                if (activeWindow == null || activeWindow.Window == null)
                {
                    activeWindow = WindowManager.Shared.CreateWindowWithComponent<OrderCarWindow>();
                }

                activeWindow.InitializeForBar(bar, snapshot, onDispatched);
                activeWindow.Window?.OrderFront();
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to open OrderCarWindow.");
            }
        }

        private void InitializeForBar(SupplyCommodityBar bar, SupplyNetworkSnapshot snapshot, Action? onDispatched)
        {
            targetBar = bar;
            onDispatchedCallback = onDispatched;
            Title = $"Order {bar.Load.description} -> {bar.FacilityDisplayName}";

            availableSources = snapshot.FindSourcesFor(bar);

            selectedSourceIndex = 0;
            RefreshCandidates(autoSelect: false);
            ApplyDefaultTargetForCurrentSource();
            AutoSelectCandidates();
            Show();
        }

        /// <summary>
        /// Sets the default target amount to the lowest of target/demand, available source transfer stock,
        /// and available eligible fleet capacity, clamped to >= 0.
        /// </summary>
        private void ApplyDefaultTargetForCurrentSource()
        {
            if (targetBar == null || targetBar.Load == null)
            {
                return;
            }

            float baseDemand;
            if (targetBar.IsSinkDemandOnly)
            {
                baseDemand = Mathf.Max(0f, targetBar.RemainingDemand - targetBar.DeliveryAmount - targetBar.FleetInboundAmount);
            }
            else if (targetBar.HasTarget)
            {
                baseDemand = Mathf.Max(0f, targetBar.RemainingDemand);
            }
            else
            {
                baseDemand = Mathf.Max(0f, targetBar.Load.NominalQuantityPerCarLoad);
            }

            if (availableSources.Count > 0 && selectedSourceIndex >= 0 && selectedSourceIndex < availableSources.Count)
            {
                SupplyOriginInfo src = availableSources[selectedSourceIndex];
                if (src.IsMowYardSource)
                {
                    // Transfer from another MOW yard: cap default to what is currently present (user can override higher)
                    baseDemand = Mathf.Min(baseDemand, Mathf.Max(0f, src.CurrentBuffer));
                }
                else if (!src.IsOffMapSource && src.CurrentBuffer > 0.001f)
                {
                    baseDemand = Mathf.Min(baseDemand, src.CurrentBuffer);
                }
            }

            float availableFleet = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                availableFleet += candidates[i].EffectiveQuantity;
            }

            baseDemand = Mathf.Max(0f, Mathf.Min(baseDemand, availableFleet));

            targetInputText = FormatInputValue(baseDemand, targetBar.Load.units);
            targetQuantityNative = TryParseInputValue(targetInputText, targetBar.Load.units, out float snapped)
                ? snapped
                : baseDemand;
        }

        private void AutoSelectCandidates()
        {
            selectedCarIds.Clear();
            if (targetQuantityNative <= 0.001f)
            {
                return;
            }

            foreach (string id in SupplyFleetStore.AutoSelectCandidatesForTarget(candidates, targetQuantityNative))
            {
                selectedCarIds.Add(id);
            }
        }

        private void RefreshCandidates(bool autoSelect)
        {
            candidates.Clear();
            SupplyFleetStore? store = SupplyFleetStore.Shared;
            if (store == null || targetBar?.Load == null)
            {
                return;
            }

            SupplyOriginInfo? chosenSource = (availableSources.Count > 0 && selectedSourceIndex >= 0 && selectedSourceIndex < availableSources.Count)
                ? availableSources[selectedSourceIndex]
                : null;

            candidates = store.FindEligibleUnassignedCars(targetBar, chosenSource);

            if (autoSelect)
            {
                AutoSelectCandidates();
            }
            else
            {
                HashSet<string> validIds = new HashSet<string>(candidates.Select(c => c.Car.id), StringComparer.Ordinal);
                selectedCarIds.IntersectWith(validIds);
            }
        }

        protected override void OnWindowDisable()
        {
            base.OnWindowDisable();
            if (activeWindow == this)
            {
                activeWindow = null;
            }
            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        protected override void BuildContent(UIPanelBuilder builder)
        {
            if (targetBar == null || targetBar.Load == null)
            {
                builder.AddLabel("No commodity selected.");
                return;
            }

            Load load = targetBar.Load;

            builder.VScrollView(scroll =>
            {
                scroll.AddTitle($"Order {load.description}", $"Destination: {targetBar.FacilityDisplayName}");

                if (!targetBar.DestinationPosition.HasValue)
                {
                    scroll.AddLabel("<color=#EF5350><b>No base-game destination assigned.</b> Assign an MOW Work Car to this project first (or use General Waybilling staging once available).</color>");
                    return;
                }

                // 1. Source Selection
                scroll.AddSection("1. Source (Fill From)", sec =>
                {
                    if (availableSources.Count == 0)
                    {
                        sec.AddLabel("<color=#FFB74D>No on-map or interchange sources found for this commodity (only already-loaded cars can be dispatched).</color>");
                    }
                    else
                    {
                        List<string> labels = availableSources.Select(s => s.FormatDropdownLabel()).ToList();

                        sec.AddDropdown(labels, Mathf.Clamp(selectedSourceIndex, 0, labels.Count - 1), idx =>
                        {
                            selectedSourceIndex = idx;
                            RefreshCandidates(autoSelect: false);
                            ApplyDefaultTargetForCurrentSource();
                            AutoSelectCandidates();
                            RebuildPanel();
                        });
                    }
                });

                // 2. Target Quantity (US Short Tons / Gallons)
                string unitSuffix = load.units == LoadUnits.Pounds ? "T" : (load.units == LoadUnits.Gallons ? "gal" : "pcs");
                scroll.AddSection($"2. Target Amount ({unitSuffix})", sec =>
                {
                    sec.HStack(row =>
                    {
                        row.ConfigureLayout(6f);
                        row.AddInputField(targetInputText, val =>
                        {
                            targetInputText = val;
                            if (TryParseInputValue(val, load.units, out float parsed))
                            {
                                targetQuantityNative = Mathf.Max(0f, parsed);
                                RefreshCandidates(autoSelect: true);
                                RebuildPanel();
                            }
                        }, $"Target ({unitSuffix})").Width(120f);

                        row.AddButtonCompact("Auto-Select", () =>
                        {
                            RefreshCandidates(autoSelect: true);
                            RebuildPanel();
                        });
                        row.Spacer();
                    });
                });

                // 3. Eligible Unassigned Cars Table
                float selectedTotal = 0f;
                int selectedCount = 0;
                foreach (EligibleCarCandidate c in candidates)
                {
                    if (selectedCarIds.Contains(c.Car.id))
                    {
                        selectedTotal += c.EffectiveQuantity;
                        selectedCount++;
                    }
                }

                string coverageColor = selectedTotal >= targetQuantityNative - 0.001f ? "#66BB6A" : "#FFB74D";
                string summaryLine = $"Selected: <color={coverageColor}><b>{FormatHelper.FormatQuantity(selectedTotal, load.units)}</b></color> / {FormatHelper.FormatQuantity(targetQuantityNative, load.units)} ({selectedCount} cars)";

                scroll.AddSection($"3. Eligible Unassigned Fleet ({candidates.Count} available)", sec =>
                {
                    sec.AddLabel(summaryLine);
                    sec.Spacer(4f);

                    if (candidates.Count == 0)
                    {
                        sec.AddLabel("<color=#90A4AE><i>No unassigned compatible cars available in your tracked fleet. Add cars in the Manage Fleet tab or wait for active orders to finish.</i></color>");
                        return;
                    }

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        EligibleCarCandidate cand = candidates[i];
                        bool isChecked = selectedCarIds.Contains(cand.Car.id);
                        string stateTag = cand.IsAlreadyLoaded
                            ? "<color=#66BB6A><b>LOADED (Skip Source)</b></color>"
                            : (cand.CurrentQuantity > 0.001f
                                ? "<color=#FFB74D>Partial (Top Off)</color>"
                                : "<color=#90A4AE>Empty</color>");

                        string rowText = $"<b>{cand.Car.DisplayName}</b>  ·  {FormatHelper.FormatQuantity(cand.EffectiveQuantity, load.units)}  ·  {stateTag}  ·  {cand.LocationName} ({cand.DistanceMiles:F1} mi)";

                        sec.HStack(row =>
                        {
                            row.AddToggle(() => selectedCarIds.Contains(cand.Car.id), val =>
                            {
                                if (val) selectedCarIds.Add(cand.Car.id);
                                else selectedCarIds.Remove(cand.Car.id);
                                RebuildPanel();
                            });
                            row.AddLabel(rowText);
                        });
                    }
                });

                scroll.Spacer(8f);
                scroll.HStack(actions =>
                {
                    actions.Spacer();
                    actions.AddButtonCompact("Cancel", () => Window?.CloseWindow());
                    actions.AddButton($"Dispatch {selectedCount} Work Order(s)", () =>
                    {
                        DispatchSelected();
                    });
                });
            });
        }

        private void DispatchSelected()
        {
            SupplyFleetStore? store = SupplyFleetStore.Shared;
            if (store == null || targetBar?.Load == null || !targetBar.DestinationPosition.HasValue)
            {
                return;
            }

            List<EligibleCarCandidate> picked = candidates.Where(c => selectedCarIds.Contains(c.Car.id)).ToList();
            if (picked.Count == 0)
            {
                return;
            }

            SupplyOriginInfo? chosenSource = (availableSources.Count > 0 && selectedSourceIndex >= 0 && selectedSourceIndex < availableSources.Count)
                ? availableSources[selectedSourceIndex]
                : null;

            // If any empty car was picked without a valid source, skip it
            List<EligibleCarCandidate> validPicked = picked
                .Where(c => c.IsAlreadyLoaded || chosenSource != null)
                .ToList();

            if (validPicked.Count == 0)
            {
                return;
            }

            int dispatched = store.DispatchOrders(validPicked, targetBar, chosenSource);

            if (dispatched > 0)
            {
                ModEntryPoint.Log.Info($"Dispatched {dispatched} Work Order(s) for {targetBar.Load.description} -> {targetBar.FacilityDisplayName}.");
                onDispatchedCallback?.Invoke();
                Window?.CloseWindow();
            }
        }

        private static string FormatInputValue(float nativeAmount, LoadUnits units)
        {
            if (units == LoadUnits.Pounds)
            {
                float tons = nativeAmount / FormatHelper.ShortTonInPounds;
                return tons.ToString("0.#", CultureInfo.InvariantCulture);
            }

            return nativeAmount.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static bool TryParseInputValue(string text, LoadUnits units, out float nativeAmount)
        {
            nativeAmount = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string cleaned = text.Trim().TrimEnd('T', 't');
            if (!float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                return false;
            }

            nativeAmount = units == LoadUnits.Pounds ? val * FormatHelper.ShortTonInPounds : val;
            return true;
        }
    }
}
