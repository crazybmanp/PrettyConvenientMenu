using System.Collections.Generic;
using Game.State;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.SupplyFleet;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.SupplyFleet
{
    /// <summary>
    /// Injects a compact Supply Fleet order banner (and Cancel/Unassign + Product selector)
    /// at the top of the Car Inspector's Automatic Waybills section.
    /// </summary>
    public static class CarInspectorSupplyOrderSection
    {
        private static readonly Color BannerBgColor = new Color(0.22f, 0.17f, 0.08f, 0.55f);
        private static readonly Color BannerOutlineColor = new Color(0.85f, 0.65f, 0.25f, 0.55f);
        private static readonly Color PassiveBannerBgColor = new Color(0.08f, 0.19f, 0.22f, 0.55f);
        private static readonly Color PassiveBannerOutlineColor = new Color(0.30f, 0.72f, 0.78f, 0.55f);

        public static void BuildAutoWaybillNotice(UIPanelBuilder builder, Car? car)
        {
            if (car == null)
            {
                return;
            }

            SupplyFleetStore? store = SupplyFleetStore.Shared;
            if (store == null)
            {
                return;
            }

            if (store.StoreKeyValueObject != null)
            {
                builder.RebuildOnPropertyChange(store.StoreKeyValueObject, SupplyFleetStore.StateKey);
            }

            WorkOrder? order = store.GetOrderForCar(car);
            if (order == null)
            {
                return;
            }

            WorkTrainMode wtMode = ResolveWorkTrainMode(store, order);
            bool isManualRestockCar = order.IsPassiveWorkTrain &&
                                      wtMode == WorkTrainMode.Restock &&
                                      string.IsNullOrEmpty(order.LoadId);
            bool isPassiveVisual = order.IsPassiveWorkTrain &&
                                   (wtMode == WorkTrainMode.Passive || isManualRestockCar);

            string titleHex = isPassiveVisual ? "#4DD0E1" : "#FFD54F";
            string titleText = order.IsPassiveWorkTrain
                ? (wtMode == WorkTrainMode.Passive
                    ? "Work Train (Passive)"
                    : (isManualRestockCar ? "Work Train (Restock · Manual)" : $"Work Train ({wtMode} Mode)"))
                : "Supply Fleet Order";
            string noteHex = isPassiveVisual ? "#B2EBF2" : "#FFCC80";
            string noteText = isManualRestockCar
                ? "Restock mode active, but product is Manual — pick a product or edit waybills below."
                : (isPassiveVisual
                    ? "Passive — automatic waybills below are yours to manage."
                    : "Managed automatically — avoid editing below.");
            string cancelLabel = order.IsPassiveWorkTrain ? "Unassign" : "Cancel";
            string cancelTooltip = order.IsPassiveWorkTrain
                ? "Removes this car's Work Train assignment."
                : "Stops automatic waybill management and clears this car's active supply order.";

            RectTransform bannerRt = builder.VStack(banner =>
            {
                RectTransform hdrRt = banner.HStack(hdr =>
                {
                    hdr.ConfigureLayout(6f);
                    hdr.AddLabel($"<size=11><color={titleHex}><b>{titleText}</b></color></size>")
                        .FlexibleWidth();

                    if (StateManager.HasTrainmasterAccess)
                    {
                        hdr.AddButtonCompact(cancelLabel, () =>
                        {
                            store.CancelWorkOrders(new[] { car });
                            builder.Rebuild();
                        }).Tooltip(order.IsPassiveWorkTrain ? "Unassign Work Train" : "Cancel Supply Fleet Order", cancelTooltip);
                    }
                });
                ConfigureCompactHRow(hdrRt);

                if (order.IsPassiveWorkTrain && StateManager.HasTrainmasterAccess)
                {
                    RectTransform infoRt = banner.HStack(info =>
                    {
                        info.ConfigureLayout(6f);
                        info.AddLabel($"<size=11><color=#ECEFF1>{order.DestinationDisplayName}</color></size>")
                            .FlexibleWidth();
                        BuildInlineProductPicker(info, store, car, order, builder);
                    });
                    ConfigureCompactHRow(infoRt);
                }
                else
                {
                    banner.AddLabel($"<size=11><color=#ECEFF1>{order.ShortSummary}</color></size>");
                }

                banner.AddLabel($"<size=10><color={noteHex}>{noteText}</color></size>");
            });

            if (bannerRt == null)
            {
                return;
            }

            StyleBannerContainer(bannerRt, isPassiveVisual);

            // Move inside the just-created "Automatic Waybills" VStack, right above "When Empty".
            int sectionIndex = bannerRt.GetSiblingIndex() - 1;
            if (sectionIndex >= 0 && bannerRt.parent != null)
            {
                Transform sectionVStack = bannerRt.parent.GetChild(sectionIndex);
                bannerRt.SetParent(sectionVStack, false);
                bannerRt.SetSiblingIndex(0);
            }
        }

        private static WorkTrainMode ResolveWorkTrainMode(SupplyFleetStore store, WorkOrder order)
        {
            if (order.IsPassiveWorkTrain &&
                CoupledToDestination.TryGetCoupledToDestination(order.DestinationId, out _, out Car workCar) &&
                workCar != null)
            {
                return store.GetWorkTrainMode(workCar);
            }

            return WorkTrainMode.Passive;
        }

        private static void BuildInlineProductPicker(
            UIPanelBuilder row,
            SupplyFleetStore store,
            Car car,
            WorkOrder order,
            UIPanelBuilder parentBuilder)
        {
            List<Load> compatible = WorkOrder.GetCompatibleLoads(new[] { car });
            List<string> options = new List<string> { "Manual" };
            int currentIdx = 0;

            for (int i = 0; i < compatible.Count; i++)
            {
                options.Add(compatible[i].description);
                if (!string.IsNullOrEmpty(order.LoadId) &&
                    string.Equals(compatible[i].id, order.LoadId, System.StringComparison.OrdinalIgnoreCase))
                {
                    currentIdx = i + 1;
                }
            }

            row.AddDropdown(options, currentIdx, idx =>
            {
                string nextLoadId = idx == 0 ? string.Empty : compatible[idx - 1].id;
                store.SetWorkOrderProduct(car, nextLoadId);
                parentBuilder.Rebuild();
            }).Width(135f);
        }

        private static void ConfigureCompactHRow(RectTransform? rowRt)
        {
            if (rowRt == null)
            {
                return;
            }

            HorizontalLayoutGroup? hlg = rowRt.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
            }

            LayoutElement le = rowRt.gameObject.GetComponent<LayoutElement>() ?? rowRt.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 0f;
        }

        private static void StyleBannerContainer(RectTransform bannerRt, bool isPassive)
        {
            LayoutElement le = bannerRt.gameObject.GetComponent<LayoutElement>() ?? bannerRt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;

            Image bg = bannerRt.gameObject.GetComponent<Image>() ?? bannerRt.gameObject.AddComponent<Image>();
            bg.color = isPassive ? PassiveBannerBgColor : BannerBgColor;
            bg.raycastTarget = false;

            Outline outline = bannerRt.gameObject.GetComponent<Outline>() ?? bannerRt.gameObject.AddComponent<Outline>();
            outline.effectColor = isPassive ? PassiveBannerOutlineColor : BannerOutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup? vlg = bannerRt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.padding = new RectOffset(7, 7, 5, 5);
                vlg.spacing = 2f;
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
        }
    }
}
