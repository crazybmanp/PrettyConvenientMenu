using System;
using System.Collections.Generic;
using Helpers;
using Model.Ops;
using Track;
using UnityEngine;

namespace PrettyConvenientMenu.Interaction
{
    /// <summary>
    /// Resolves an Industry facility from track locations or raycast collider hits.
    /// Preserves Unity-safe null equality checks for UnityEngine.Object types.
    /// </summary>
    internal static class FacilityResolver
    {
        private const float SidingClearanceRadius = 50f;
        private const float CoarseIndustryRadiusSq = 400f * 400f;

        /// <summary>
        /// Attempts to match a track Location to an Industry containing that track span.
        /// </summary>
        public static bool TryResolveFromTrack(Location loc, out Industry? industry)
        {
            industry = null;
            if (OpsController.Shared is not { AllIndustries: { } allIndustries })
            {
                return false;
            }

            foreach (Industry? candidate in allIndustries)
            {
                if (candidate is not { Components: { } components })
                {
                    continue;
                }

                foreach (IndustryComponent? comp in components)
                {
                    if (comp is not { trackSpans: { } spans })
                    {
                        continue;
                    }

                    foreach (TrackSpan? span in spans)
                    {
                        if (span is not null && span.Contains(loc))
                        {
                            industry = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve an Industry from a raycast hit on a physical structure.
        /// </summary>
        public static bool TryResolveFromHit(RaycastHit hit, out Industry? industry)
        {
            industry = null;
            Collider? hitCollider = hit.collider;
            if (hitCollider == null || OpsController.Shared is not { AllIndustries: { } allIndustries })
            {
                return false;
            }

            GameObject hitObject = hitCollider.gameObject;
            if (hitObject == null)
            {
                return false;
            }

            // Strategy 1: Scene Hierarchy Name Matching
            if (TryResolveFromHierarchy(hitObject, allIndustries, out industry))
            {
                return true;
            }

            // Strategy 2: Prop Definition Identifier Matching
            if (TryResolveFromProp(hitObject, allIndustries, out industry))
            {
                return true;
            }

            // Strategy 3: Operational Track Spatial Footprint
            if (TryResolveFromSpatialProximity(hit, hitCollider, allIndustries, out industry))
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveFromHierarchy(GameObject hitObject, Industry[] allIndustries, out Industry? industry)
        {
            industry = null;
            for (Transform? current = hitObject.transform; current != null; current = current.parent)
            {
                string objName = current.gameObject.name;
                foreach (Industry? candidate in allIndustries)
                {
                    if (candidate is { identifier: { Length: > 0 } id } &&
                        (objName.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         candidate.name.Equals(objName, StringComparison.OrdinalIgnoreCase)))
                    {
                        industry = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryResolveFromProp(GameObject hitObject, Industry[] allIndustries, out Industry? industry)
        {
            industry = null;
            PropInstance? prop = hitObject.GetComponentInParent<PropInstance>();
            if (prop == null || string.IsNullOrEmpty(prop.identifier))
            {
                return false;
            }

            string propId = prop.identifier;
            foreach (Industry? candidate in allIndustries)
            {
                if (candidate is { identifier: { Length: > 0 } id } &&
                    propId.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    industry = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveFromSpatialProximity(RaycastHit hit, Collider hitCollider, Industry[] allIndustries, out Industry? industry)
        {
            industry = null;
            Bounds buildingBounds = hitCollider.bounds;

            PropInstance? prop = hitCollider.gameObject.GetComponentInParent<PropInstance>();
            if (prop != null)
            {
                Collider[] propColliders = prop.GetComponentsInChildren<Collider>();
                for (int i = 0; i < propColliders.Length; i++)
                {
                    Collider child = propColliders[i];
                    if (child != null && child.enabled)
                    {
                        buildingBounds.Encapsulate(child.bounds);
                    }
                }
            }

            Vector3 gamePoint = hit.point.WorldToGame();
            float bestDistanceSq = SidingClearanceRadius * SidingClearanceRadius;

            foreach (Industry? candidate in allIndustries)
            {
                if (candidate is not { Components: { } components })
                {
                    continue;
                }

                // Coarse spatial filter: skip industries outside the clicked town Area and far from the click
                bool inArea = candidate.Area is { } area && area.Contains(gamePoint);
                bool nearCenter = (candidate.Position - gamePoint).sqrMagnitude <= CoarseIndustryRadiusSq;
                if (!inArea && !nearCenter)
                {
                    continue;
                }

                foreach (IndustryComponent? comp in components)
                {
                    if (comp is not { trackSpans: { } spans })
                    {
                        continue;
                    }

                    foreach (TrackSpan? span in spans)
                    {
                        if (span is not { } validSpan)
                        {
                            continue;
                        }

                        IReadOnlyCollection<Vector3> spanPoints = validSpan.GetPoints();
                        foreach (Vector3 trackGamePoint in spanPoints)
                        {
                            Vector3 trackWorldPoint = trackGamePoint.GameToWorld();
                            Vector3 closestShellPoint = buildingBounds.ClosestPoint(trackWorldPoint);
                            float distSq = (closestShellPoint - trackWorldPoint).sqrMagnitude;
                            if (distSq <= bestDistanceSq)
                            {
                                bestDistanceSq = distSq;
                                industry = candidate;
                            }
                        }
                    }
                }
            }

            return industry is not null;
        }
    }
}
