using System;
using Model.Definition.Data;
using NUnit.Framework;
using PrettyConvenientMenu.UI.Common;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class FormatHelperTests
    {
        [TestCase(-10f, "0.0T")]
        [TestCase(0f, "0.0T")]
        [TestCase(0.0005f, "0.0T")]
        [TestCase(150f, "150 lbs")]
        [TestCase(199.9f, "200 lbs")]
        [TestCase(200f, "0.1T")]
        [TestCase(2000f, "1.0T")]
        [TestCase(19400f, "9.7T")]
        [TestCase(100000f, "50.0T")]
        public void FormatWeight_VariousValues_FormatsCorrectly(float pounds, string expected)
        {
            string result = FormatHelper.FormatWeight(pounds);
            Assert.AreEqual(expected, result);
        }

        [TestCase(0f, LoadUnits.Gallons, "0 gal")]
        [TestCase(450f, LoadUnits.Gallons, "450 gal")]
        [TestCase(1000f, LoadUnits.Gallons, "1.0 kGal")]
        [TestCase(12500f, LoadUnits.Gallons, "12.5 kGal")]
        [TestCase(0f, LoadUnits.Quantity, "0 pcs")]
        [TestCase(240f, LoadUnits.Quantity, "240 pcs")]
        [TestCase(1000f, LoadUnits.Quantity, "1.0k pcs")]
        [TestCase(2500f, LoadUnits.Quantity, "2.5k pcs")]
        public void FormatQuantity_MultiUnit_FormatsCorrectly(float amount, LoadUnits units, string expected)
        {
            string result = FormatHelper.FormatQuantity(amount, units);
            Assert.AreEqual(expected, result);
        }

        [TestCase(0f, 0f, LoadUnits.Pounds, "0.0T (0%)")]
        [TestCase(20000f, 50f, LoadUnits.Pounds, "10.0T (50%)")]
        [TestCase(8000f, 100f, LoadUnits.Gallons, "8.0 kGal (100%)")]
        [TestCase(500f, 50f, LoadUnits.Quantity, "500 pcs (50%)")]
        public void FormatCarLoad_VariousValues_FormatsCorrectly(float amount, float percent, LoadUnits units, string expected)
        {
            string result = FormatHelper.FormatCarLoad(amount, percent, units);
            Assert.AreEqual(expected, result);
        }

        [TestCase(0f, LoadUnits.Pounds, "0.0T/day")]
        [TestCase(150f, LoadUnits.Pounds, "150 lbs/day")]
        [TestCase(20000f, LoadUnits.Pounds, "10.0T/day")]
        [TestCase(5000f, LoadUnits.Gallons, "5.0 kGal/day")]
        [TestCase(250f, LoadUnits.Quantity, "250 pcs/day")]
        public void FormatDailyRate_VariousValues_FormatsCorrectly(float amountPerDay, LoadUnits units, string expected)
        {
            string result = FormatHelper.FormatDailyRate(amountPerDay, units);
            Assert.AreEqual(expected, result);
        }

        [TestCase(0f, LoadUnits.Pounds, "--")]
        [TestCase(0.00005f, LoadUnits.Pounds, "--")]
        [TestCase(200f, LoadUnits.Pounds, "0.1T/min")]
        [TestCase(4000f, LoadUnits.Pounds, "2.0T/min")]
        [TestCase(10f, LoadUnits.Pounds, "0.3T/hr")] // 10 lbs/min * 60 = 600 lbs/hr = 0.3 T/hr
        [TestCase(3.5f, LoadUnits.Pounds, "0.1T/hr")] // 3.5 * 60 = 210 lbs/hr = 0.1 T/hr
        [TestCase(1f, LoadUnits.Pounds, "1 lbs/min")] // 1 lb/min * 60 = 60 lbs/hr (< 200 lbs/hr threshold -> lbs/min)
        [TestCase(50f, LoadUnits.Gallons, "50 gpm")]
        [TestCase(1500f, LoadUnits.Gallons, "1.5 kGal/min")]
        [TestCase(20f, LoadUnits.Quantity, "20 pcs/min")]
        [TestCase(2500f, LoadUnits.Quantity, "2.5k pcs/min")]
        public void FormatTransferRate_VariousRates_FormatsCorrectly(float amountPerMin, LoadUnits units, string expected)
        {
            string result = FormatHelper.FormatTransferRate(amountPerMin, units);
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void FormatDuration_VariousTimes_FormatsCorrectly()
        {
            Assert.AreEqual("--", FormatHelper.FormatDuration(null));
            Assert.AreEqual("< 1 min", FormatHelper.FormatDuration(TimeSpan.FromSeconds(30)));
            Assert.AreEqual("~23 min", FormatHelper.FormatDuration(TimeSpan.FromMinutes(23.4)));
            Assert.AreEqual("~2h 05m", FormatHelper.FormatDuration(TimeSpan.FromMinutes(125)));
            Assert.AreEqual("~8d 0h", FormatHelper.FormatDuration(TimeSpan.FromHours(192)));
        }
    }
}
