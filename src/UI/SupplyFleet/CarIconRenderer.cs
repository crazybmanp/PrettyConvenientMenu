using System;
using System.Collections.Generic;
using PrettyConvenientMenu.Data.SupplyFleet;
using PrettyConvenientMenu.UI.Common;
using PrettyConvenientMenu.UI.Widgets;
using TMPro;
using UI.Builder;
using UI.Tooltips;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.SupplyFleet
{
    public enum CarIconFillState
    {
        Empty = 0,
        Partial = 1,
        Full = 2,
        OffMapSlash = 3
    }

    /// <summary>
    /// Pointer handler attached to each compact car icon supporting Left-Click select,
    /// Shift/Ctrl multi-select, Double-Click inspect, and Right-Click context actions.
    /// </summary>
    public sealed class CarIconPointerHandler : MonoBehaviour, IPointerClickHandler
    {
        public SupplyCarInfo? CarInfo { get; set; }
        public Action<SupplyCarInfo, bool, bool>? OnLeftClick { get; set; }
        public Action<SupplyCarInfo>? OnDoubleClick { get; set; }
        public Action<SupplyCarInfo>? OnRightClick { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (CarInfo == null)
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke(CarInfo);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (eventData.clickCount >= 2)
                {
                    OnDoubleClick?.Invoke(CarInfo);
                    return;
                }

                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                OnLeftClick?.Invoke(CarInfo, shift, ctrl);
            }
        }
    }

    /// <summary>
    /// Renders high-density compact rolling-stock silhouettes (36x22 px) with the 2-letter AAR type code
    /// overlaid on top, destination color, Empty/Partial/Full/Off-Map-Slash fill, and a Railroader hover tooltip balloon.
    /// </summary>
    internal static class CarIconRenderer
    {
        private const int IconWidth = 36;
        private const int IconHeight = 22;

        private static readonly Dictionary<(SupplyCarShape, CarIconFillState), Sprite> SpriteCache =
            new Dictionary<(SupplyCarShape, CarIconFillState), Sprite>();

        private static readonly Color SelectionBorderColor = new Color(0.98f, 0.80f, 0.24f, 1f);
        private static readonly Color NormalBgColor = new Color(0.09f, 0.11f, 0.14f, 0.78f);
        private static readonly Color SelectedBgColor = new Color(0.26f, 0.22f, 0.09f, 0.96f);
        private static readonly Color BadgeBgColor = new Color(0.04f, 0.05f, 0.07f, 0.88f);
        private static readonly Color DarkCodeColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        private static readonly Color LightCodeColor = new Color(0.96f, 0.97f, 0.99f, 1f);

        public static CarIconFillState GetFillState(SupplyCarInfo car)
        {
            if (car.IsOffMap)
            {
                return CarIconFillState.OffMapSlash;
            }

            if (!car.IsLoaded)
            {
                return CarIconFillState.Empty;
            }

            return car.FillRatio >= 0.85f ? CarIconFillState.Full : CarIconFillState.Partial;
        }

        private static Sprite GetSilhouetteSprite(SupplyCarShape shape, CarIconFillState fillState)
        {
            (SupplyCarShape, CarIconFillState) key = (shape, fillState);
            if (SpriteCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite created = CreateSilhouetteSprite(shape, fillState);
            SpriteCache[key] = created;
            return created;
        }

        public static void RenderIconStrip(
            UIPanelBuilder builder,
            IReadOnlyList<SupplyCarInfo> cars,
            HashSet<string> selectedCarIds,
            int iconsPerRow,
            Action<SupplyCarInfo, bool, bool> onLeftClick,
            Action<SupplyCarInfo> onDoubleClick,
            Action<SupplyCarInfo> onRightClick)
        {
            if (cars == null || cars.Count == 0)
            {
                return;
            }

            RectTransform flowRt = builder.VStack(container =>
            {
                for (int i = 0; i < cars.Count; i++)
                {
                    SupplyCarInfo car = cars[i];
                    bool isSelected = selectedCarIds.Contains(car.CarId);
                    CreateCarTile(container, car, isSelected, onLeftClick, onDoubleClick, onRightClick);
                }
            });

            AttachFlowLayout(flowRt, keepGroupsTogether: false, groupGapX: 2f);
        }

        /// <summary>
        /// Renders cars grouped into physically coupled strings inside a self-adapting <see cref="FlowLayoutGroup"/>
        /// that keeps short strings together, breaks long strings across rows with continuation markers,
        /// and inserts 12px gaps between distinct coupled cuts automatically.
        /// </summary>
        public static void RenderCoupledStringStrip(
            UIPanelBuilder builder,
            IReadOnlyList<List<SupplyCarInfo>> coupledStrings,
            HashSet<string> selectedCarIds,
            Action<SupplyCarInfo, bool, bool> onLeftClick,
            Action<SupplyCarInfo> onDoubleClick,
            Action<SupplyCarInfo> onRightClick)
        {
            if (coupledStrings == null || coupledStrings.Count == 0)
            {
                return;
            }

            RectTransform flowRt = builder.VStack(container =>
            {
                for (int s = 0; s < coupledStrings.Count; s++)
                {
                    List<SupplyCarInfo> cut = coupledStrings[s];
                    for (int c = 0; c < cut.Count; c++)
                    {
                        SupplyCarInfo car = cut[c];
                        bool isSelected = selectedCarIds.Contains(car.CarId);
                        RectTransform? tileRt = CreateCarTile(container, car, isSelected, onLeftClick, onDoubleClick, onRightClick);
                        if (tileRt != null)
                        {
                            FlowGroupMember member = tileRt.gameObject.AddComponent<FlowGroupMember>();
                            member.GroupId = s;
                            if (cut.Count > 1)
                            {
                                member.LeftContinuationMarker = CreateSplitMarker(tileRt, isRight: false);
                                member.RightContinuationMarker = CreateSplitMarker(tileRt, isRight: true);
                            }
                        }
                    }
                }
            });

            AttachFlowLayout(flowRt, keepGroupsTogether: true, groupGapX: 12f);
        }

        private static void AttachFlowLayout(RectTransform? rt, bool keepGroupsTogether, float groupGapX)
        {
            if (rt == null) return;

            VerticalLayoutGroup? vlg = rt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                UnityEngine.Object.DestroyImmediate(vlg);
            }

            FlowLayoutGroup flow = rt.gameObject.AddComponent<FlowLayoutGroup>();
            flow.SpacingX = 2f;
            flow.SpacingY = 3f;
            flow.GroupGapX = groupGapX;
            flow.KeepGroupsTogether = keepGroupsTogether;
            flow.childAlignment = TextAnchor.UpperLeft;
        }

        private static GameObject CreateSplitMarker(RectTransform tileRt, bool isRight)
        {
            GameObject markerGo = new GameObject(isRight ? "SplitRight" : "SplitLeft", typeof(RectTransform), typeof(Image))
            {
                layer = tileRt.gameObject.layer
            };
            markerGo.transform.SetParent(tileRt, worldPositionStays: false);

            RectTransform rt = markerGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(isRight ? 1f : 0f, 0.15f);
            rt.anchorMax = new Vector2(isRight ? 1f : 0f, 0.85f);
            rt.pivot = new Vector2(isRight ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(2.5f, 0f);
            rt.anchoredPosition = Vector2.zero;

            Image img = markerGo.GetComponent<Image>();
            img.color = new Color(1.00f, 0.84f, 0.25f, 0.95f);
            img.raycastTarget = false;
            markerGo.SetActive(false);
            return markerGo;
        }

        private static RectTransform? CreateCarTile(
            UIPanelBuilder parent,
            SupplyCarInfo car,
            bool isSelected,
            Action<SupplyCarInfo, bool, bool> onLeftClick,
            Action<SupplyCarInfo> onDoubleClick,
            Action<SupplyCarInfo> onRightClick)
        {
            RectTransform tileRt = parent.AddLabel(string.Empty);
            if (tileRt == null)
            {
                return null;
            }

            int uiLayer = tileRt.gameObject.layer;

            TMP_Text? existingTmp = tileRt.GetComponent<TMP_Text>();
            if (existingTmp != null)
            {
                existingTmp.text = string.Empty;
                existingTmp.raycastTarget = false;
            }

            LayoutElement le = tileRt.GetComponent<LayoutElement>() ?? tileRt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = IconWidth + 4f;
            le.preferredWidth = IconWidth + 4f;
            le.minHeight = IconHeight + 4f;
            le.preferredHeight = IconHeight + 4f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            CarIconFillState fillState = GetFillState(car);
            TooltipInfo tooltipInfo = BuildTooltipInfo(car);

            // Attach tooltip provider directly on the UIPanelBuilder-owned RectTransform (ensures Railroader TooltipManager hits it)
            tileRt.Tooltip(() => tooltipInfo);

            // Background chip (on UI layer, raycastTarget = true)
            GameObject bgGo = new GameObject("CarIconBg", typeof(RectTransform), typeof(Image))
            {
                layer = uiLayer
            };
            bgGo.transform.SetParent(tileRt, worldPositionStays: false);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            Image bgImg = bgGo.GetComponent<Image>();
            bgImg.color = isSelected ? SelectedBgColor : NormalBgColor;
            bgImg.raycastTarget = true;

            if (isSelected)
            {
                Outline outline = bgGo.AddComponent<Outline>();
                outline.effectColor = SelectionBorderColor;
                outline.effectDistance = new Vector2(1.5f, 1.5f);
            }

            // Rolling stock silhouette picture
            GameObject iconGo = new GameObject("CarSilhouette", typeof(RectTransform), typeof(Image))
            {
                layer = uiLayer
            };
            iconGo.transform.SetParent(bgGo.transform, worldPositionStays: false);
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(IconWidth, IconHeight);

            Image iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = GetSilhouetteSprite(car.Shape, fillState);
            iconImg.color = car.DestinationColor;
            iconImg.raycastTarget = false;

            // 2-letter AAR type code overlaid cleanly on the silhouette
            string aarCode = ResolveShortTypeCode(car);
            GameObject codeGo = new GameObject("CarTypeOverlay", typeof(RectTransform), typeof(TextMeshProUGUI))
            {
                layer = uiLayer
            };
            codeGo.transform.SetParent(bgGo.transform, worldPositionStays: false);
            RectTransform codeRt = codeGo.GetComponent<RectTransform>();
            codeRt.anchorMin = Vector2.zero;
            codeRt.anchorMax = Vector2.one;
            codeRt.offsetMin = new Vector2(0f, 3f);
            codeRt.offsetMax = new Vector2(0f, -1f);

            TextMeshProUGUI tmp = codeGo.GetComponent<TextMeshProUGUI>();
            tmp.text = aarCode;
            tmp.fontSize = 9.5f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = fillState == CarIconFillState.Full ? DarkCodeColor : LightCodeColor;
            tmp.raycastTarget = false;

            // Corner Status Badges:
            // Top-Left (Situation): Yellow W = Work Car / Coupled to Work Car; Green T = Coupled to Train (Loco)
            bool isCoupledToWorkCar = car.Shape == SupplyCarShape.WorkCar || car.CoupledWorkCars.Count > 0;
            bool isCoupledToTrain = !isCoupledToWorkCar && car.CoupledLocomotives.Count > 0;
            if (isCoupledToWorkCar)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: false, isTop: true, "W", new Color(1.00f, 0.86f, 0.22f, 1f));
            }
            else if (isCoupledToTrain)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: false, isTop: true, "T", new Color(0.35f, 0.90f, 0.45f, 1f));
            }

            // Bottom-Left (Location): D = At Destination, S = At Source, O = Off-Map
            if (car.IsOffMap)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: false, isTop: false, "O", new Color(0.68f, 0.78f, 0.88f, 1f));
            }
            else if (car.IsAtDestination)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: false, isTop: false, "D", new Color(0.30f, 0.85f, 0.96f, 1f));
            }
            else if (car.IsAtSource)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: false, isTop: false, "S", new Color(1.00f, 0.68f, 0.26f, 1f));
            }

            // Top-Right (Advisories): Red ! = Blocked (No material to load); Green * = Suggested Job
            if (car.IsBlockedNoMaterial)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: true, isTop: true, "!", new Color(0.95f, 0.34f, 0.32f, 1f));
            }
            else if (car.SuggestedJob != null)
            {
                AddCornerBadge(bgGo.transform, uiLayer, isRight: true, isTop: true, "*", new Color(0.35f, 0.90f, 0.45f, 1f));
            }

            // Also attach UITooltipProvider on bgGo itself (the raycast hit target) so hover balloon always triggers
            UITooltipProvider bgTooltip = bgGo.AddComponent<UITooltipProvider>();
            bgTooltip.TooltipInfo = tooltipInfo;

            CarIconPointerHandler handler = bgGo.AddComponent<CarIconPointerHandler>();
            handler.CarInfo = car;
            handler.OnLeftClick = onLeftClick;
            handler.OnDoubleClick = onDoubleClick;
            handler.OnRightClick = onRightClick;

            return tileRt;
        }

        private static string ResolveShortTypeCode(SupplyCarInfo car)
        {
            if (car.Car != null)
            {
                string code = car.Car.CarType.ToString();
                if (!string.IsNullOrEmpty(code))
                {
                    return code.Length <= 3 ? code.ToUpperInvariant() : code.Substring(0, 3).ToUpperInvariant();
                }
            }

            return car.Shape switch
            {
                SupplyCarShape.Hopper => "HM",
                SupplyCarShape.Gondola => "GB",
                SupplyCarShape.Flatcar => "FM",
                SupplyCarShape.Boxcar => "XM",
                SupplyCarShape.Tanker => "TM",
                SupplyCarShape.WorkCar => "MW",
                _ => "FR"
            };
        }

        private static TooltipInfo BuildTooltipInfo(SupplyCarInfo car)
        {
            string aarCode = ResolveShortTypeCode(car);
            string title = $"{car.DisplayName}  ·  {aarCode} ({car.Shape})";

            string capText = car.MaxCapacity > 0.001f
                ? FormatHelper.FormatQuantity(car.MaxCapacity, car.Units)
                : "N/A";
            string curText = FormatHelper.FormatQuantity(car.CurrentQuantity, car.Units);
            string fillLine = car.IsLoaded
                ? $"{curText} / {capText} ({car.FillRatio * 100f:F0}%) {car.CurrentLoad?.description ?? string.Empty}"
                : (car.MaxCapacity > 0.001f ? $"Empty (0 / {capText})" : "Empty");

            List<string> statuses = new List<string>();
            if (car.IsOffMap) statuses.Add("Off-Map (Interchanged)");
            else if (car.IsAtDestination) statuses.Add("At Destination (Spotted)");
            else if (car.IsAtSource) statuses.Add("At Source (Loading)");
            if (car.IsBlockedNoMaterial) statuses.Add("<color=#EF5350>Blocked: Source has no material</color>");
            string statusLine = statuses.Count > 0 ? $"\nStatus: {string.Join(" · ", statuses)}" : string.Empty;
            string orderText = car.ActiveOrder != null ? car.ActiveOrder.ShortSummary : "Unassigned";
            string plannedLoadLine = string.Empty;
            if (car.ActiveOrder != null && !car.ActiveOrder.IsPassiveWorkTrain && !string.IsNullOrEmpty(car.ActiveOrder.LoadId))
            {
                Model.Ops.Definition.Load? orderedLoad = Model.Ops.Definition.CarLoadLibrary.GetLoad(car.ActiveOrder.LoadId);
                string loadName = orderedLoad != null ? orderedLoad.description : car.ActiveOrder.LoadId;

                if (!car.ActiveOrder.HasBeenLoaded)
                {
                    string qtyStr = orderedLoad != null && car.ActiveOrder.ExpectedQuantity > 0.001f
                        ? $" ({FormatHelper.FormatQuantity(car.ActiveOrder.ExpectedQuantity, orderedLoad.units)})"
                        : string.Empty;
                    plannedLoadLine = $"\n<color=#FFB74D>Planning to load: {loadName}{qtyStr}</color>";
                }
            }
            string locosLine = car.CoupledLocomotives.Count > 0
                ? $"\nCoupled Locos: {string.Join(", ", car.CoupledLocomotives)}"
                : string.Empty;
            string workCarsLine = car.CoupledWorkCars.Count > 0
                ? $"\nCoupled Work Cars: {string.Join(", ", car.CoupledWorkCars)}"
                : string.Empty;
            string suggestedLine = car.SuggestedJob != null
                ? $"\n<color=#66BB6A>Suggested: {car.SuggestedJob.Summary}</color>"
                : string.Empty;

            string body = $"Fill: {fillLine}{plannedLoadLine}{statusLine}\nLocation: {car.LocationArea}\nDestination: {car.DestinationName}\nOrder: {orderText}{locosLine}{workCarsLine}{suggestedLine}\n<i>Click: select · Shift: multi · 2xClick: inspect</i>";
            return new TooltipInfo(title, body);
        }

        private static Sprite CreateSilhouetteSprite(SupplyCarShape shape, CarIconFillState fillState)
        {
            Texture2D tex = new Texture2D(IconWidth, IconHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 solid = new Color32(255, 255, 255, 255);
            Color32 slash = new Color32(255, 255, 255, 160);
            Color32[] pixels = new Color32[IconWidth * IconHeight];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            bool[,] mask = BuildBodyMask(shape);

            int minY = IconHeight;
            int maxY = 0;
            for (int y = 0; y < IconHeight; y++)
            {
                for (int x = 0; x < IconWidth; x++)
                {
                    if (mask[x, y])
                    {
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            int midY = minY + ((maxY - minY) / 2);

            for (int y = 0; y < IconHeight; y++)
            {
                for (int x = 0; x < IconWidth; x++)
                {
                    if (!mask[x, y]) continue;

                    bool isBorder = x == 0 || y == 0 || x == IconWidth - 1 || y == IconHeight - 1 ||
                                    !mask[x - 1, y] || !mask[x + 1, y] || !mask[x, y - 1] || !mask[x, y + 1];

                    if (isBorder)
                    {
                        pixels[y * IconWidth + x] = solid;
                        continue;
                    }

                    switch (fillState)
                    {
                        case CarIconFillState.Full:
                            pixels[y * IconWidth + x] = solid;
                            break;
                        case CarIconFillState.Partial:
                            if (y <= midY) pixels[y * IconWidth + x] = solid;
                            break;
                        case CarIconFillState.OffMapSlash:
                            if (((x + y) % 5) < 2) pixels[y * IconWidth + x] = slash;
                            break;
                        case CarIconFillState.Empty:
                        default:
                            break;
                    }
                }
            }

            // Wheels
            DrawWheel(pixels, 7, 1, solid);
            DrawWheel(pixels, 28, 1, solid);

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, IconWidth, IconHeight), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void AddCornerBadge(
            Transform parent,
            int uiLayer,
            bool isRight,
            bool isTop,
            string symbol,
            Color textColor)
        {
            GameObject badgeGo = new GameObject($"Badge_{symbol}", typeof(RectTransform), typeof(Image))
            {
                layer = uiLayer
            };
            badgeGo.transform.SetParent(parent, worldPositionStays: false);

            RectTransform rt = badgeGo.GetComponent<RectTransform>();
            float ax = isRight ? 1f : 0f;
            float ay = isTop ? 1f : 0f;
            rt.anchorMin = new Vector2(ax, ay);
            rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(12f, 11f);
            rt.anchoredPosition = Vector2.zero;

            Image bg = badgeGo.GetComponent<Image>();
            bg.color = BadgeBgColor;
            bg.raycastTarget = false;

            GameObject txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI))
            {
                layer = uiLayer
            };
            txtGo.transform.SetParent(badgeGo.transform, worldPositionStays: false);
            RectTransform txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = txtGo.GetComponent<TextMeshProUGUI>();
            tmp.text = symbol;
            tmp.fontSize = 9.0f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.color = textColor;
            tmp.raycastTarget = false;
        }

        private static bool[,] BuildBodyMask(SupplyCarShape shape)
        {
            bool[,] m = new bool[IconWidth, IconHeight];

            void FillRect(int x0, int y0, int x1, int y1)
            {
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(IconHeight - 1, y1); y++)
                {
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(IconWidth - 1, x1); x++)
                    {
                        m[x, y] = true;
                    }
                }
            }

            switch (shape)
            {
                case SupplyCarShape.Hopper:
                    // Wide open hopper top + two prominent V-shaped bottom bays
                    FillRect(3, 10, 32, 19);
                    FillRect(5, 7, 16, 9);
                    FillRect(19, 7, 30, 9);
                    FillRect(8, 4, 13, 6);
                    FillRect(22, 4, 27, 6);
                    break;

                case SupplyCarShape.Gondola:
                    // Low, long open-top gondola
                    FillRect(2, 4, 33, 13);
                    break;

                case SupplyCarShape.Flatcar:
                    // Thin flat deck with raised end bulkheads/stakes
                    FillRect(1, 4, 34, 7);
                    FillRect(2, 8, 4, 14);
                    FillRect(31, 8, 33, 14);
                    FillRect(6, 8, 29, 10);
                    break;

                case SupplyCarShape.Boxcar:
                    // Tall full-height enclosed boxcar
                    FillRect(3, 4, 32, 19);
                    break;

                case SupplyCarShape.Tanker:
                    // Horizontal cylinder with rounded ends + prominent top dome
                    FillRect(2, 4, 33, 6);
                    FillRect(6, 7, 29, 16);
                    FillRect(4, 8, 31, 15);
                    FillRect(3, 10, 32, 13);
                    FillRect(15, 17, 20, 20);
                    break;

                case SupplyCarShape.WorkCar:
                    // Flat deck + cab on left + angled crane boom on right
                    FillRect(1, 4, 34, 7);
                    FillRect(3, 8, 16, 18);
                    for (int i = 0; i < 12; i++)
                    {
                        int bx = 17 + i;
                        int by = 8 + i;
                        if (bx < IconWidth && by < IconHeight)
                        {
                            m[bx, by] = true;
                            if (by + 1 < IconHeight) m[bx, by + 1] = true;
                            if (by + 2 < IconHeight) m[bx, by + 2] = true;
                        }
                    }
                    break;
            }

            return m;
        }

        private static void DrawWheel(Color32[] pixels, int cx, int cy, Color32 col)
        {
            for (int dy = 0; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (Mathf.Abs(dx) == 2 && (dy == 0 || dy == 2)) continue;
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x >= 0 && x < IconWidth && y >= 0 && y < IconHeight)
                    {
                        pixels[y * IconWidth + x] = col;
                    }
                }
            }
        }
    }
}
