using System.Collections.Generic;
using Model.Ops;
using NUnit.Framework;
using PrettyConvenientMenu.Data;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class CollectorLifecycleTests
    {
        private class TestCollector : IFacilityDataCollector
        {
            public IndustryFacilityType FacilityType => IndustryFacilityType.CommercialFreight;
            public List<string> ExecutionLog { get; } = new List<string>();

            public void PopulateFacilityMetadata(Industry industry, IndustrySnapshot snapshot)
            {
                ExecutionLog.Add("Metadata");
            }

            public void PopulateBuffers(Industry industry, IndustrySnapshot snapshot)
            {
                ExecutionLog.Add("Buffers");
            }

            public void ScanEnRouteTraffic(Industry industry, IndustrySnapshot snapshot)
            {
                ExecutionLog.Add("EnRoute");
            }

            public void EvaluateFacilityDiagnostics(Industry industry, IndustrySnapshot snapshot)
            {
                ExecutionLog.Add("Diagnostics");
            }
        }

        [Test]
        public void ExecuteLifecycle_ExecutesCollectorStagesInOrder()
        {
            TestCollector collector = new TestCollector();
            Industry industry = new Industry { identifier = "test_ind" };
            IndustrySnapshot snapshot = new IndustrySnapshot();

            IndustryDataService.ExecuteLifecycle(collector, industry, snapshot);

            List<string> expectedOrder = new List<string>
            {
                "Metadata",
                "Buffers",
                "EnRoute",
                "Diagnostics"
            };

            CollectionAssert.AreEqual(expectedOrder, collector.ExecutionLog);
        }
    }
}
