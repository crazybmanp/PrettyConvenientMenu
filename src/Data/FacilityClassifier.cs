using System;
using System.Collections.Generic;
using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Data
{
    public static class FacilityClassifier
    {
        public static FacilityType Classify(Industry industry)
        {
            if (industry == null)
            {
                throw new ArgumentNullException(nameof(industry));
            }

            List<IndustryComponent>? components = industry.Components;
            if (components != null)
            {
                bool hasStorageLoaderOrUnloader = false;
                bool hasRepairTrack = false;
                bool hasTeamTrack = false;
                bool hasOutputLoader = false;
                bool hasServiceUnloader = false;
                bool hasProgression = false;

                foreach (IndustryComponent c in components)
                {
                    if (c is StorageLoader or StorageUnloader)
                    {
                        hasStorageLoaderOrUnloader = true;
                    }
                    if (c is RepairTrack)
                    {
                        hasRepairTrack = true;
                    }
                    if (c is TeamTrack)
                    {
                        hasTeamTrack = true;
                    }
                    if (c is ProgressionIndustryComponent or FormulaicIndustryComponent)
                    {
                        hasProgression = true;
                    }
                    if (c is IndustryLoader)
                    {
                        hasOutputLoader = true;
                    }
                    if (c is IndustryUnloader unloader && unloader.load != null)
                    {
                        string loadId = (unloader.load.id ?? string.Empty).ToLowerInvariant();
                        if (loadId.Contains("diesel") || loadId.Contains("coal") || loadId.Contains("sand") || loadId.Contains("water"))
                        {
                            hasServiceUnloader = true;
                        }
                    }
                }

                // 1. MOW Supply Tracks (ties, rails, ballast)
                if (hasStorageLoaderOrUnloader)
                {
                    return FacilityType.MaintenanceOfWay;
                }

                // 2. Equipment / Locomotive Repair Tracks
                if (hasRepairTrack)
                {
                    return FacilityType.EngineService;
                }

                // 3. Commercial Progression / Production Loaders
                if (hasProgression || hasOutputLoader)
                {
                    return FacilityType.CommercialFreight;
                }

                // 4. Dedicated Service Fuel / Sand / Coal / Water Unloaders without Freight Contracts
                if (hasServiceUnloader && !industry.usesContract)
                {
                    return FacilityType.EngineService;
                }

                // 5. Team Tracks without Freight Contracts (Passenger / Depot)
                if (hasTeamTrack && !industry.usesContract)
                {
                    return FacilityType.Depot;
                }
            }

            return FacilityType.CommercialFreight;
        }
    }
}
