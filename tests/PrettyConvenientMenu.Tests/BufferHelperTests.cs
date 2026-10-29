using System.Collections.Generic;
using Model.Definition.Data;
using Model.Ops;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;

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
            Assert.IsTrue(created.IsInput);
            Assert.AreEqual(LoadUnits.Pounds, created.Units);
            Assert.AreEqual(BufferRole.CommercialInput, created.Role);
        }

        [Test]
        public void GetOrAddCommercialBuffer_WhenMatchingExists_ReturnsExistingWithoutAddingDuplicate()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();
            CommercialBuffer initial = BufferHelper.GetOrAddCommercialBuffer(buffers, "raw_logs", "Raw Logs", LoadUnits.Pounds, isInput: true);
            initial.CurrentAmount = 500f;

            CommercialBuffer retrieved = BufferHelper.GetOrAddCommercialBuffer(buffers, "raw_logs", "Raw Logs Different Name", LoadUnits.Pounds, isInput: true);

            Assert.AreSame(initial, retrieved);
            Assert.AreEqual(1, buffers.Count);
            Assert.AreEqual(500f, retrieved.CurrentAmount);
        }

        [Test]
        public void GetOrAddCommercialBuffer_WhenSameCargoTypeButDifferentIsInput_CreatesSeparateBuffers()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();

            CommercialBuffer inputBuffer = BufferHelper.GetOrAddCommercialBuffer(buffers, "lumber", "Lumber Input", LoadUnits.Pounds, isInput: true);
            CommercialBuffer outputBuffer = BufferHelper.GetOrAddCommercialBuffer(buffers, "lumber", "Lumber Output", LoadUnits.Pounds, isInput: false);

            Assert.AreNotSame(inputBuffer, outputBuffer);
            Assert.AreEqual(2, buffers.Count);
            Assert.IsTrue(inputBuffer.IsInput);
            Assert.IsFalse(outputBuffer.IsInput);
        }

        [Test]
        public void GetOrAddReserveBuffer_WhenDoesNotExist_CreatesReserveBuffer()
        {
            List<BufferInfo> buffers = new List<BufferInfo>();

            ReserveBuffer created = BufferHelper.GetOrAddReserveBuffer(buffers, "diesel", "Diesel Fuel", LoadUnits.Gallons, BufferRole.LocomotiveSupply);

            Assert.IsNotNull(created);
            Assert.AreEqual(1, buffers.Count);
            Assert.AreEqual("diesel", created.CargoType);
            Assert.AreEqual(LoadUnits.Gallons, created.Units);
            Assert.AreEqual(BufferRole.LocomotiveSupply, created.Role);
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
