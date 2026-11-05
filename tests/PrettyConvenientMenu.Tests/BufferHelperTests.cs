using System.Collections.Generic;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class BufferCollectionTests
    {
        private static Load CreateTestLoad(string id, string desc)
        {
            var def = new LoadDefinition
            {
                Description = desc,
                Units = LoadUnits.Pounds,
                Density = 50f,
                CarTypeFilter = "*"
            };
            return Load.FromDefinition(id, def);
        }

        [Test]
        public void Add_WhenDistinctBuffers_AddsToList()
        {
            BufferCollection collection = new BufferCollection();
            CommercialBuffer buf1 = new CommercialBuffer { CargoType = "raw_logs", Role = BufferRole.CommercialOutput, MaxCapacity = 100f };
            CommercialBuffer buf2 = new CommercialBuffer { CargoType = "wood_chips", Role = BufferRole.CommercialOutput, MaxCapacity = 50f };

            collection.Add(buf1);
            collection.Add(buf2);

            Assert.AreEqual(2, collection.Count);
        }

        [Test]
        public void Add_WhenSameCargoDifferentPrefix_PromotesToComposite()
        {
            BufferCollection collection = new BufferCollection();
            CommercialBuffer landing1 = new CommercialBuffer
            {
                CargoType = "raw_logs",
                Role = BufferRole.CommercialOutput,
                StoragePrefix = "l1",
                CurrentAmount = 100f,
                MaxCapacity = 500f,
                DailyRate = 200f
            };
            CommercialBuffer landing2 = new CommercialBuffer
            {
                CargoType = "raw_logs",
                Role = BufferRole.CommercialOutput,
                StoragePrefix = "l2",
                CurrentAmount = 150f,
                MaxCapacity = 500f,
                DailyRate = 300f
            };

            collection.Add(landing1);
            collection.Add(landing2);

            Assert.AreEqual(1, collection.Count);
            Assert.IsInstanceOf<CompositeCommercialBuffer>(collection[0]);

            CompositeCommercialBuffer composite = (CompositeCommercialBuffer)collection[0];
            Assert.AreEqual(2, composite.SubBuffers.Count);
            Assert.AreEqual(250f, composite.CurrentAmount);
            Assert.AreEqual(1000f, composite.MaxCapacity);
            Assert.AreEqual(500f, composite.DailyRate);
        }

        [Test]
        public void FindMatching_ByComponent_RoutesToCorrectLandingOrComposite()
        {
            BufferCollection collection = new BufferCollection();
            CommercialBuffer landing1 = new CommercialBuffer
            {
                CargoType = "raw_logs",
                Role = BufferRole.CommercialOutput,
                StoragePrefix = "l1",
                CurrentAmount = 100f,
                MaxCapacity = 500f
            };
            CommercialBuffer landing2 = new CommercialBuffer
            {
                CargoType = "raw_logs",
                Role = BufferRole.CommercialOutput,
                StoragePrefix = "l2",
                CurrentAmount = 150f,
                MaxCapacity = 500f
            };

            collection.Add(landing1);
            collection.Add(landing2);

            Load load = CreateTestLoad("raw_logs", "Raw Logs");

            StorageLoader loaderL1 = new StorageLoader
            {
                subIdentifier = "l1",
                sharedStorage = false
            };

            StorageLoader sharedLoader = new StorageLoader
            {
                subIdentifier = null,
                sharedStorage = true
            };

            BufferInfo? matchL1 = collection.FindMatching(loaderL1, load);
            BufferInfo? matchShared = collection.FindMatching(sharedLoader, load);

            Assert.AreSame(landing1, matchL1);
            Assert.IsInstanceOf<CompositeCommercialBuffer>(matchShared);
        }
    }
}
