using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.SupplyFleet;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets.ProgressBar;
using UI.Builder;
using UI.Windows;
using UnityEngine;

namespace PrettyConvenientMenu.UI.SupplyFleet
{
    public enum AssignCarOrderMode
    {
        LoadAndDeliver,
        DirectUnload,
        PassiveWorkTrain
    }

    /// <summary>
    /// Dedicated pop-out dialog for assigning Work Orders to selected car(s):
    /// - Load &amp; Deliver (source + destination)
    /// - Direct Unload (with full overview of all compatible stockpiles, current stock, targets &amp; deltas)
    /// - Passive Work Train assignment
    /// </summary>
    [RequireComponent(typeof(Window))]
    public sealed class AssignCarOrderWindow : BuilderWindowBase<AssignCarOrderWindow>
    {
        private static readonly Vector2Int DefaultWindowSize = new Vector2Int(580, 480);
        private static readonly Vector2Int MinWindowSize = new Vector2Int(460, 360);

        private static AssignCarOrderWindow? activeWindow;

        public override string Identifier => "PrettyConvenientMenu.AssignCarOrder";
        public override WindowConfig Config => WindowConfig.Resizable(DefaultWindowSize, MinWindowSize);

        private AssignCarOrderMode mode;
        private List<SupplyCarInfo> selectedCars = new List<SupplyCarInfo>();
        private SupplyNetworkSnapshot? snapshot;
        private Action<string>? onCompletedCallback;

        private int selectedDestIndex;
        private int selectedSourceIndex;
        private int selectedWorkCarIndex;
        private int selectedProductIndex;
        private bool isEditingExisting;

        public static void Open(
            AssignCarOrderMode mode,
            List<SupplyCarInfo> cars,
            SupplyNetworkSnapshot snapshot,
            Action<string>? onCompleted = null)
        {
            if (cars == null || cars.Count == 0 || snapshot == null || WindowManager.Shared == null)
            {
                return;
            }

            try
            {
                if (activeWindow == null || activeWindow.Window == null)
                {
                    activeWindow = WindowManager.Shared.CreateWindowWithComponent<AssignCarOrderWindow>();
                }

                activeWindow.InitializeForSelection(mode, cars, snapshot, onCompleted);
                activeWindow.Window?.OrderFront();
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to open AssignCarOrderWindow.");
            }
        }

        private void InitializeForSelection(
            AssignCarOrderMode assignMode,
            List<SupplyCarInfo> cars,
            SupplyNetworkSnapshot snap,
            Action<string>? onCompleted)
        {
            mode = assignMode;
            selectedCars = new List<SupplyCarInfo>(cars);
            snapshot = snap;
            onCompletedCallback = onCompleted;
            selectedDestIndex = 0;
            selectedSourceIndex = 0;
            selectedWorkCarIndex = 0;
            selectedProductIndex = 0;
            isEditingExisting = cars.Any(c => c.ActiveOrder != null);

            if (assignMode == AssignCarOrderMode.PassiveWorkTrain)
            {
                PreselectWorkTrainFields(cars, snap);
            }

            Title = assignMode switch
            {
                AssignCarOrderMode.DirectUnload => $"Unload Loaded ({cars.Count} car{(cars.Count == 1 ? "" : "s")})",
                AssignCarOrderMode.PassiveWorkTrain => isEditingExisting
                    ? $"Edit Work Train ({cars.Count} car{(cars.Count == 1 ? "" : "s")})"
                    : $"Assign to Work Train ({cars.Count} car{(cars.Count == 1 ? "" : "s")})",
                _ => isEditingExisting
                    ? $"Edit Work Order ({cars.Count} car{(cars.Count == 1 ? "" : "s")})"
                    : $"Assign Work Order ({cars.Count} car{(cars.Count == 1 ? "" : "s")})"
            };

            Show();
        }

