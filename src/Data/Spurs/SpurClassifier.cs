using System.Collections.Generic;
using Model;
using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;

namespace PrettyConvenientMenu.Data.Spurs
{
    /// <summary>
    /// Classifies physical industry spur tracks and assigns their operational roles.
    /// Groups co-located track components and coordinates car evaluation.
    /// </summary>
    public static class SpurClassifier
    {
        /// <summary>
        /// Resolves the operational role and UI badge style for a spur based purely on its track capabilities.
        /// Unhandled track configurations resolve to Unknown so they are surfaced immediately.
        /// </summary>
        public static CarGroupRole ResolveSpurRole(SpurCapabilities caps)
        {
            if (caps.CanStorageTransfer)
            {
                return CarGroupRole.MowMaterialHandling;
            }

            if (caps.CanRepair)
            {
                return CarGroupRole.LocomotiveRepair;
            }

            if (caps.CanTeamTrack)
            {
                return CarGroupRole.TeamTrack;
            }

            if (caps.CanLoad && caps.CanUnload)
            {
                return CarGroupRole.CommercialDual;
            }

            if (caps.CanLoad)
            {
                return CarGroupRole.CommercialOutbound;
            }

            if (caps.CanUnload)
            {
                return CarGroupRole.CommercialInbound;
            }

            return CarGroupRole.Unknown;
        }

        /// <summary>
        /// Groups all operational track components across an industry by physical track name
        /// and classifies each whole physical spur once.
        /// </summary>
        public static void ClassifyAllSpurs(
            Industry industry,
            FacilitySnapshot snapshot,
            OpsController? opsController = null)
        {
            List<IndustryComponent>? components = industry.Components;
            if (components == null || components.Count == 0) return;

            opsController ??= OpsController.Shared;
            if (opsController == null) return;

            Dictionary<int, List<IndustryComponent>> spursByTrack = new Dictionary<int, List<IndustryComponent>>();

            foreach (IndustryComponent comp in components)
            {
                if (comp is not (IndustryLoaderBase or IndustryUnloader or RepairTrack or TeamTrack or StorageLoader or StorageUnloader or IndustrySeller))
                {
                    continue;
                }

                if (comp.trackSpans == null || comp.trackSpans.Length == 0)
                {
                    continue;
                }

                int trackHash = comp.TrackGrouping();
                if (!spursByTrack.TryGetValue(trackHash, out List<IndustryComponent> spurList))
                {
                    spurList = new List<IndustryComponent>(2);
                    spursByTrack[trackHash] = spurList;
                }
                spurList.Add(comp);
            }

            foreach (List<IndustryComponent> spurComponents in spursByTrack.Values)
            {
                string trackName = spurComponents[0].ShortName(industry) ?? spurComponents[0].DisplayName ?? "Spur";
                ClassifySpur(trackName, spurComponents, industry, snapshot, opsController);
            }
        }

        /// <summary>
        /// Classifies a physical spur track by its components and delegates its spotted cars for evaluation.
        /// </summary>
        public static void ClassifySpur(
            string trackName,
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            Industry industry,
            FacilitySnapshot snapshot,
            OpsController opsController)
        {
            if (componentsOnSpur == null || componentsOnSpur.Count == 0 || opsController == null)
            {
                return;
            }

            SpurCapabilities caps = SpurCapabilities.FromComponents(componentsOnSpur);
            CarGroupRole role = ResolveSpurRole(caps);
            FacilityCarGroup spurGroup = snapshot.GetOrAddCarGroup(trackName, role);

            IEnumerable<Car>? carsOnTrack = opsController.CarsAtPosition(componentsOnSpur[0]);
            if (carsOnTrack == null) return;

            SpottedCarClassifier.ClassifySpurCars(
                spurGroup, componentsOnSpur, carsOnTrack,
                industry, snapshot, opsController, caps, role);
        }
    }
}
