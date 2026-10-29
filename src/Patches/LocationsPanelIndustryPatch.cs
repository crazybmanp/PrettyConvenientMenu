using System;
using System.Reflection;
using HarmonyLib;
using Model.Ops;
using PrettyConvenientMenu.UI.Windows;
using UI.Builder;
using UI.CompanyWindow;

namespace PrettyConvenientMenu.Patches
{
    [HarmonyPatch]
    public static class LocationsPanelIndustryPatch
    {
        [HarmonyTargetMethod]
        public static MethodBase? TargetMethod()
        {
            Type? nestedType = AccessTools.Inner(typeof(LocationsPanelBuilder), "IndustryDetailBuilder");
            return nestedType is null ? null : AccessTools.Method(nestedType, "AddContractSection");
        }

        [HarmonyPostfix]
        public static void Postfix(object? __instance, UIPanelBuilder builder)
        {
            if (__instance is null)
            {
                return;
            }

            FieldInfo? industryField = AccessTools.Field(__instance.GetType(), "_industry");
            if (industryField?.GetValue(__instance) is not Industry industry)
            {
                return;
            }

            builder.AddSection("Production Monitor", b =>
            {
                b.AddButton("Open Detailed Production Monitor", () =>
                {
                    IndustryProductionWindow.Open(industry);
                });
            });
        }
    }
}
