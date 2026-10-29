using RollingStock;
using UI;
using UI.Tooltips;
using UnityEngine.EventSystems;

namespace PrettyConvenientMenu.Interaction
{
    /// <summary>
    /// Evaluates input and game UI state to determine whether a world click is valid for industry interaction.
    /// </summary>
    internal static class WorldInteractionGatekeeper
    {
        public static bool CanProcessWorldClick()
        {
            // 1. Check for primary mouse click via Railroader's active Input System
            if (GameInput.Shared is not { PrimaryPressStartedThisFrame: true })
            {
                return false;
            }

            // 2. Ignore click if pointer is hovering over an interactive game UI menu (Unity-safe null check)
            EventSystem? es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
            {
                return false;
            }

            if (UIStateArbiter.ContextMenuShown || global::UI.ContextMenu.ContextMenu.IsShown)
            {
                return false;
            }

            TrackLocationPicker? picker = TrackLocationPicker.Shared;
            if (picker != null && picker.IsPicking)
            {
                return false;
            }

            UITooltipController? tooltipController = UITooltipController.Shared;
            if (tooltipController != null && tooltipController.IsMouseOverUI)
            {
                return false;
            }

            // 3. Ignore click if mouse is actively hovering over an interactive game object (lever, switch, cab control, turntable)
            ObjectPicker? objectPicker = ObjectPicker.Shared;
            if (objectPicker != null)
            {
                IPickable? hovered = objectPicker.HoveringOver;
                if ((hovered as UnityEngine.Object) != null && hovered is not IndustryContentHoverable)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
