using System;
using Model;
using RollingStock;
using Track;
using UnityEngine;

namespace PrettyConvenientMenu.Interaction
{
    /// <summary>
    /// Filters out non-industry game entities (rolling stock, switches, turntables, pickable tools).
    /// Uses Unity-safe '==' and '!=' checks on all UnityEngine.Object types.
    /// </summary>
    internal static class InteractionHitFilter
    {
        public static bool ShouldIgnoreHit(Collider? hitCollider)
        {
            if (hitCollider == null || hitCollider.gameObject == null)
            {
                return true;
            }

            GameObject hitObject = hitCollider.gameObject;

            // 1. Ignore rolling stock clicks (player selecting or uncoupling a car)
            if (hitObject.GetComponentInParent<Car>() != null)
            {
                return true;
            }

            // 2. Ignore switch stand clicks (player throwing a switch)
            if (hitObject.GetComponentInParent<SwitchStand>() != null)
            {
                return true;
            }

            // 3. Ignore interactive pickable objects (turntable levers, station agents, signals, etc.)
            IPickable? pickable = hitObject.GetComponentInParent<IPickable>();
            if ((pickable as UnityEngine.Object) != null && pickable is not IndustryContentHoverable)
            {
                return true;
            }

            // 4. Ignore turntables and switch stands by hierarchy or component
            if (IsSwitchStandOrTurntable(hitObject))
            {
                return true;
            }

            return false;
        }

        private static bool IsSwitchStandOrTurntable(GameObject hitObject)
        {
            if (hitObject.GetComponentInParent<TurntableController>() != null ||
                hitObject.GetComponentInParent<TurntablePickable>() != null)
            {
                return true;
            }

            for (Transform? scan = hitObject.transform; scan != null; scan = scan.parent)
            {
                string name = scan.name;
                if (name.IndexOf("turntable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("switch_stand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("switchstand", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
