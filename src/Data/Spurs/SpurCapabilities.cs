using System.Collections.Generic;
using Model.Ops;

namespace PrettyConvenientMenu.Data.Spurs
{
    /// <summary>
    /// Immutable value struct defining the operational capabilities of a physical spur track.
    /// Derived by inspecting all co-located components residing on that physical track.
    /// </summary>
    public readonly struct SpurCapabilities
    {
        /// <summary>Indicates whether the spur has mechanical unloading equipment.</summary>
        public bool CanUnload { get; }

        /// <summary>Indicates whether the spur has mechanical loading equipment.</summary>
        public bool CanLoad { get; }

        /// <summary>Indicates whether the spur has rolling stock repair facilities.</summary>
        public bool CanRepair { get; }

        /// <summary>Indicates whether the spur handles contract/team track public freight.</summary>
        public bool CanTeamTrack { get; }

        /// <summary>Indicates whether the spur transfers maintenance-of-way storage supplies.</summary>
        public bool CanStorageTransfer { get; }

        public SpurCapabilities(
            bool canUnload,
            bool canLoad,
            bool canRepair,
            bool canTeamTrack,
            bool canStorageTransfer)
        {
            CanUnload = canUnload;
            CanLoad = canLoad;
            CanRepair = canRepair;
            CanTeamTrack = canTeamTrack;
            CanStorageTransfer = canStorageTransfer;
        }

        /// <summary>
        /// Derives the capability profile from all components residing on the physical track.
        /// </summary>
        /// <param name="components">The components co-located on the track.</param>
        /// <returns>The aggregated capability profile.</returns>
        public static SpurCapabilities FromComponents(IReadOnlyList<IndustryComponent>? components)
        {
            if (components == null || components.Count == 0)
            {
                return default;
            }

            bool canUnload = false;
            bool canLoad = false;
            bool canRepair = false;
            bool canTeamTrack = false;
            bool canStorageTransfer = false;

            for (int i = 0; i < components.Count; i++)
            {
                IndustryComponent comp = components[i];
                if (comp is IndustryUnloader) canUnload = true;
                else if (comp is IndustryLoaderBase or IndustrySeller) canLoad = true;
                else if (comp is RepairTrack) canRepair = true;
                else if (comp is TeamTrack) canTeamTrack = true;
                else if (comp is StorageUnloader or StorageLoader) canStorageTransfer = true;
            }

            return new SpurCapabilities(canUnload, canLoad, canRepair, canTeamTrack, canStorageTransfer);
        }
    }
}
