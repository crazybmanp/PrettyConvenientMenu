using System;
using System.Collections.Generic;
using Game.State;
using Model;
using PrettyConvenientMenu.Data.SupplyFleet;
using UI.Builder;
using UI.Extensions;

namespace PrettyConvenientMenu.UI.SupplyFleet
{
    /// <summary>
    /// Renders the Work Train Mode selector (Passive / Work / Restock) into the Car Inspector
    /// Operations tab for MOW Work Cars via Railroader's native UIExtensionRegistry.
    /// </summary>
    public static class WorkTrainInspectorSection
    {
        private const string ExtensionPointId = "carInspector.operations";
        private const string ExtensionId = "CRZBP.APrettyConvenientMenu.WorkTrainMode";

        private static readonly List<string> WorkTrainModeLabels = new List<string>
        {
            "Passive (Manual)",
            "Work (Assemble to Train)",
            "Restock (Fill at MOW Depot)"
        };

        public static void Register()
        {
            UIExtensionRegistry.RegisterExtension<Car>(
                ExtensionPointId,
                ExtensionId,
                null,
                priority: 5,
                BuildSection);
        }

        private static void BuildSection(UIPanelBuilder builder, Car car)
        {
            if (car == null || !car.CarType.IsMaintenanceOfWay)
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

            Car rootWorkCar = store.ResolveRootWorkCar(car);
            WorkTrainMode activeMode = store.GetWorkTrainMode(rootWorkCar);
            bool smartMode = store.GetWorkTrainSmartMode(rootWorkCar);
            int assignedCount = store.CountCarsAssignedToWorkTrain(rootWorkCar);

            builder.AddSection("Work Train", section =>
            {
                section.FieldLabelWidth = 100f;

                if (!string.Equals(rootWorkCar.id, car.id, StringComparison.Ordinal))
                {
                    section.AddField("Pooled Under", rootWorkCar.DisplayName);
                }

                section.AddField("Assigned Cars", $"{assignedCount} car{(assignedCount == 1 ? "" : "s")}");

                if (StateManager.HasTrainmasterAccess)
                {
                    section.AddField("Mode", section.AddDropdown(WorkTrainModeLabels, (int)activeMode, idx =>
                    {
                        store.SetWorkTrainMode(rootWorkCar, (WorkTrainMode)idx);
                        section.Rebuild();
                    }));

                    if (activeMode == WorkTrainMode.Restock)
                    {
                        section.AddField("Smart Mode", section.HStack(row =>
                        {
                            row.ConfigureLayout(8f);
                            row.AddToggle(() => store.GetWorkTrainSmartMode(rootWorkCar), val =>
                            {
                                store.SetWorkTrainSmartMode(rootWorkCar, val);
                                section.Rebuild();
                            }).Tooltip("Smart Restock", "Assigns unset empty cars to the nearest MOW depot with available supply (or seller fallback) without mid-trip re-routing.");

                            if (smartMode)
                            {
                                row.AddButtonCompact("Re-evaluate", () =>
                                {
                                    store.ReevaluateSmartRestock(rootWorkCar);
                                    section.Rebuild();
                                }).Tooltip("Re-evaluate Empty Cars", "Clears and recalculates Smart Restock depots for all assigned cars.");
                            }
                        }));
                    }
                }
                else
                {
                    section.AddField("Mode", WorkTrainModeLabels[(int)activeMode]);
                    if (activeMode == WorkTrainMode.Restock)
                    {
                        section.AddField("Smart Mode", smartMode ? "On" : "Off");
                    }
                }

                string modeHint = activeMode switch
                {
                    WorkTrainMode.Work => "All assigned cars waybill to Coupled to this Work Car.",
                    WorkTrainMode.Restock => smartMode
                        ? "Restocks at nearest MOW depot with available supply (or seller fallback)."
                        : "Empty cars waybill to the nearest MOW depot for their product.",
                    _ => "Manual mode — assigned cars' waybills are not modified."
                };
                section.AddLabel($"<size=11><color=#B0BEC5>{modeHint}</color></size>");
            });
        }
    }
}
