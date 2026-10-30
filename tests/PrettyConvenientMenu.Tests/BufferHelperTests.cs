using System.Collections.Generic;
using Model.Definition.Data;
using Model.Ops;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Helpers;

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
