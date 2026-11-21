using System;
using System.Collections.Generic;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.AutoDestinations;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.UI.Common;
using UnityEngine;

namespace PrettyConvenientMenu.Data.SupplyFleet
{
    public enum SupplyCarShape
    {
        Hopper,
        Gondola,
        Flatcar,
        Boxcar,
        Tanker,
        WorkCar
    }

    public enum SuggestedJobKind
    {
        DirectUnload,
        LoadAndDeliver,
        PassiveWorkTrain
    }

    /// <summary>
    /// Represents a detected, actionable job suggestion for an unassigned supply car.
    /// </summary>
    public sealed class SuggestedCarJob
    {
        public SuggestedJobKind Kind { get; set; }
        public SupplyCommodityBar? DestinationBar { get; set; }
        public SupplyOriginInfo? SourceOrigin { get; set; }
        public Car? TargetWorkCar { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Live visual and operational snapshot of a single tracked supply car.
    /// </summary>
    public sealed class SupplyCarInfo
    {
        public Car Car { get; set; } = null!;
        public string CarId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public SupplyCarShape Shape { get; set; } = SupplyCarShape.Gondola;
        public Load? CurrentLoad { get; set; }
        public float CurrentQuantity { get; set; }
        public float MaxCapacity { get; set; }
        public LoadUnits Units { get; set; } = LoadUnits.Pounds;
        public float FillRatio => MaxCapacity > 0.001f ? Mathf.Clamp01(CurrentQuantity / MaxCapacity) : 0f;
        public bool IsLoaded => CurrentLoad != null && CurrentQuantity > 0.001f;
        public bool IsOffMap { get; set; }
        public bool IsAtDestination { get; set; }
        public bool IsAtSource { get; set; }
        public bool IsBlockedNoMaterial { get; set; }
        public string LocationArea { get; set; } = "On Line";
        public string DestinationName { get; set; } = "Unassigned";
        public string DestinationArea { get; set; } = "Unassigned";
        public Color DestinationColor { get; set; } = new Color(0.58f, 0.62f, 0.69f, 1f);
        public WorkOrder? ActiveOrder { get; set; }
        public SuggestedCarJob? SuggestedJob { get; set; }
        public List<string> CoupledLocomotives { get; } = new List<string>();
        public List<string> CoupledWorkCars { get; } = new List<string>();
    }

    /// <summary>
    /// A supply source where a commodity can be loaded or ordered (Interchange, IndustrySeller, or MOW StorageLoader).
    /// </summary>
    public sealed class SupplyOriginInfo
    {
        public string Identifier { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string KindLabel { get; set; } = string.Empty;
        public string AreaName { get; set; } = string.Empty;
        public bool IsOffMapSource { get; set; }
        public Industry? Industry { get; set; }
        public IndustryComponent Component { get; set; } = null!;
        public OpsCarPosition Position { get; set; }
        public Load Load { get; set; } = null!;
        public float CurrentBuffer { get; set; }
        public float MaxBuffer { get; set; }
        public List<SupplyCarInfo> ActiveCars { get; } = new List<SupplyCarInfo>();

        public bool IsMowYardSource => string.Equals(KindLabel, "MOW Yard", StringComparison.OrdinalIgnoreCase);
        public bool IsSellerSource => string.Equals(KindLabel, "Industry Seller", StringComparison.OrdinalIgnoreCase);

        public string FormatDropdownLabel()
        {
            string cleanName = Industry != null && !string.IsNullOrEmpty(Industry.DisplayName)
                ? Industry.DisplayName
                : DisplayName;
            string baseLabel = $"{KindLabel} - {cleanName}";
            if ((IsMowYardSource || (!IsOffMapSource && CurrentBuffer > 0.001f)) && Load != null)
            {
                return $"{baseLabel} ({PrettyConvenientMenu.UI.Common.FormatHelper.FormatQuantity(Mathf.Max(0f, CurrentBuffer), Load.units)} avail)";
            }
            return baseLabel;
        }

        public bool SuppliesLoad(Load load)
        {
            return load != null && Load != null && string.Equals(Load.id, load.id, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A single clickable commodity stock/demand bar on an MOW Yard, Engine Terminal, or Project Sink.
    /// </summary>
    public sealed class SupplyCommodityBar
    {
        public string FacilityId { get; set; } = string.Empty;
        public string FacilityDisplayName { get; set; } = string.Empty;
        public string AreaName { get; set; } = string.Empty;
        public Color AreaColor { get; set; } = Color.white;
        public Load Load { get; set; } = null!;
        public float CurrentAmount { get; set; }
        public float DeliveryAmount { get; set; }
        public float OutgoingAmount { get; set; }
        public float CommittedOutboundAmount { get; set; }
        public float MaxCapacity { get; set; }
        public bool HasTarget { get; set; }
        public float TargetAmount { get; set; }
        public float RemainingDemand { get; set; }
        public float FleetInboundAmount { get; set; }
        public float NonFleetInboundAmount { get; set; }
        public bool IsSinkDemandOnly { get; set; }
        public bool IsTargetEditable { get; set; }
        public OpsCarPosition? DestinationPosition { get; set; }
        public WorkOrderDestinationKind DestinationKind { get; set; }
        public string? SinkId { get; set; }

        public float TotalOutboundAmount => OutgoingAmount + CommittedOutboundAmount;
        public float EffectiveBarScale => Mathf.Max(MaxCapacity, HasTarget ? TargetAmount : 0f);
        public float StockRatio => EffectiveBarScale > 0.001f ? Mathf.Clamp01(CurrentAmount / EffectiveBarScale) : 0f;
        public float DeliveryRatio => EffectiveBarScale > 0.001f ? Mathf.Clamp01(DeliveryAmount / EffectiveBarScale) : 0f;
        public float OutgoingRatio => EffectiveBarScale > 0.001f ? Mathf.Clamp01(OutgoingAmount / EffectiveBarScale) : 0f;
        public float CommittedOutboundRatio => EffectiveBarScale > 0.001f ? Mathf.Clamp01(CommittedOutboundAmount / EffectiveBarScale) : 0f;
        public float FleetInboundRatio => EffectiveBarScale > 0.001f ? Mathf.Clamp01(FleetInboundAmount / EffectiveBarScale) : 0f;
        public float? TargetRatio => HasTarget && TargetAmount > 0.001f && EffectiveBarScale > 0.001f
            ? Mathf.Clamp01(TargetAmount / EffectiveBarScale)
            : (float?)null;
        public float? StorageCapRatio => HasTarget && TargetAmount > MaxCapacity + 0.001f && MaxCapacity > 0.001f && EffectiveBarScale > 0.001f
            ? Mathf.Clamp01(MaxCapacity / EffectiveBarScale)
            : (float?)null;

        public bool AllowsMowYardSources =>
            DestinationKind == WorkOrderDestinationKind.MowYard ||
            DestinationKind == WorkOrderDestinationKind.WorkCar ||
            DestinationKind == WorkOrderDestinationKind.PlannedRepair;

        public bool AcceptsLoad(Load load)
        {
            return DestinationPosition.HasValue && load != null && Load != null &&
                   string.Equals(Load.id, load.id, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A facility card in Layer 2 (MOW Yards) or Layer 2 (Engine Terminals / Repair Tracks).
    /// </summary>
    public sealed class SupplyFacilityNode
    {
        public string FacilityId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AreaName { get; set; } = string.Empty;
        public Color AreaColor { get; set; } = Color.white;
        public Industry Industry { get; set; } = null!;
        public List<SupplyCommodityBar> Bars { get; } = new List<SupplyCommodityBar>();
        public List<SupplyCarInfo> ActiveCars { get; } = new List<SupplyCarInfo>();
        public bool HasRepairAlert { get; set; }
        public string? AlertText { get; set; }

        public bool OwnsDestination(string destinationId)
        {
            for (int i = 0; i < Bars.Count; i++)
            {
                if (Bars[i].DestinationPosition.HasValue &&
                    string.Equals(Bars[i].DestinationPosition!.Value.Identifier, destinationId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A Layer 3 MOW Sink (active TrackConstructionMilestone or user-keyed PlannedRepairDemand).
    /// </summary>
    public sealed class SupplyProjectSink
    {
        public string SinkId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsConstructionMilestone { get; set; }
        public float ProgressRatio { get; set; }
        public string? AssignedWorkCarId { get; set; }
        public string StagingLabel { get; set; } = "Unassigned Staging";
        public Color AreaColor { get; set; } = new Color(0.90f, 0.66f, 0.24f, 1f);
        public List<SupplyCommodityBar> Bars { get; } = new List<SupplyCommodityBar>();
        public List<SupplyCarInfo> ActiveCars { get; } = new List<SupplyCarInfo>();
    }

    /// <summary>
    /// A physically coupled cut/string of tracked supply cars, ordered in physical coupled sequence
    /// and tagged with its nearest MilePost and Area for milepost-ordered display on the Fleet tab.
    /// </summary>
    public sealed class CoupledCarString
    {
        public string AreaName { get; set; } = "On Line";
        public bool IsOffMap { get; set; }
        public bool HasMilePost { get; set; }
        public float NearestMileage { get; set; } = float.MaxValue;
        public string MilePostLabel { get; set; } = string.Empty;
        public List<SupplyCarInfo> Cars { get; } = new List<SupplyCarInfo>();

        public string DisplayHeader
        {
            get
            {
                if (IsOffMap)
                {
                    return $"Off-Map · {AreaName}";
                }

                return HasMilePost
                    ? $"{MilePostLabel} · {AreaName}"
                    : AreaName;
            }
        }
    }

    /// <summary>
    /// Empowered domain collection &amp; live network snapshot rendered by the Supply &amp; MOW Fleet Manager.
    /// Owns its own area color mapping, source lookups, unload destination lookups, coupled string grouping, and car-to-node routing.
    /// </summary>
    public sealed class SupplyNetworkSnapshot
    {
        private static readonly Color UnassignedColor = new Color(0.58f, 0.62f, 0.69f, 1f);

        private readonly Dictionary<string, Color> areaColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private int nextAreaColorIndex;

        public List<SupplyCarInfo> FleetCars { get; } = new List<SupplyCarInfo>();
        public List<CoupledCarString> CoupledStrings { get; } = new List<CoupledCarString>();
        public List<Car> AvailableWorkCars { get; } = new List<Car>();

        public List<SupplyOriginInfo> MowOrigins { get; } = new List<SupplyOriginInfo>();
        public List<SupplyFacilityNode> MowYards { get; } = new List<SupplyFacilityNode>();
        public List<SupplyProjectSink> MowSinks { get; } = new List<SupplyProjectSink>();

        public List<SupplyOriginInfo> FuelOrigins { get; } = new List<SupplyOriginInfo>();
        public List<SupplyFacilityNode> FuelSinks { get; } = new List<SupplyFacilityNode>();

        public IReadOnlyDictionary<string, Color> AreaColors => areaColors;

        public void Clear()
        {
            FleetCars.Clear();
            CoupledStrings.Clear();
            AvailableWorkCars.Clear();
            MowOrigins.Clear();
            MowYards.Clear();
            MowSinks.Clear();
            FuelOrigins.Clear();
            FuelSinks.Clear();
            areaColors.Clear();
            nextAreaColorIndex = 0;
        }

        public Color ResolveAreaColor(string? areaName)
        {
            if (string.IsNullOrWhiteSpace(areaName) || string.Equals(areaName, "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                return UnassignedColor;
            }

            if (!areaColors.TryGetValue(areaName!, out Color col))
            {
                col = LandingColors.GetSwatch(nextAreaColorIndex++).Color;
                areaColors[areaName!] = col;
            }

            return col;
        }

        /// <summary>
        /// Returns all valid loading sources for the given destination commodity bar,
        /// excluding the destination facility itself and respecting MOW Yard source rules.
        /// </summary>
        public List<SupplyOriginInfo> FindSourcesFor(SupplyCommodityBar destinationBar)
        {
            List<SupplyOriginInfo> results = new List<SupplyOriginInfo>();
            if (destinationBar == null || destinationBar.Load == null)
            {
                return results;
            }

            bool allowMowYards = destinationBar.AllowsMowYardSources;
            void CollectFrom(List<SupplyOriginInfo> pool)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    SupplyOriginInfo origin = pool[i];
                    if (!origin.SuppliesLoad(destinationBar.Load))
                    {
                        continue;
                    }
                    if (!allowMowYards && origin.IsMowYardSource)
                    {
                        continue;
                    }
                    if (origin.Industry != null &&
                        string.Equals(origin.Industry.identifier, destinationBar.FacilityId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    results.Add(origin);
                }
            }

            CollectFrom(MowOrigins);
            CollectFrom(FuelOrigins);

            // Sort sources by destination-appropriate priority:
            // - Projects / Sinks: MOW Yard > Industry Seller > Interchange
            // - MOW Stockpiles:   Industry Seller > Interchange > MOW Yard
            // - Engine Terminals: Industry Seller > Interchange
            results.Sort((a, b) =>
            {
                int pa = GetSourcePriority(a, destinationBar.DestinationKind);
                int pb = GetSourcePriority(b, destinationBar.DestinationKind);
                int prioCmp = pa.CompareTo(pb);
                if (prioCmp != 0) return prioCmp;

                int bufCmp = b.CurrentBuffer.CompareTo(a.CurrentBuffer);
                if (bufCmp != 0) return bufCmp;

                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            return results;
        }

        private static int GetSourcePriority(SupplyOriginInfo origin, WorkOrderDestinationKind destKind)
        {
            switch (destKind)
            {
                case WorkOrderDestinationKind.WorkCar:
                case WorkOrderDestinationKind.PlannedRepair:
                    // Projects: MOW Yard > Industry Seller > Interchange
                    if (origin.IsMowYardSource) return 0;
                    if (origin.IsSellerSource) return 1;
                    return 2;

                case WorkOrderDestinationKind.MowYard:
                    // MOW Stockpiles: Industry Seller > Interchange > MOW Yard
                    if (origin.IsSellerSource) return 0;
                    if (origin.IsOffMapSource) return 1;
                    return 2;

                default:
                    // Engine Terminals / General: Industry Seller > Interchange > MOW Yard
                    if (origin.IsSellerSource) return 0;
                    if (origin.IsOffMapSource) return 1;
                    return 2;
            }
        }

        /// <summary>
        /// Finds all destination bars across MOW Yards, Project Sinks, and Engine Terminals that can unload the given load.
        /// </summary>
        public List<SupplyCommodityBar> FindCompatibleUnloadDestinations(Load? load)
        {
            List<SupplyCommodityBar> results = new List<SupplyCommodityBar>();
            if (load == null)
            {
                return results;
            }

            foreach (SupplyCommodityBar bar in EnumerateAllOrderableBars())
            {
                if (bar.AcceptsLoad(load))
                {
                    results.Add(bar);
                }
            }

            return results;
        }

        /// <summary>
        /// Finds all destination bars that can be served by at least one of the given unassigned cars.
        /// </summary>
        public List<SupplyCommodityBar> FindCompatibleOrderDestinations(IReadOnlyList<SupplyCarInfo> cars)
        {
            List<SupplyCommodityBar> results = new List<SupplyCommodityBar>();
            if (cars == null || cars.Count == 0)
            {
                return results;
            }

            foreach (SupplyCommodityBar bar in EnumerateAllOrderableBars())
            {
                for (int i = 0; i < cars.Count; i++)
                {
                    if (SupplyFleetStore.CanCarCarryLoad(cars[i].Car, bar.Load))
                    {
                        results.Add(bar);
                        break;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Splits a subset of fleet cars (e.g. cars belonging to one Destination, Work Order, or Location card)
        /// into their physically coupled strings, preserving physical order within each string and
        /// milepost order across strings.
        /// </summary>
        public List<List<SupplyCarInfo>> SplitIntoCoupledStrings(IReadOnlyList<SupplyCarInfo> groupCars)
        {
            List<List<SupplyCarInfo>> result = new List<List<SupplyCarInfo>>();
            if (groupCars == null || groupCars.Count == 0)
            {
                return result;
            }

            HashSet<string> allowedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < groupCars.Count; i++)
            {
                allowedIds.Add(groupCars[i].CarId);
            }

            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);

            // Walk pre-sorted CoupledStrings (already ordered by nearest MilePost and physical order inside each cut)
            for (int s = 0; s < CoupledStrings.Count; s++)
            {
                CoupledCarString cut = CoupledStrings[s];
                List<SupplyCarInfo>? slice = null;
                for (int c = 0; c < cut.Cars.Count; c++)
                {
                    SupplyCarInfo car = cut.Cars[c];
                    if (allowedIds.Contains(car.CarId) && emitted.Add(car.CarId))
                    {
                        slice ??= new List<SupplyCarInfo>();
                        slice.Add(car);
                    }
                }

                if (slice != null && slice.Count > 0)
                {
                    result.Add(slice);
                }
            }

            // Fallback for any car not found in CoupledStrings
            List<SupplyCarInfo>? remainder = null;
            for (int i = 0; i < groupCars.Count; i++)
            {
                if (emitted.Add(groupCars[i].CarId))
                {
                    remainder ??= new List<SupplyCarInfo>();
                    remainder.Add(groupCars[i]);
                }
            }
            if (remainder != null && remainder.Count > 0)
            {
                result.Add(remainder);
            }

            return result;
        }

        private IEnumerable<SupplyCommodityBar> EnumerateAllOrderableBars()
        {
            for (int i = 0; i < MowYards.Count; i++)
            {
                foreach (SupplyCommodityBar bar in MowYards[i].Bars)
                {
                    if (bar.DestinationPosition.HasValue) yield return bar;
                }
            }

            for (int i = 0; i < MowSinks.Count; i++)
            {
                foreach (SupplyCommodityBar bar in MowSinks[i].Bars)
                {
                    if (bar.DestinationPosition.HasValue) yield return bar;
                }
            }

            for (int i = 0; i < FuelSinks.Count; i++)
            {
                foreach (SupplyCommodityBar bar in FuelSinks[i].Bars)
                {
                    if (bar.DestinationPosition.HasValue) yield return bar;
                }
            }
        }

        /// <summary>
        /// Routes active-order fleet cars AND tracked cars physically spotted at a facility/origin onto their respective nodes.
        /// </summary>
        internal void AttachActiveCarsToNodes()
        {
            Dictionary<string, HashSet<string>> spottedCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < FleetCars.Count; i++)
            {
                SupplyCarInfo carInfo = FleetCars[i];
                WorkOrder? order = carInfo.ActiveOrder;
                if (order != null)
                {
                    if (!order.HasBeenLoaded && !string.IsNullOrEmpty(order.SourceId))
                    {
                        AttachToOrigin(MowOrigins, order.SourceId!, carInfo);
                        AttachToOrigin(FuelOrigins, order.SourceId!, carInfo);
                    }

                    bool attached = false;
                    if (!string.IsNullOrEmpty(order.SinkId))
                    {
                        for (int s = 0; s < MowSinks.Count; s++)
                        {
                            if (string.Equals(MowSinks[s].SinkId, order.SinkId, StringComparison.OrdinalIgnoreCase))
                            {
                                AddUnique(MowSinks[s].ActiveCars, carInfo);
                                attached = true;
                                break;
                            }
                        }
                    }

                    if (!attached && order.IsPassiveWorkTrain)
                    {
                        for (int s = 0; s < MowSinks.Count; s++)
                        {
                            if (order.IsPassiveForWorkCar(MowSinks[s].AssignedWorkCarId, SupplyFleetStore.Shared))
                            {
                                AddUnique(MowSinks[s].ActiveCars, carInfo);
                                attached = true;
                                break;
                            }
                        }
                    }

                    if (!attached)
                    {
                        attached = AttachToFacility(MowYards, order.DestinationId, carInfo)
                                || AttachToFacility(FuelSinks, order.DestinationId, carInfo);
                    }

                    continue;
                }

                // Unassigned tracked cars: show them on the MOW Yard, Engine Terminal, or Origin where they are currently spotted/waybilled
                AttachSpottedOrWaybilledCar(carInfo, spottedCache);
            }
        }

        private void AttachSpottedOrWaybilledCar(SupplyCarInfo carInfo, Dictionary<string, HashSet<string>> spottedCache)
        {
            Car car = carInfo.Car;
            if (car == null) return;

            // 1. Check active vanilla waybill destination (exact component ID or industry match)
            if (car.Waybill.HasValue)
            {
                string destId = car.Waybill.Value.Destination.Identifier;
                if (!string.IsNullOrEmpty(destId))
                {
                    if (AttachToFacility(MowYards, destId, carInfo) ||
                        AttachToFacility(FuelSinks, destId, carInfo))
                    {
                        return;
                    }

                    bool matchedOrigin = AttachToOrigin(MowOrigins, destId, carInfo);
                    matchedOrigin |= AttachToOrigin(FuelOrigins, destId, carInfo);
                    if (matchedOrigin)
                    {
                        return;
                    }
                }

                bool matchedIndustry = AttachToOriginByIndustryWaybill(MowOrigins, car.Waybill, carInfo);
                matchedIndustry |= AttachToOriginByIndustryWaybill(FuelOrigins, car.Waybill, carInfo);
                if (matchedIndustry)
                {
                    return;
                }
            }

            // 2. Otherwise check exact spur/track spotting via OpsController.CarsAtPosition
            if (car.IsInBardo || OpsController.Shared == null) return;

            if (TryAttachBySpottedTrack(MowYards, carInfo, spottedCache)) return;
            if (TryAttachBySpottedTrack(FuelSinks, carInfo, spottedCache)) return;
            TryAttachOriginBySpottedTrack(MowOrigins, carInfo, spottedCache);
            TryAttachOriginBySpottedTrack(FuelOrigins, carInfo, spottedCache);
        }

        private static bool IsCarSpottedAt(string carId, OpsCarPosition pos, Dictionary<string, HashSet<string>> spottedCache)
        {
            string posId = pos.Identifier;
            if (string.IsNullOrEmpty(posId) || OpsController.Shared == null)
            {
                return false;
            }

            if (!spottedCache.TryGetValue(posId, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    IEnumerable<Car>? carsOnTrack = OpsController.Shared.CarsAtPosition(pos);
                    if (carsOnTrack != null)
                    {
                        foreach (Car c in carsOnTrack)
                        {
                            if (c != null) set.Add(c.id);
                        }
                    }
                }
                catch
                {
                    // Ignore transient track lookup errors
                }
                spottedCache[posId] = set;
            }

            return set.Contains(carId);
        }

        private static bool TryAttachBySpottedTrack(
            List<SupplyFacilityNode> nodes,
            SupplyCarInfo carInfo,
            Dictionary<string, HashSet<string>> spottedCache)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                SupplyFacilityNode node = nodes[i];
                for (int b = 0; b < node.Bars.Count; b++)
                {
                    if (!node.Bars[b].DestinationPosition.HasValue) continue;
                    if (IsCarSpottedAt(carInfo.CarId, node.Bars[b].DestinationPosition!.Value, spottedCache))
                    {
                        AddUnique(node.ActiveCars, carInfo);
                        return true;
                    }
                }
            }
            return false;
        }

        private static void TryAttachOriginBySpottedTrack(
            List<SupplyOriginInfo> origins,
            SupplyCarInfo carInfo,
            Dictionary<string, HashSet<string>> spottedCache)
        {
            for (int i = 0; i < origins.Count; i++)
            {
                if (origins[i].IsMowYardSource) continue;
                if (IsCarSpottedAt(carInfo.CarId, origins[i].Position, spottedCache))
                {
                    AddUnique(origins[i].ActiveCars, carInfo);
                    return;
                }
            }
        }

        private static bool AttachToOriginByIndustryWaybill(
            List<SupplyOriginInfo> origins,
            Waybill? waybill,
            SupplyCarInfo carInfo)
        {
            if (!waybill.HasValue) return false;
            for (int i = 0; i < origins.Count; i++)
            {
                if (origins[i].Industry != null && CarTrackingHelper.IsWaybilledHere(waybill, origins[i].Industry))
                {
                    AddUnique(origins[i].ActiveCars, carInfo);
                    return true;
                }
            }
            return false;
        }

        private static bool AttachToOrigin(List<SupplyOriginInfo> origins, string sourceId, SupplyCarInfo carInfo)
        {
            for (int i = 0; i < origins.Count; i++)
            {
                if (string.Equals(origins[i].Identifier, sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(origins[i].ActiveCars, carInfo);
                    return true;
                }
            }

            return false;
        }

        private static bool AttachToFacility(List<SupplyFacilityNode> nodes, string destId, SupplyCarInfo carInfo)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].OwnsDestination(destId))
                {
                    AddUnique(nodes[i].ActiveCars, carInfo);
                    return true;
                }
            }

            return false;
        }

        private static void AddUnique(List<SupplyCarInfo> list, SupplyCarInfo carInfo)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].CarId, carInfo.CarId, StringComparison.Ordinal))
                {
                    return;
                }
            }
            list.Add(carInfo);
        }

        /// <summary>
        /// Detects suggested jobs for unassigned supply cars strictly from a complete set of
        /// Railroader Auto-Waybill settings (both WhenEmpty = valid supply Source AND WhenLoaded = valid supply Destination).
        /// </summary>
        internal void EvaluateSuggestedJobs()
        {
            if (OpsController.Shared == null)
            {
                return;
            }

            for (int i = 0; i < FleetCars.Count; i++)
            {
                SupplyCarInfo carInfo = FleetCars[i];
                carInfo.SuggestedJob = null;

                if (carInfo.ActiveOrder != null || carInfo.Car == null || carInfo.Car.CarType.IsMaintenanceOfWay)
                {
                    continue;
                }

                ResolvedAutoDestination? whenEmpty = carInfo.Car.GetAutoDestination(AutoDestinationType.Empty, OpsController.Shared);
                ResolvedAutoDestination? whenLoaded = carInfo.Car.GetAutoDestination(AutoDestinationType.Load, OpsController.Shared);

                // Strictly require a full complete pair of auto-waybill settings
                if (!whenEmpty.HasValue || !whenLoaded.HasValue)
                {
                    continue;
                }

                string sourceId = whenEmpty.Value.Position.Identifier;
                string destId = whenLoaded.Value.Position.Identifier;
                if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(destId))
                {
                    continue;
                }

                SupplyOriginInfo? matchedSource = FindOriginByIdentifier(sourceId);
                if (matchedSource == null)
                {
                    continue;
                }

                SupplyCommodityBar? matchedDest = FindDestinationBarByIdentifierAndLoad(destId, matchedSource.Load);
                if (matchedDest == null)
                {
                    continue;
                }

                // Only suggest DirectUnload when the car is actually FULL with the matching load;
                // partially loaded cars (e.g. mid-loading at a seller) must stay on LoadAndDeliver to finish loading.
                bool isFullyLoadedWithMatch = carInfo.IsLoaded &&
                    carInfo.FillRatio >= 0.99f &&
                    carInfo.CurrentLoad != null &&
                    string.Equals(carInfo.CurrentLoad.id, matchedSource.Load.id, StringComparison.OrdinalIgnoreCase);

                carInfo.SuggestedJob = new SuggestedCarJob
                {
                    Kind = isFullyLoadedWithMatch ? SuggestedJobKind.DirectUnload : SuggestedJobKind.LoadAndDeliver,
                    DestinationBar = matchedDest,
                    SourceOrigin = matchedSource,
                    Summary = $"{matchedSource.Load.description}: {matchedSource.DisplayName} -> {matchedDest.FacilityDisplayName}"
                };
            }
        }

        private SupplyOriginInfo? FindOriginByIdentifier(string identifier)
        {
            for (int i = 0; i < MowOrigins.Count; i++)
            {
                if (string.Equals(MowOrigins[i].Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return MowOrigins[i];
                }
            }

            for (int i = 0; i < FuelOrigins.Count; i++)
            {
                if (string.Equals(FuelOrigins[i].Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return FuelOrigins[i];
                }
            }

            return null;
        }

        private SupplyCommodityBar? FindDestinationBarByIdentifierAndLoad(string destIdentifier, Load load)
        {
            foreach (SupplyCommodityBar bar in EnumerateAllOrderableBars())
            {
                if (bar.DestinationPosition.HasValue &&
                    string.Equals(bar.DestinationPosition.Value.Identifier, destIdentifier, StringComparison.OrdinalIgnoreCase) &&
                    bar.AcceptsLoad(load))
                {
                    return bar;
                }
            }

            return null;
        }
    }
}