        private void PreselectWorkTrainFields(List<SupplyCarInfo> cars, SupplyNetworkSnapshot snap)
        {
            WorkOrder? firstExisting = cars.Select(c => c.ActiveOrder).FirstOrDefault(o => o != null && o.IsPassiveWorkTrain);
            if (firstExisting == null)
            {
                return;
            }

            for (int i = 0; i < snap.AvailableWorkCars.Count; i++)
            {
                Car wc = snap.AvailableWorkCars[i];
                Car root = SupplyFleetStore.Shared != null ? SupplyFleetStore.Shared.ResolveRootWorkCar(wc) : wc;
                if (string.Equals(Model.Ops.CoupledToDestination.GetIdentifierForCar(root), firstExisting.DestinationId, StringComparison.Ordinal))
                {
                    selectedWorkCarIndex = i;
                    break;
                }
            }

            if (string.IsNullOrEmpty(firstExisting.LoadId))
            {
                selectedProductIndex = 1; // None (Manual)
                return;
            }

            List<Load> compatible = WorkOrder.GetCompatibleLoads(cars.Select(c => c.Car));
            for (int i = 0; i < compatible.Count; i++)
            {
                if (string.Equals(compatible[i].id, firstExisting.LoadId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedProductIndex = i + 2;
                    return;
                }
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
            if (snapshot == null || selectedCars.Count == 0)
            {
                builder.AddLabel("No cars selected.");
                return;
            }

            builder.VScrollView(scroll =>
            {
                scroll.ConfigureLayout(6f);

                string carListText = string.Join(", ", selectedCars.Take(10).Select(c => c.DisplayName));
                if (selectedCars.Count > 10) carListText += $" (+{selectedCars.Count - 10} more)";
                scroll.AddTitle(Title, $"Cars: {carListText}");

                switch (mode)
                {
                    case AssignCarOrderMode.DirectUnload:
                        BuildDirectUnloadContent(scroll);
                        break;
                    case AssignCarOrderMode.LoadAndDeliver:
                        BuildLoadAndDeliverContent(scroll);
                        break;
                    case AssignCarOrderMode.PassiveWorkTrain:
                        BuildPassiveWorkTrainContent(scroll);
                        break;
                }
            });
        }

        private void BuildDirectUnloadContent(UIPanelBuilder scroll)
        {
            Load? primaryLoad = selectedCars[0].CurrentLoad;
            if (primaryLoad == null)
            {
                scroll.AddLabel("<color=#FFB74D>Selected car has no cargo.</color>");
                return;
            }

            float totalLoaded = selectedCars.Sum(c => c.CurrentQuantity);
            scroll.AddLabel($"<size=12>Cargo to unload: <b>{FormatHelper.FormatQuantity(totalLoaded, primaryLoad.units)} {primaryLoad.description}</b> across {selectedCars.Count} car(s)</size>");

            List<SupplyCommodityBar> compatibleDests = snapshot!.FindCompatibleUnloadDestinations(primaryLoad);
            if (compatibleDests.Count == 0)
            {
                scroll.AddLabel("<color=#FFB74D>No unlocked stockpiles or sinks accept this commodity.</color>");
                return;
            }

            selectedDestIndex = Mathf.Clamp(selectedDestIndex, 0, compatibleDests.Count - 1);

            scroll.AddSection($"Stockpiles & Sinks Accepting {primaryLoad.description}", sec =>
            {
                for (int i = 0; i < compatibleDests.Count; i++)
                {
                    int idx = i;
                    SupplyCommodityBar bar = compatibleDests[i];
                    bool isSelected = idx == selectedDestIndex;

                    sec.HStack(hdr =>
                    {
                        hdr.ConfigureLayout(6f);
                        hdr.AddToggle(() => selectedDestIndex == idx, val =>
                        {
                            if (val)
                            {
                                selectedDestIndex = idx;
                                RebuildPanel();
                            }
                        });

                        if (bar.IsSinkDemandOnly)
                        {
                            string needText = FormatHelper.FormatQuantity(bar.RemainingDemand, bar.Load.units);
                            string onTrainText = bar.DeliveryAmount > 0.001f
                                ? $" <color=#66BB6A>(+{FormatHelper.FormatQuantity(bar.DeliveryAmount, bar.Load.units)} on train)</color>"
                                : string.Empty;
                            string ordText = bar.FleetInboundAmount > 0.001f
                                ? $" <color=#81C784>({FormatHelper.FormatQuantity(bar.FleetInboundAmount, bar.Load.units)} ord)</color>"
                                : string.Empty;
                            hdr.AddLabel($"<size=12><b>{bar.FacilityDisplayName}</b>  ·  {needText} needed{onTrainText}{ordText}</size>");
                        }
                        else
                        {
                            string curText = FormatHelper.FormatQuantity(bar.CurrentAmount, bar.Load.units);
                            string onTrackPart = bar.DeliveryAmount > 0.001f
                                ? $" <color=#66BB6A>(+{FormatHelper.FormatQuantity(bar.DeliveryAmount, bar.Load.units)} on track)</color>"
                                : string.Empty;
                            string maxText = FormatHelper.FormatQuantity(bar.MaxCapacity, bar.Load.units);
                            string tgtPart = string.Empty;
                            string deltaPart = string.Empty;

                            if (bar.HasTarget && bar.TargetAmount > 0.001f)
                            {
                                float delta = (bar.CurrentAmount + bar.DeliveryAmount + bar.FleetInboundAmount - bar.TotalOutboundAmount) - bar.TargetAmount;
                                bool met = delta >= -0.001f;
                                string col = met ? "#66BB6A" : "#FFB74D";
                                string absStr = FormatHelper.FormatQuantity(Mathf.Abs(delta), bar.Load.units);
                                tgtPart = $" <color={col}>(Tgt {FormatHelper.FormatQuantity(bar.TargetAmount, bar.Load.units)})</color>";
                                deltaPart = met ? $"<color=#66BB6A><b>+{absStr}</b></color>" : $"<color=#FFB74D><b>-{absStr}</b></color>";
                            }
                            else
                            {
                                tgtPart = " <color=#78909C>(No Tgt)</color>";
                            }

                            hdr.AddLabel($"<size=12><b>{bar.FacilityDisplayName}</b>  {curText}{onTrackPart} / {maxText}{tgtPart}</size>");
                            if (!string.IsNullOrEmpty(deltaPart))
                            {
                                hdr.Spacer();
                                hdr.AddLabel($"<size=12>{deltaPart}</size>");
                            }
                        }
                    });

                    // Progress bar preview for this stockpile/sink
                    if (bar.IsSinkDemandOnly)
                    {
                        float delivRatio = bar.RemainingDemand > 0.001f ? Mathf.Clamp01(bar.DeliveryAmount / bar.RemainingDemand) : 0f;
                        float ordRatio = bar.RemainingDemand > 0.001f ? Mathf.Clamp01(bar.FleetInboundAmount / bar.RemainingDemand) : 0f;
                        ProgressBarFactory.CreateMowBar(sec, stockRatio: 0f, deliveryRatio: delivRatio, inboundRatio: ordRatio);
                    }
                    else
                    {
                        ProgressBarFactory.CreateMowBar(
                            sec,
                            stockRatio: bar.StockRatio,
                            deliveryRatio: bar.DeliveryRatio,
                            outgoingRatio: bar.OutgoingRatio,
                            committedRatio: bar.CommittedOutboundRatio,
                            inboundRatio: bar.FleetInboundRatio,
                            targetRatio: bar.TargetRatio,
                            storageCapRatio: bar.StorageCapRatio);
                    }

                    sec.Spacer(4f);
                }
            });

            scroll.Spacer(6f);
            scroll.HStack(actions =>
            {
                actions.Spacer();
                actions.AddButtonCompact("Cancel", () => Window?.CloseWindow());
                SupplyCommodityBar chosen = compatibleDests[selectedDestIndex];
                actions.AddButton($"Unload at {chosen.FacilityDisplayName}", () =>
                {
                    if (SupplyFleetStore.Shared == null) return;
                    int count = SupplyFleetStore.Shared.DispatchDirectUnload(selectedCars, chosen);
                    onCompletedCallback?.Invoke($"Dispatched {count} direct unload order(s) to {chosen.FacilityDisplayName}.");
                    Window?.CloseWindow();
                });
            });
        }

        private void BuildLoadAndDeliverContent(UIPanelBuilder scroll)
        {
            List<SupplyCommodityBar> compatibleDests = snapshot!.FindCompatibleOrderDestinations(selectedCars);
            if (compatibleDests.Count == 0)
            {
                scroll.AddLabel("<color=#FFB74D>No unlocked destinations match the selected car type(s).</color>");
                return;
            }

            selectedDestIndex = Mathf.Clamp(selectedDestIndex, 0, compatibleDests.Count - 1);
            SupplyCommodityBar chosenDest = compatibleDests[selectedDestIndex];
            List<SupplyOriginInfo> sources = snapshot.FindSourcesFor(chosenDest);
            selectedSourceIndex = sources.Count > 0 ? Mathf.Clamp(selectedSourceIndex, 0, sources.Count - 1) : 0;

            scroll.AddSection("1. Destination & Commodity", sec =>
            {
                List<string> destLabels = compatibleDests
                    .Select(d => $"{d.Load.description} -> {d.FacilityDisplayName}")
                    .ToList();
                sec.AddDropdown(destLabels, selectedDestIndex, idx =>
                {
                    selectedDestIndex = idx;
                    selectedSourceIndex = 0;
                    RebuildPanel();
                });

                sec.Spacer(4f);
                RenderDestinationDemandPreview(sec, chosenDest);
            });

            scroll.AddSection("2. Source (Fill From)", sec =>
            {
                if (sources.Count == 0)
                {
                    sec.AddLabel("<color=#FFB74D>No unlocked source found for this commodity.</color>");
                    return;
                }

                List<string> srcLabels = sources.Select(s => s.FormatDropdownLabel()).ToList();
                sec.AddDropdown(srcLabels, selectedSourceIndex, idx => selectedSourceIndex = idx);
            });

            scroll.Spacer(8f);
            scroll.HStack(actions =>
            {
                actions.Spacer();
                actions.AddButtonCompact("Cancel", () => Window?.CloseWindow());
                if (sources.Count > 0)
                {
                    actions.AddButton($"Dispatch {selectedCars.Count} Work Order(s)", () =>
                    {
                        if (SupplyFleetStore.Shared == null) return;
                        SupplyOriginInfo src = sources[selectedSourceIndex];
                        List<EligibleCarCandidate> candidates = new List<EligibleCarCandidate>();
                        foreach (SupplyCarInfo carInfo in selectedCars)
                        {
                            if (SupplyFleetStore.TryGetCarCapacityForLoad(carInfo.Car, chosenDest.Load, out float cap))
                            {
                                candidates.Add(new EligibleCarCandidate(carInfo.Car, cap, 0f, false, 0f, carInfo.LocationArea));
                            }
                        }

                        int dispatched = SupplyFleetStore.Shared.DispatchOrders(candidates, chosenDest, src);
                        onCompletedCallback?.Invoke($"Dispatched {dispatched} Work Order(s): {src.DisplayName} -> {chosenDest.FacilityDisplayName}.");
                        Window?.CloseWindow();
                    });
                }
            });
        }

        private void RenderDestinationDemandPreview(UIPanelBuilder sec, SupplyCommodityBar bar)
        {
            float dispatchQty = 0f;
            int eligibleCount = 0;
            for (int i = 0; i < selectedCars.Count; i++)
            {
                if (SupplyFleetStore.TryGetCarCapacityForLoad(selectedCars[i].Car, bar.Load, out float cap) && cap > 0.001f)
                {
                    dispatchQty += cap;
                    eligibleCount++;
                }
            }

            if (bar.IsSinkDemandOnly)
            {
                string needText = FormatHelper.FormatQuantity(bar.RemainingDemand, bar.Load.units);
                string onTrainText = bar.DeliveryAmount > 0.001f
                    ? $" <color=#66BB6A>(+{FormatHelper.FormatQuantity(bar.DeliveryAmount, bar.Load.units)} on train)</color>"
                    : string.Empty;
                string ordText = bar.FleetInboundAmount > 0.001f
                    ? $" <color=#81C784>({FormatHelper.FormatQuantity(bar.FleetInboundAmount, bar.Load.units)} ordered)</color>"
                    : string.Empty;
                sec.AddLabel($"<size=11><b>{bar.Load.description}</b>: {needText} needed{onTrainText}{ordText}</size>");

                float delivRatio = bar.RemainingDemand > 0.001f
                    ? Mathf.Clamp01(bar.DeliveryAmount / bar.RemainingDemand)
                    : 0f;
                float ordRatio = bar.RemainingDemand > 0.001f
                    ? Mathf.Clamp01(bar.FleetInboundAmount / bar.RemainingDemand)
                    : 0f;
                ProgressBarFactory.CreateMowBar(sec, stockRatio: 0f, deliveryRatio: delivRatio, inboundRatio: ordRatio);

                if (eligibleCount > 0)
                {
                    float afterRemaining = bar.RemainingDemand - bar.DeliveryAmount - bar.FleetInboundAmount - dispatchQty;
                    string afterText = afterRemaining <= 0.001f
                        ? "<color=#66BB6A><b>Fully covered</b></color>"
                        : $"<color=#FFB74D>{FormatHelper.FormatQuantity(afterRemaining, bar.Load.units)} still needed</color>";
                    sec.AddLabel($"<size=11><color=#B0BEC5>This dispatch: +{FormatHelper.FormatQuantity(dispatchQty, bar.Load.units)} ({eligibleCount} car(s))  ·  After: {afterText}</color></size>");
                }
            }
            else
            {
                string curText = FormatHelper.FormatQuantity(bar.CurrentAmount, bar.Load.units);
                string onTrackPart = bar.DeliveryAmount > 0.001f
                    ? $" <color=#66BB6A>(+{FormatHelper.FormatQuantity(bar.DeliveryAmount, bar.Load.units)} on track)</color>"
                    : string.Empty;
                string maxText = FormatHelper.FormatQuantity(bar.MaxCapacity, bar.Load.units);
                string tgtPart;
                string deltaPart = string.Empty;

                if (bar.HasTarget && bar.TargetAmount > 0.001f)
                {
                    float delta = (bar.CurrentAmount + bar.DeliveryAmount + bar.FleetInboundAmount - bar.TotalOutboundAmount) - bar.TargetAmount;
                    bool met = delta >= -0.001f;
                    string col = met ? "#66BB6A" : "#FFB74D";
                    string absStr = FormatHelper.FormatQuantity(Mathf.Abs(delta), bar.Load.units);
                    tgtPart = $" <color={col}>(Tgt {FormatHelper.FormatQuantity(bar.TargetAmount, bar.Load.units)})</color>";
                    deltaPart = met ? $"<color=#66BB6A><b>+{absStr}</b></color>" : $"<color=#FFB74D><b>-{absStr}</b></color>";
                }
                else
                {
                    tgtPart = " <color=#78909C>(No Tgt)</color>";
                }

                sec.HStack(hdr =>
                {
                    hdr.AddLabel($"<size=11><b>{bar.Load.description}</b>: {curText}{onTrackPart} / {maxText}{tgtPart}</size>");
                    if (!string.IsNullOrEmpty(deltaPart))
                    {
                        hdr.Spacer();
                        hdr.AddLabel($"<size=11>{deltaPart}</size>");
                    }
                });

                ProgressBarFactory.CreateMowBar(
                    sec,
                    stockRatio: bar.StockRatio,
                    deliveryRatio: bar.DeliveryRatio,
                    outgoingRatio: bar.OutgoingRatio,
                    committedRatio: bar.CommittedOutboundRatio,
                    inboundRatio: bar.FleetInboundRatio,
                    targetRatio: bar.TargetRatio,
                    storageCapRatio: bar.StorageCapRatio);

                if (eligibleCount > 0)
                {
                    string addText = FormatHelper.FormatQuantity(dispatchQty, bar.Load.units);
                    string afterSummary;
                    if (bar.HasTarget && bar.TargetAmount > 0.001f)
                    {
                        float afterDelta = (bar.CurrentAmount + bar.DeliveryAmount + bar.FleetInboundAmount - bar.TotalOutboundAmount + dispatchQty) - bar.TargetAmount;
                        bool afterMet = afterDelta >= -0.001f;
                        string absAfter = FormatHelper.FormatQuantity(Mathf.Abs(afterDelta), bar.Load.units);
                        afterSummary = afterMet
                            ? $"<color=#66BB6A><b>+{absAfter} vs Tgt</b></color>"
                            : $"<color=#FFB74D><b>-{absAfter} vs Tgt</b></color>";
                    }
                    else
                    {
                        float afterTotal = bar.CurrentAmount + bar.DeliveryAmount + bar.FleetInboundAmount + dispatchQty;
                        afterSummary = $"{FormatHelper.FormatQuantity(afterTotal, bar.Load.units)} / {maxText}";
                    }

                    sec.AddLabel($"<size=11><color=#B0BEC5>This dispatch: +{addText} ({eligibleCount} car(s))  ·  After: {afterSummary}</color></size>");
                }
            }
        }

        private void BuildPassiveWorkTrainContent(UIPanelBuilder scroll)
        {
            List<Car> workCars = snapshot!.AvailableWorkCars;
            if (workCars.Count == 0)
            {
                scroll.AddLabel("<color=#FFB74D>No owned MOW Work Cars found on the railroad.</color>");
                return;
            }

            selectedWorkCarIndex = Mathf.Clamp(selectedWorkCarIndex, 0, workCars.Count - 1);

            scroll.AddSection("1. Work Train", sec =>
            {
                sec.AddLabel("<size=11><color=#B0BEC5>Assign cars to a Work Train. Waybill automation follows the Work Car's active mode (Work / Restock / Passive).</color></size>");
                List<string> labels = workCars.Select(wc =>
                {
                    Car root = SupplyFleetStore.Shared != null ? SupplyFleetStore.Shared.ResolveRootWorkCar(wc) : wc;
                    string area = SupplyFleetStore.ResolveCarAreaName(root);
                    WorkTrainMode wtMode = SupplyFleetStore.Shared != null ? SupplyFleetStore.Shared.GetWorkTrainMode(root) : WorkTrainMode.Passive;
                    return root.id == wc.id
                        ? $"Work Car {wc.DisplayName} ({area}) [{wtMode}]"
                        : $"Work Car {wc.DisplayName} -> pooled under {root.DisplayName} ({area}) [{wtMode}]";
                }).ToList();

                sec.AddDropdown(labels, selectedWorkCarIndex, idx => selectedWorkCarIndex = idx);
            });

            List<Load> compatibleLoads = WorkOrder.GetCompatibleLoads(selectedCars.Select(c => c.Car));
            List<string> productOptions = new List<string> { "Auto-Detect per Car", "None (Manual)" };
            for (int i = 0; i < compatibleLoads.Count; i++)
            {
                productOptions.Add(compatibleLoads[i].description);
            }
            selectedProductIndex = Mathf.Clamp(selectedProductIndex, 0, productOptions.Count - 1);

            scroll.AddSection("2. Product (Used in Restock Mode)", sec =>
            {
                sec.AddDropdown(productOptions, selectedProductIndex, idx =>
                {
                    selectedProductIndex = idx;
                    RebuildPanel();
                });

                if (selectedProductIndex == 0)
                {
                    List<string> detectedLabels = selectedCars
                        .Take(4)
                        .Select(c =>
                        {
                            string id = WorkOrder.DetectCarProductId(c.Car, Model.Ops.OpsController.Shared);
                            Load? ld = !string.IsNullOrEmpty(id) ? CarLoadLibrary.GetLoad(id) : null;
                            return $"{c.DisplayName}: {(ld != null ? ld.description : "None")}";
                        })
                        .ToList();
                    if (selectedCars.Count > 4) detectedLabels.Add($"+{selectedCars.Count - 4} more");
                    sec.AddLabel($"<size=11><color=#90A4AE>Detected: {string.Join(" · ", detectedLabels)}</color></size>");
                }
            });

            scroll.Spacer(8f);
            scroll.HStack(actions =>
            {
                actions.Spacer();
                actions.AddButtonCompact("Cancel", () => Window?.CloseWindow());
                string submitText = isEditingExisting
                    ? $"Save {selectedCars.Count} Car(s)"
                    : $"Assign {selectedCars.Count} Car(s)";

                actions.AddButton(submitText, () =>
                {
                    if (SupplyFleetStore.Shared == null) return;
                    Car chosen = workCars[selectedWorkCarIndex];

                    string? overrideLoadId = selectedProductIndex switch
                    {
                        0 => null,
                        1 => string.Empty,
                        _ => compatibleLoads[selectedProductIndex - 2].id
                    };
                    bool autoDetect = selectedProductIndex == 0;

                    int count = SupplyFleetStore.Shared.AssignPassiveWorkTrain(selectedCars, chosen, overrideLoadId, autoDetect);
                    string verb = isEditingExisting ? "Updated" : "Assigned";
                    onCompletedCallback?.Invoke($"{verb} {count} car(s) on Work Train {chosen.DisplayName}.");
                    Window?.CloseWindow();
                });
            });
        }
    }
}
