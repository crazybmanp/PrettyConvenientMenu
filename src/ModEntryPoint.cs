using System;
using System.Reflection;
using HarmonyLib;
using PrettyConvenientMenu.Settings;
using PrettyConvenientMenu.UI.Preferences;
using Railroader.Modding;
using UnityEngine;

namespace PrettyConvenientMenu
{
    public sealed class ModEntryPoint : IModEntryPoint, IWorldLifecycle
    {
        private static IModLogger? logger;
        public static IModLogger Log => logger ?? NullModLogger.Instance;

        private sealed class NullModLogger : IModLogger
        {
            public static readonly NullModLogger Instance = new NullModLogger();
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warning(string message) { }
            public void Error(string message) { }
            public void Error(Exception exception, string message) { }
        }

        private Harmony? harmony;
        private GameObject? interactionHost;

        public void OnModLoad(IModContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            logger = context.Log;
            Log.Info("PrettyConvenientMenu initialized via Railroader.Modding contract.");

            try
            {
                ModSettings.Load(context.ModDirectory);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load ModSettings.");
            }

            try
            {
                harmony = new Harmony("CRZBP.APrettyConvenientMenu");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony patches.");
            }

            try
            {
                if (harmony != null && !string.IsNullOrEmpty(ModSettings.Current.FilePath))
                {
                    SettingsUi.Start(harmony, ModSettings.Current.FilePath!);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize SettingsUi.");
            }

            // If world was already loaded prior to mod load, initialize interaction host immediately
            if (Model.Ops.OpsController.Shared is not null && interactionHost is null)
            {
                OnWorldLoad();
            }
        }

        public void OnModUnload()
        {
            OnWorldUnload();

            try
            {
                SettingsUi.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop SettingsUi.");
            }
            ModSettings.Current.Save();
            harmony?.UnpatchAll("CRZBP.APrettyConvenientMenu");
            harmony = null;
            Log.Info("PrettyConvenientMenu unloaded.");
        }

        public void OnWorldLoad()
        {
            if (interactionHost is not null)
            {
                return;
            }

            Log.Info("World loaded: Initializing PrettyConvenientMenu World Interaction Host...");

            // Set up interaction listener for 3D world raycasting
            interactionHost = new GameObject("PrettyConvenientMenu_InteractionHost");
            UnityEngine.Object.DontDestroyOnLoad(interactionHost);
            interactionHost.AddComponent<Interaction.FacilityWorldInteraction>();
        }

        public void OnWorldUnload()
        {
            Log.Info("World unloaded: Cleaning up PrettyConvenientMenu Host...");

            if (interactionHost is not null)
            {
                UnityEngine.Object.Destroy(interactionHost);
                interactionHost = null;
            }
        }
    }
}
