using Model.Ops;
using Model.Ops.Definition;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Helpers;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class CarTrackingHelperTests
    {
        [Test]
        public void GetCarCargo_WhenCarIsNull_ReturnsEmptyTuple()
        {
            (Load? load, float qty, float cap) = CarTrackingHelper.GetCarCargo(null!, null);
            Assert.IsNull(load);
            Assert.AreEqual(0f, qty);
            Assert.AreEqual(0f, cap);
        }

        [Test]
        public void IsWaybilledHere_WhenWaybillOrIndustryNull_ReturnsFalse()
        {
            Assert.IsFalse(CarTrackingHelper.IsWaybilledHere(null, null!));
            Assert.IsFalse(CarTrackingHelper.IsWaybilledHere(null, new Industry()));
        }
    }
}
