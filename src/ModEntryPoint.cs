using System;
using System.Reflection;
using HarmonyLib;
using Railroader.Modding;
using UnityEngine;

namespace PrettyConvenientMenu
{
    public sealed class ModEntryPoint : IModEntryPoint, IWorldLifecycle
    {
        private static IModLogger? logger;
        public static IModLogger Log => logger ?? throw new InvalidOperationException("ModEntryPoint logger not initialized.");

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
                harmony = new Harmony("CRZBP.APrettyConvenientMenu");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony patches.");
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
            interactionHost.AddComponent<Interaction.IndustryWorldInteraction>();
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
