using System.Linq;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class SnapshotResetTests
    {
        [Test]
        public void IndustrySnapshot_Clear_ResetsAllFieldsAndCollections()
        {
            IndustrySnapshot snapshot = new IndustrySnapshot
            {
                IndustryId = "test_ind",
                DisplayName = "Test Industry",
                FacilityType = IndustryFacilityType.EngineService,
                CurrentTier = 2,
                TierDescription = "Tier 2",
                State = ProductionState.Running,
                StatusMessage = "Running",
                DiagnosticAlert = "Alert"
            };

            snapshot.Buffers.Add(new CommercialBuffer());

            SpurCarGroup inGroup = new SpurCarGroup { TrackName = "In Spur", Role = SpurRole.CommercialInbound };
            inGroup.Cars.Add(new SpottedCarInfo { ReportingMark = "IN-1" });
            snapshot.SpurGroups.Add(inGroup);

            SpurCarGroup outGroup = new SpurCarGroup { TrackName = "Out Spur", Role = SpurRole.CommercialOutbound };
            outGroup.Cars.Add(new SpottedCarInfo { ReportingMark = "OUT-1" });
            snapshot.SpurGroups.Add(outGroup);

            Assert.AreEqual(1, snapshot.InboundCars.Count());
            Assert.AreEqual(1, snapshot.OutboundCars.Count());
            Assert.AreEqual(2, snapshot.TotalCarCount);

            snapshot.Clear();

            Assert.AreEqual(string.Empty, snapshot.IndustryId);
            Assert.AreEqual(string.Empty, snapshot.DisplayName);
            Assert.AreEqual(IndustryFacilityType.CommercialFreight, snapshot.FacilityType);
            Assert.AreEqual(0, snapshot.CurrentTier);
            Assert.AreEqual(string.Empty, snapshot.TierDescription);
            Assert.AreEqual(ProductionState.Idle, snapshot.State);
            Assert.AreEqual(string.Empty, snapshot.StatusMessage);
            Assert.AreEqual(string.Empty, snapshot.DiagnosticAlert);

            Assert.IsEmpty(snapshot.Buffers);
            Assert.IsEmpty(snapshot.InboundCars);
            Assert.IsEmpty(snapshot.OutboundCars);
            Assert.IsEmpty(snapshot.SpurGroups);
            Assert.AreEqual(0, snapshot.TotalCarCount);
        }

        [Test]
        public void SpurCarGroup_Clear_ResetsFieldsAndCars()
        {
            SpurCarGroup group = new SpurCarGroup
            {
                TrackName = "Spur 1",
                Role = SpurRole.EnRouteWaybill
            };
            group.Cars.Add(new SpottedCarInfo());

            Assert.IsTrue(group.IsEnRoute);

            group.Clear();

            Assert.AreEqual(string.Empty, group.TrackName);
            Assert.AreEqual(SpurRole.CommercialInbound, group.Role);
            Assert.IsTrue(group.IsInbound);
            Assert.IsFalse(group.IsEnRoute);
            Assert.IsEmpty(group.Cars);
        }
    }
}
