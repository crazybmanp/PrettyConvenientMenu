using System;
using Helpers;
using Model.Ops;
using PrettyConvenientMenu.UI.Windows;
using Track;
using UnityEngine;

namespace PrettyConvenientMenu.Interaction
{
    /// <summary>
    /// Coordinates 3D world clicking to open the facility window.
    /// Delegates validation to <see cref="WorldInteractionGatekeeper"/>, exclusions to <see cref="InteractionHitFilter"/>,
    /// and spatial matching to <see cref="FacilityResolver"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FacilityWorldInteraction : MonoBehaviour
    {
        public static FacilityWorldInteraction? Instance { get; private set; }

        private const float maxRaycastDistance = 3000f;
        private Camera? cachedCamera;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            try
            {
                // 1. Gatekeep input and active UI states
                if (!WorldInteractionGatekeeper.CanProcessWorldClick())
                {
                    return;
                }

                // 2. Resolve main rendering camera (Unity-safe null check)
                if (!MainCameraHelper.TryGetIfNeeded(ref cachedCamera) || cachedCamera == null)
                {
                    return;
                }

                // 3. Track Geometry Check (clicking on rails inside an industry track span)
                if (Graph.Shared is { } graph && graph.LocationFromMouse(cachedCamera) is { } trackLoc)
                {
                    if (FacilityResolver.TryResolveFromTrack(trackLoc, out Industry? trackIndustry))
                    {
                        OpenForIndustry(trackIndustry!);
                        return;
                    }
                }

                // 4. Physical Raycast Check (clicking on buildings, loading docks, or props)
                int layerMask = (1 << Layers.Default) | (1 << Layers.Clickable);
                Ray ray = cachedCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, layerMask))
                {
                    if (!InteractionHitFilter.ShouldIgnoreHit(hit.collider))
                    {
                        if (FacilityResolver.TryResolveFromHit(hit, out Industry? hitIndustry))
                        {
                            OpenForIndustry(hitIndustry!);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Error processing world click interaction in FacilityWorldInteraction.");
            }
        }

        public void OpenForIndustry(Industry? industry)
        {
            if (industry is null)
            {
                return;
            }

            FacilityWindow.Open(industry);
        }
    }
}
