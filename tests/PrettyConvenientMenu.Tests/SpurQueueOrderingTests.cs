using System.Collections.Generic;
using NUnit.Framework;
using PrettyConvenientMenu.Data;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class SpurQueueOrderingTests
    {
        [Test]
        public void UnloadOrderAscending_SortsLowestQuantityFirst_AndTieBreaksByTrackOrder()
        {
            SpurTrackCar car1 = new SpurTrackCar(null!, quantity: 500f, capacity: 1000f, trackOrder: 3);
            SpurTrackCar car2 = new SpurTrackCar(null!, quantity: 100f, capacity: 1000f, trackOrder: 2);
            SpurTrackCar car3 = new SpurTrackCar(null!, quantity: 100f, capacity: 1000f, trackOrder: 1);
            SpurTrackCar car4 = new SpurTrackCar(null!, quantity: 800f, capacity: 1000f, trackOrder: 0);

            List<SpurTrackCar> list = new List<SpurTrackCar> { car1, car2, car3, car4 };
            list.Sort(UnloadOrderAscending.Instance);

            // car3 (100 qty, order 1) comes before car2 (100 qty, order 2)
            Assert.AreEqual(1, list[0].TrackOrder);
            Assert.AreEqual(100f, list[0].Quantity);

            Assert.AreEqual(2, list[1].TrackOrder);
            Assert.AreEqual(100f, list[1].Quantity);

            Assert.AreEqual(3, list[2].TrackOrder);
            Assert.AreEqual(500f, list[2].Quantity);

            Assert.AreEqual(0, list[3].TrackOrder);
            Assert.AreEqual(800f, list[3].Quantity);
        }

        [Test]
        public void LoadOrderDescending_SortsHighestQuantityFirst_AndTieBreaksByTrackOrder()
        {
            SpurTrackCar car1 = new SpurTrackCar(null!, quantity: 500f, capacity: 1000f, trackOrder: 3);
            SpurTrackCar car2 = new SpurTrackCar(null!, quantity: 800f, capacity: 1000f, trackOrder: 2);
            SpurTrackCar car3 = new SpurTrackCar(null!, quantity: 800f, capacity: 1000f, trackOrder: 1);
            SpurTrackCar car4 = new SpurTrackCar(null!, quantity: 100f, capacity: 1000f, trackOrder: 0);

            List<SpurTrackCar> list = new List<SpurTrackCar> { car1, car2, car3, car4 };
            list.Sort(LoadOrderDescending.Instance);

            // car3 (800 qty, order 1) comes before car2 (800 qty, order 2)
            Assert.AreEqual(1, list[0].TrackOrder);
            Assert.AreEqual(800f, list[0].Quantity);

            Assert.AreEqual(2, list[1].TrackOrder);
            Assert.AreEqual(800f, list[1].Quantity);

            Assert.AreEqual(3, list[2].TrackOrder);
            Assert.AreEqual(500f, list[2].Quantity);

            Assert.AreEqual(0, list[3].TrackOrder);
            Assert.AreEqual(100f, list[3].Quantity);
        }
    }
}
