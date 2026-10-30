using System.Collections.Generic;
using NUnit.Framework;
using PrettyConvenientMenu.Data;
using PrettyConvenientMenu.Data.RollingStock;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class SpurQueueOrderingTests
    {
        [Test]
        public void UnloadOrderAscending_SortsLowestQuantityFirst_AndTieBreaksByTrackOrder()
        {
            SpottedCarInfo car1 = new SpottedCarInfo { CurrentAmount = 500f, CapacityAmount = 1000f, TrackIndex = 3 };
            SpottedCarInfo car2 = new SpottedCarInfo { CurrentAmount = 100f, CapacityAmount = 1000f, TrackIndex = 2 };
            SpottedCarInfo car3 = new SpottedCarInfo { CurrentAmount = 100f, CapacityAmount = 1000f, TrackIndex = 1 };
            SpottedCarInfo car4 = new SpottedCarInfo { CurrentAmount = 800f, CapacityAmount = 1000f, TrackIndex = 0 };

            List<SpottedCarInfo> list = new List<SpottedCarInfo> { car1, car2, car3, car4 };
            list.Sort(UnloadOrderAscending.Instance);

            // car3 (100 qty, order 1) comes before car2 (100 qty, order 2)
            Assert.AreEqual(1, list[0].TrackIndex);
            Assert.AreEqual(100f, list[0].CurrentAmount);

            Assert.AreEqual(2, list[1].TrackIndex);
            Assert.AreEqual(100f, list[1].CurrentAmount);

            Assert.AreEqual(3, list[2].TrackIndex);
            Assert.AreEqual(500f, list[2].CurrentAmount);

            Assert.AreEqual(0, list[3].TrackIndex);
            Assert.AreEqual(800f, list[3].CurrentAmount);
        }

        [Test]
        public void LoadOrderDescending_SortsHighestQuantityFirst_AndTieBreaksByTrackOrder()
        {
            SpottedCarInfo car1 = new SpottedCarInfo { CurrentAmount = 500f, CapacityAmount = 1000f, TrackIndex = 3 };
            SpottedCarInfo car2 = new SpottedCarInfo { CurrentAmount = 800f, CapacityAmount = 1000f, TrackIndex = 2 };
            SpottedCarInfo car3 = new SpottedCarInfo { CurrentAmount = 800f, CapacityAmount = 1000f, TrackIndex = 1 };
            SpottedCarInfo car4 = new SpottedCarInfo { CurrentAmount = 100f, CapacityAmount = 1000f, TrackIndex = 0 };

            List<SpottedCarInfo> list = new List<SpottedCarInfo> { car1, car2, car3, car4 };
            list.Sort(LoadOrderDescending.Instance);

            // car3 (800 qty, order 1) comes before car2 (800 qty, order 2)
            Assert.AreEqual(1, list[0].TrackIndex);
            Assert.AreEqual(800f, list[0].CurrentAmount);

            Assert.AreEqual(2, list[1].TrackIndex);
            Assert.AreEqual(800f, list[1].CurrentAmount);

            Assert.AreEqual(3, list[2].TrackIndex);
            Assert.AreEqual(500f, list[2].CurrentAmount);

            Assert.AreEqual(0, list[3].TrackIndex);
            Assert.AreEqual(100f, list[3].CurrentAmount);
        }
    }
}
