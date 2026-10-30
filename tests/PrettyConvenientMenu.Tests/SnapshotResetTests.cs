using System.Linq;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class SnapshotResetTests
    {
        [Test]
        public void FacilitySnapshot_Clear_ResetsAllFieldsAndCollections()
        {
            FacilitySnapshot snapshot = new FacilitySnapshot
            {
                IndustryId = "test_ind",
                DisplayName = "Test Industry",
                FacilityType = FacilityType.EngineService,
                CurrentTier = 2,
                TierDescription = "Tier 2",
                State = ProductionState.Running,
                StatusMessage = "Running",
                DiagnosticAlert = "Alert"
            };

            snapshot.Buffers.Add(new CommercialBuffer());

            FacilityCarGroup inGroup = new FacilityCarGroup { TrackName = "In Spur", Role = CarGroupRole.CommercialInbound };
            inGroup.Cars.Add(new SpottedCarInfo { ReportingMark = "IN-1" });
            snapshot.CarGroups.Add(inGroup);

            FacilityCarGroup outGroup = new FacilityCarGroup { TrackName = "Out Spur", Role = CarGroupRole.CommercialOutbound };
            outGroup.Cars.Add(new SpottedCarInfo { ReportingMark = "OUT-1" });
            snapshot.CarGroups.Add(outGroup);

            Assert.AreEqual(1, snapshot.InboundCars.Count());
            Assert.AreEqual(1, snapshot.OutboundCars.Count());
            Assert.AreEqual(2, snapshot.TotalCarCount);

            snapshot.Clear();

            Assert.AreEqual(string.Empty, snapshot.IndustryId);
            Assert.AreEqual(string.Empty, snapshot.DisplayName);
            Assert.AreEqual(FacilityType.CommercialFreight, snapshot.FacilityType);
            Assert.AreEqual(0, snapshot.CurrentTier);
            Assert.AreEqual(string.Empty, snapshot.TierDescription);
            Assert.AreEqual(ProductionState.Idle, snapshot.State);
            Assert.AreEqual(string.Empty, snapshot.StatusMessage);
            Assert.AreEqual(string.Empty, snapshot.DiagnosticAlert);

            Assert.IsEmpty(snapshot.Buffers);
            Assert.IsEmpty(snapshot.InboundCars);
            Assert.IsEmpty(snapshot.OutboundCars);
            Assert.IsEmpty(snapshot.CarGroups);
            Assert.AreEqual(0, snapshot.TotalCarCount);
        }

        [Test]
        public void FacilitySnapshot_GetOrAddCarGroup_WhenDoesNotExist_CreatesNewAndAddsToList()
        {
            FacilitySnapshot snapshot = new FacilitySnapshot();

            FacilityCarGroup group = snapshot.GetOrAddCarGroup("Spur 1", CarGroupRole.CommercialInbound);

            Assert.IsNotNull(group);
            Assert.AreEqual(1, snapshot.CarGroups.Count);
            Assert.AreEqual("Spur 1", group.TrackName);
            Assert.IsTrue(group.IsInbound);
            Assert.IsFalse(group.IsEnRoute);
            Assert.AreEqual(CarGroupRole.CommercialInbound, group.Role);
        }

        [Test]
        public void FacilitySnapshot_GetOrAddCarGroup_WhenMatchingExists_ReturnsExistingWithoutAddingDuplicate()
        {
            FacilitySnapshot snapshot = new FacilitySnapshot();
            FacilityCarGroup initial = snapshot.GetOrAddCarGroup("Spur 1", CarGroupRole.CommercialInbound);
            initial.Cars.Add(new SpottedCarInfo { ReportingMark = "PRR 123" });

            FacilityCarGroup retrieved = snapshot.GetOrAddCarGroup("Spur 1", CarGroupRole.CommercialInbound);

            Assert.AreSame(initial, retrieved);
            Assert.AreEqual(1, snapshot.CarGroups.Count);
            Assert.AreEqual(1, retrieved.Cars.Count);
            Assert.AreEqual("PRR 123", retrieved.Cars[0].ReportingMark);
        }

        [Test]
        public void FacilitySnapshot_GetOrAddCarGroup_WhenSameTrackButDifferentRole_CreatesSeparateGroups()
        {
            FacilitySnapshot snapshot = new FacilitySnapshot();

            FacilityCarGroup inbound = snapshot.GetOrAddCarGroup("Track A", CarGroupRole.CommercialInbound);
            FacilityCarGroup outbound = snapshot.GetOrAddCarGroup("Track A", CarGroupRole.CommercialOutbound);

            Assert.AreNotSame(inbound, outbound);
            Assert.AreEqual(2, snapshot.CarGroups.Count);
            Assert.IsTrue(inbound.IsInbound);
            Assert.IsFalse(outbound.IsInbound);
        }

        [Test]
        public void FacilityCarGroup_Clear_ResetsFieldsAndCars()
        {
            FacilityCarGroup group = new FacilityCarGroup
            {
                TrackName = "Spur 1",
                Role = CarGroupRole.EnRouteWaybill
            };
            group.Cars.Add(new SpottedCarInfo());

            Assert.IsTrue(group.IsEnRoute);

            group.Clear();

            Assert.AreEqual(string.Empty, group.TrackName);
            Assert.AreEqual(CarGroupRole.CommercialInbound, group.Role);
            Assert.IsTrue(group.IsInbound);
            Assert.IsFalse(group.IsEnRoute);
            Assert.IsEmpty(group.Cars);
        }
    }
}
