using HarmonyLib;
using Model;
using PrettyConvenientMenu.UI.SupplyFleet;
using UI.Builder;
using UI.CarInspector;

namespace PrettyConvenientMenu.Patches
{
    /// <summary>
    /// Non-destructive CarInspector patches:
    /// - Registers our Work Train Mode UI extension on PopulateOperationsPanel.
    /// - Appends our compact Supply Fleet banner inside Automatic Waybills on PopulateSetWaybillPanel.
    /// </summary>
    [HarmonyPatch(typeof(CarInspector))]
    public static class CarInspectorPatches
    {
        [HarmonyPatch("PopulateOperationsPanel")]
        [HarmonyPrefix]
        public static void PopulateOperationsPanelPrefix()
        {
            WorkTrainInspectorSection.Register();
        }

        [HarmonyPatch("PopulateSetWaybillPanel")]
        [HarmonyPostfix]
        public static void PopulateSetWaybillPanelPostfix(Car ____car, UIPanelBuilder builder)
        {
            CarInspectorSupplyOrderSection.BuildAutoWaybillNotice(builder, ____car);
        }
    }
}
