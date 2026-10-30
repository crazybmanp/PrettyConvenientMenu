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
        public void Classify_WhenNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => FacilityClassifier.Classify(null!));
        }

        [Test]
        public void Classify_ComponentsDriveClassification()
        {
            // 1. RepairTrack -> EngineService
            Industry engineFacility = new Industry { identifier = "engine_facility" };
            engineFacility.Components.Add(new RepairTrack());
            Assert.AreEqual(FacilityType.EngineService, FacilityClassifier.Classify(engineFacility));

            // 2. StorageLoader -> MaintenanceOfWay
            Industry mowFacility = new Industry { identifier = "mow_facility" };
            mowFacility.Components.Add(new StorageLoader());
            Assert.AreEqual(FacilityType.MaintenanceOfWay, FacilityClassifier.Classify(mowFacility));

            // 3. StorageUnloader -> MaintenanceOfWay
            Industry mowFacility2 = new Industry { identifier = "mow_facility_2" };
            mowFacility2.Components.Add(new StorageUnloader());
            Assert.AreEqual(FacilityType.MaintenanceOfWay, FacilityClassifier.Classify(mowFacility2));

            // 4. TeamTrack without contract -> Depot
            Industry depotFacility = new Industry { identifier = "depot_facility", usesContract = false };
            depotFacility.Components.Add(new TeamTrack());
            Assert.AreEqual(FacilityType.Depot, FacilityClassifier.Classify(depotFacility));

            // 5. Commercial Loader -> CommercialFreight
            Industry freightFacility = new Industry { identifier = "freight_facility" };
            freightFacility.Components.Add(new IndustryLoader());
            Assert.AreEqual(FacilityType.CommercialFreight, FacilityClassifier.Classify(freightFacility));

            // 6. Formulaic Component -> CommercialFreight
            Industry formulaicFacility = new Industry { identifier = "formulaic_facility" };
            formulaicFacility.Components.Add(new FormulaicIndustryComponent());
            Assert.AreEqual(FacilityType.CommercialFreight, FacilityClassifier.Classify(formulaicFacility));

            // 7. Generic facility with no special components defaults to CommercialFreight
            Industry genericFacility = new Industry { identifier = "generic_facility" };
            Assert.AreEqual(FacilityType.CommercialFreight, FacilityClassifier.Classify(genericFacility));
        }
    }
}
