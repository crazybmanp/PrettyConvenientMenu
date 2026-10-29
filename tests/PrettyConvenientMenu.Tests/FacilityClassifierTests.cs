using System;
using Model.Ops;
using NUnit.Framework;
using PrettyConvenientMenu.Data;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class FacilityClassifierTests
    {
        [Test]
        public void DetermineFacilityType_WhenNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => IndustryDataService.DetermineFacilityType(null!));
        }

        [Test]
        public void DetermineFacilityType_WithIdentifiers_ClassifiesCorrectly()
        {
            Industry industry = new Industry();

            industry.identifier = "bryson_depot";
            Assert.AreEqual(IndustryFacilityType.Depot, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "alarka_engine_house";
            Assert.AreEqual(IndustryFacilityType.EngineService, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "sylva_roundhouse";
            Assert.AreEqual(IndustryFacilityType.EngineService, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "coaling_tower";
            Assert.AreEqual(IndustryFacilityType.EngineService, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "mow_track_yard";
            Assert.AreEqual(IndustryFacilityType.MaintenanceOfWay, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "track_maintenance_depot";
            Assert.AreEqual(IndustryFacilityType.MaintenanceOfWay, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "whittier_sawmill";
            Assert.AreEqual(IndustryFacilityType.CommercialFreight, IndustryDataService.DetermineFacilityType(industry));
        }

        [Test]
        public void DetermineFacilityType_GuardsAgainstGenericSubstrings()
        {
            Industry industry = new Industry();

            // Previously, "house" matched "warehouse" and made it a Depot
            industry.identifier = "freight_warehouse";
            Assert.AreEqual(IndustryFacilityType.CommercialFreight, IndustryDataService.DetermineFacilityType(industry));

            industry.identifier = "powerhouse";
            Assert.AreEqual(IndustryFacilityType.CommercialFreight, IndustryDataService.DetermineFacilityType(industry));

            // Previously, "service" matched "freight_service" and made it EngineService
            industry.identifier = "industrial_service_spur";
            Assert.AreEqual(IndustryFacilityType.CommercialFreight, IndustryDataService.DetermineFacilityType(industry));
        }

        [Test]
        public void DetermineFacilityType_ComponentsDriveClassification_WithZeroKeywordsInIdentifier()
        {
            // 1. RepairTrack on a facility with generic identifier "facility_generic_01" -> EngineService
            Industry engineFacility = new Industry { identifier = "facility_generic_01" };
            RepairTrack repairTrack = new RepairTrack();
            engineFacility.Components.Add(repairTrack);

            Assert.AreEqual(
                IndustryFacilityType.EngineService,
                IndustryDataService.DetermineFacilityType(engineFacility),
                "RepairTrack must classify as EngineService even when identifier has zero engine keywords.");

            // 2. StorageLoader on a facility with generic identifier "facility_generic_02" -> MaintenanceOfWay
            Industry mowFacility = new Industry { identifier = "facility_generic_02" };
            StorageLoader storageLoader = new StorageLoader();
            mowFacility.Components.Add(storageLoader);

            Assert.AreEqual(
                IndustryFacilityType.MaintenanceOfWay,
                IndustryDataService.DetermineFacilityType(mowFacility),
                "StorageLoader must classify as MaintenanceOfWay even when identifier has zero mow keywords.");

            // 3. TeamTrack on a facility with generic identifier "facility_generic_03" and usesContract=false -> Depot
            Industry depotFacility = new Industry { identifier = "facility_generic_03", usesContract = false };
            TeamTrack teamTrack = new TeamTrack();
            depotFacility.Components.Add(teamTrack);

            Assert.AreEqual(
                IndustryFacilityType.Depot,
                IndustryDataService.DetermineFacilityType(depotFacility),
                "TeamTrack without contract must classify as Depot even when identifier has zero depot keywords.");

            // 4. Commercial Loader on a facility with an identifier that contains a depot keyword -> CommercialFreight
            Industry freightFacility = new Industry { identifier = "general_station_building" };
            IndustryLoader industryLoader = new IndustryLoader();
            freightFacility.Components.Add(industryLoader);

            Assert.AreEqual(
                IndustryFacilityType.CommercialFreight,
                IndustryDataService.DetermineFacilityType(freightFacility),
                "IndustryLoader must override string keywords and classify as CommercialFreight.");
        }
    }
}
