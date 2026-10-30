using System.Collections.Generic;
using Model.Definition.Data;
using Model.Ops;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class BufferHelperTests
    {
        [Test]
        public void GetOrAddCommercialBuffer_WhenDoesNotExist_CreatesNewAndAddsToList()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();

            CommercialBuffer created = BufferHelper.GetOrAddCommercialBuffer(buffers, "raw_logs", "Raw Logs", LoadUnits.Pounds, isInput: true);

            Assert.IsNotNull(created);
            Assert.AreEqual(1, buffers.Count);
            Assert.AreEqual("raw_logs", created.CargoType);
            Assert.AreEqual("Raw Logs", created.CargoDisplayName);
            Assert.AreEqual(LoadUnits.Pounds, created.Units);
            Assert.AreEqual(BufferRole.CommercialInput, created.Role);
            Assert.IsTrue(created.IsInput);
        }

        [Test]
        public void GetOrAddCommercialBuffer_WhenMatchingExists_ReturnsExistingWithoutAddingDuplicate()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();
            CommercialBuffer initial = BufferHelper.GetOrAddCommercialBuffer(buffers, "raw_logs", "Raw Logs", LoadUnits.Pounds, isInput: true);
            initial.CurrentAmount = 500f;

            CommercialBuffer retrieved = BufferHelper.GetOrAddCommercialBuffer(buffers, "raw_logs", "Raw Logs", LoadUnits.Pounds, isInput: true);

            Assert.AreSame(initial, retrieved);
            Assert.AreEqual(1, buffers.Count);
            Assert.AreEqual(500f, retrieved.CurrentAmount);
        }

        [Test]
        public void GetOrAddCommercialBuffer_WhenSameCargoDifferentRole_CreatesSeparateBuffers()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();

            CommercialBuffer input = BufferHelper.GetOrAddCommercialBuffer(buffers, "wood_chips", "Wood Chips", LoadUnits.Pounds, isInput: true);
            CommercialBuffer output = BufferHelper.GetOrAddCommercialBuffer(buffers, "wood_chips", "Wood Chips", LoadUnits.Pounds, isInput: false);

            Assert.AreNotSame(input, output);
            Assert.AreEqual(2, buffers.Count);
            Assert.IsTrue(input.IsInput);
            Assert.IsFalse(output.IsInput);
        }

        [Test]
        public void GetOrAddReserveBuffer_WhenDoesNotExist_CreatesNewAndAddsToList()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();

            ReserveBuffer created = BufferHelper.GetOrAddReserveBuffer(buffers, "coal", "Coal", LoadUnits.Pounds, BufferRole.LocomotiveSupply);

            Assert.IsNotNull(created);
            Assert.AreEqual(1, buffers.Count);
            Assert.AreEqual("coal", created.CargoType);
            Assert.AreEqual("Coal", created.CargoDisplayName);
            Assert.AreEqual(BufferRole.LocomotiveSupply, created.Role);
            Assert.IsFalse(created.IsRepairParts);
        }

        [Test]
        public void GetOrAddReserveBuffer_WhenMatchingExists_ReturnsExistingWithoutAddingDuplicate()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();
            ReserveBuffer initial = BufferHelper.GetOrAddReserveBuffer(buffers, "diesel", "Diesel", LoadUnits.Gallons, BufferRole.LocomotiveSupply);
            initial.CurrentAmount = 1200f;

            ReserveBuffer retrieved = BufferHelper.GetOrAddReserveBuffer(buffers, "diesel", "Diesel", LoadUnits.Gallons, BufferRole.LocomotiveSupply);

            Assert.AreSame(initial, retrieved);
            Assert.AreEqual(1, buffers.Count);
            Assert.AreEqual(1200f, retrieved.CurrentAmount);
        }

        [Test]
        public void GetOrAddSpurGroup_WhenDoesNotExist_CreatesNewAndAddsToList()
        {
            List<SpurCarGroup> groups = new List<SpurCarGroup>();

            SpurCarGroup group = BufferHelper.GetOrAddSpurGroup(groups, "Spur 1", SpurRole.CommercialInbound);

            Assert.IsNotNull(group);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual("Spur 1", group.TrackName);
            Assert.IsTrue(group.IsInbound);
            Assert.IsFalse(group.IsEnRoute);
            Assert.AreEqual(SpurRole.CommercialInbound, group.Role);
        }

        [Test]
        public void GetOrAddSpurGroup_WhenMatchingExists_ReturnsExistingWithoutAddingDuplicate()
        {
            List<SpurCarGroup> groups = new List<SpurCarGroup>();
            SpurCarGroup initial = BufferHelper.GetOrAddSpurGroup(groups, "Spur 1", SpurRole.CommercialInbound);
            initial.Cars.Add(new SpottedCarInfo { ReportingMark = "PRR 123" });

            SpurCarGroup retrieved = BufferHelper.GetOrAddSpurGroup(groups, "Spur 1", SpurRole.CommercialInbound);

            Assert.AreSame(initial, retrieved);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(1, retrieved.Cars.Count);
            Assert.AreEqual("PRR 123", retrieved.Cars[0].ReportingMark);
        }

        [Test]
        public void GetOrAddSpurGroup_WhenSameTrackButDifferentRole_CreatesSeparateGroups()
        {
            List<SpurCarGroup> groups = new List<SpurCarGroup>();

            SpurCarGroup inbound = BufferHelper.GetOrAddSpurGroup(groups, "Track A", SpurRole.CommercialInbound);
            SpurCarGroup outbound = BufferHelper.GetOrAddSpurGroup(groups, "Track A", SpurRole.CommercialOutbound);

            Assert.AreNotSame(inbound, outbound);
            Assert.AreEqual(2, groups.Count);
            Assert.IsTrue(inbound.IsInbound);
            Assert.IsFalse(outbound.IsInbound);
        }

        [Test]
        public void FindMaxStorageForLoad_WhenComponentsNullOrEmpty_ReturnsDefault100()
        {
            float resultNull = BufferHelper.FindMaxStorageForLoad(null!, null!);
            Assert.AreEqual(100f, resultNull);

            List<IndustryComponent> empty = new List<IndustryComponent>();
            float resultEmpty = BufferHelper.FindMaxStorageForLoad(empty, null!);
            Assert.AreEqual(100f, resultEmpty);
        }
    }
}
