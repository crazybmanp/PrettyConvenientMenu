using System;
using System.Collections.Generic;
using HarmonyLib;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Helpers;
using PrettyConvenientMenu.Data.RollingStock;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class MultiLoadQueueTests
    {
        private Harmony? _harmony;

        [OneTimeSetUp]
        public void SetUp()
        {
            _harmony = new Harmony("test.cartypefilter.patch");
            var original = typeof(CarTypeFilter).GetMethod(nameof(CarTypeFilter.Matches), new[] { typeof(string) });
            var prefix = typeof(MultiLoadQueueTests).GetMethod(nameof(MatchesPrefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (original != null && prefix != null)
            {
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _harmony?.UnpatchAll("test.cartypefilter.patch");
        }

        private static bool MatchesPrefix(ref bool __result)
        {
            __result = true;
            return false;
        }

        private Load CreateTestLoad(string id, string desc)
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

        private class FakeOpsCar : IOpsCar
        {
            public string Id { get; set; } = "CAR1";
            public string CarType { get; set; } = "XM";
            public string DisplayName { get; set; } = "CAR 1";
            public bool IsOwnedByPlayer { get; set; } = true;
            public int WeightInTons { get; set; } = 20;
            public float TareTons { get; set; } = 15f;
            public Waybill? Waybill { get; set; }
            public PassengerMarker? PassengerMarker { get; set; }
            public float Condition { get; set; } = 1f;

            public Load? CurrentLoad { get; set; }
            public float CurrentQuantity { get; set; }
            public float MaxCapacity { get; set; } = 1000f;

            public bool IsEmptyOrContains(Load load) => CurrentQuantity <= (load?.ZeroThreshold ?? 0.001f) || CurrentLoad == load;
            public (float quantity, float capacity) QuantityOfLoad(Load load) => CurrentLoad == load ? (CurrentQuantity, MaxCapacity) : (0f, MaxCapacity);
            public string OriginOfLoad(Load load) => "origin";
            public float Unload(Load load, float quantityToConsume) => 0f;
            public float Load(Load load, float quantityToLoad, string originId) => 0f;
            public bool IsFull(Load load) => CurrentQuantity >= MaxCapacity;
            public void SetWaybill(Waybill? waybill, IndustryComponent setter, string reason) => Waybill = waybill;
            public bool GetOverrideDestination(OverrideDestination overrideDestination, out OpsCarPosition destination, out string tag)
            {
                destination = default;
                tag = string.Empty;
                return false;
            }
        }

        [Test]
        public void MultiLoadQueue_WhenMultipleLoadsOnSpur_OnlyFirstIsActive_SubsequentAreQueued()
        {
            Industry industry = new Industry { identifier = "mow_depot" };
            StorageUnloader unloader = new StorageUnloader { Industry = industry, subIdentifier = "unloader" };
            industry.Components.Add(unloader);

            Load ballast = CreateTestLoad("rr/ballast", "Ballast");
            Load ties = CreateTestLoad("rr/ties", "Ties");

            OpsCarPosition pos = new OpsCarPosition("MOW Unloader", unloader.Identifier, null!);
            Waybill waybill1 = new Waybill(default, null, pos, 100, false, "rr/ballast", 3);
            Waybill waybill2 = new Waybill(default, null, pos, 100, false, "rr/ties", 3);

            FakeOpsCar opsCar1 = new FakeOpsCar
            {
                Id = "CAR1",
                DisplayName = "BALLAST 1",
                Waybill = waybill1,
                CurrentLoad = ballast,
                CurrentQuantity = 600f,
                MaxCapacity = 1000f
            };

            FakeOpsCar opsCar2 = new FakeOpsCar
            {
                Id = "CAR2",
                DisplayName = "TIES 1",
                Waybill = waybill2,
                CurrentLoad = ties,
                CurrentQuantity = 300f,
                MaxCapacity = 1000f
            };

            SpottedCarInfo carInfo1 = new SpottedCarInfo(null, opsCar1, 0);
            SpottedCarInfo carInfo2 = new SpottedCarInfo(null, opsCar2, 1);
            List<SpottedCarInfo> trackCars = new List<SpottedCarInfo> { carInfo1, carInfo2 };

            bool activeCarAssigned = false;
            float accumulatedMinutes = 0f;

            // Rate: 10 units/min. Buffer available: 2000.
            // Car 1 (Ballast): 600 units / 10 = 60 mins.
            TransferQueueHelper.SequenceQueue(
                trackCars, unloader, ballast, industry,
                bufferAvailable: 2000f, nominalRatePerMin: 10f, isUnload: true,
                matchingBuffer: null, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true);

            Assert.IsTrue(activeCarAssigned);
            Assert.AreEqual(CarStatus.ActiveTransfer, carInfo1.Status);
            Assert.AreEqual("Unloading", carInfo1.StatusDetail);
            Assert.AreEqual(TimeSpan.FromMinutes(60), carInfo1.EstimatedTimeToCompletion);

            // Car 2 (Ties): 300 units / 10 = 30 mins.
            // Should be Queued with ETA = 60 + 30 = 90 mins.
            TransferQueueHelper.SequenceQueue(
                trackCars, unloader, ties, industry,
                bufferAvailable: 2000f, nominalRatePerMin: 10f, isUnload: true,
                matchingBuffer: null, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true);

            Assert.AreEqual(CarStatus.Queued, carInfo2.Status);
            Assert.AreEqual("Queued", carInfo2.StatusDetail);
            Assert.AreEqual(TimeSpan.FromMinutes(90), carInfo2.EstimatedTimeToCompletion);
        }

        [Test]
        public void MultiLoadQueue_WhenFirstLoadBufferFull_FirstIsBlocked_SecondBecomesActive()
        {
            Industry industry = new Industry { identifier = "mow_depot" };
            StorageUnloader unloader = new StorageUnloader { Industry = industry, subIdentifier = "unloader" };
            industry.Components.Add(unloader);

            Load ballast = CreateTestLoad("rr/ballast", "Ballast");
            Load ties = CreateTestLoad("rr/ties", "Ties");

            OpsCarPosition pos = new OpsCarPosition("MOW Unloader", unloader.Identifier, null!);
            Waybill waybill1 = new Waybill(default, null, pos, 100, false, "rr/ballast", 3);
            Waybill waybill2 = new Waybill(default, null, pos, 100, false, "rr/ties", 3);

            FakeOpsCar opsCar1 = new FakeOpsCar
            {
                Id = "CAR1",
                DisplayName = "BALLAST 1",
                Waybill = waybill1,
                CurrentLoad = ballast,
                CurrentQuantity = 600f,
                MaxCapacity = 1000f
            };

            FakeOpsCar opsCar2 = new FakeOpsCar
            {
                Id = "CAR2",
                DisplayName = "TIES 1",
                Waybill = waybill2,
                CurrentLoad = ties,
                CurrentQuantity = 300f,
                MaxCapacity = 1000f
            };

            SpottedCarInfo carInfo1 = new SpottedCarInfo(null, opsCar1, 0);
            SpottedCarInfo carInfo2 = new SpottedCarInfo(null, opsCar2, 1);
            List<SpottedCarInfo> trackCars = new List<SpottedCarInfo> { carInfo1, carInfo2 };

            bool activeCarAssigned = false;
            float accumulatedMinutes = 0f;

            // Ballast buffer is FULL (bufferAvailable = 0)
            TransferQueueHelper.SequenceQueue(
                trackCars, unloader, ballast, industry,
                bufferAvailable: 0f, nominalRatePerMin: 10f, isUnload: true,
                matchingBuffer: null, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true);

            // Car 1 must be blocked, activeCarAssigned must remain false
            Assert.IsFalse(activeCarAssigned);
            Assert.AreEqual(CarStatus.Blocked, carInfo1.Status);
            Assert.AreEqual("Buffer Full", carInfo1.StatusDetail);

            // Ties buffer has space (bufferAvailable = 2000)
            TransferQueueHelper.SequenceQueue(
                trackCars, unloader, ties, industry,
                bufferAvailable: 2000f, nominalRatePerMin: 10f, isUnload: true,
                matchingBuffer: null, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true);

            // Car 2 must now be the active transfer car!
            Assert.IsTrue(activeCarAssigned);
            Assert.AreEqual(CarStatus.ActiveTransfer, carInfo2.Status);
            Assert.AreEqual("Unloading", carInfo2.StatusDetail);
            Assert.AreEqual(TimeSpan.FromMinutes(30), carInfo2.EstimatedTimeToCompletion);
        }

        [Test]
        public void MultiLoadQueue_StorageLoader_SequencesLoadingCarsAcrossLoads()
        {
            Industry industry = new Industry { identifier = "mow_depot" };
            StorageLoader loader = new StorageLoader { Industry = industry, subIdentifier = "loader" };
            industry.Components.Add(loader);

            Load ballast = CreateTestLoad("rr/ballast", "Ballast");
            Load ties = CreateTestLoad("rr/ties", "Ties");

            OpsCarPosition pos = new OpsCarPosition("MOW Loader", loader.Identifier, null!);
            Waybill waybill1 = new Waybill(default, null, pos, 100, false, "rr/ballast", 3);
            Waybill waybill2 = new Waybill(default, null, pos, 100, false, "rr/ties", 3);

            // Empty hopper waiting to load ballast
            FakeOpsCar opsCar1 = new FakeOpsCar
            {
                Id = "CAR1",
                DisplayName = "HOPPER 1",
                Waybill = waybill1,
                CurrentLoad = null,
                CurrentQuantity = 0f,
                MaxCapacity = 1000f
            };

            // Empty flat waiting to load ties
            FakeOpsCar opsCar2 = new FakeOpsCar
            {
                Id = "CAR2",
                DisplayName = "FLAT 1",
                Waybill = waybill2,
                CurrentLoad = null,
                CurrentQuantity = 0f,
                MaxCapacity = 500f
            };

            SpottedCarInfo carInfo1 = new SpottedCarInfo(null, opsCar1, 0);
            SpottedCarInfo carInfo2 = new SpottedCarInfo(null, opsCar2, 1);
            List<SpottedCarInfo> trackCars = new List<SpottedCarInfo> { carInfo1, carInfo2 };

            bool activeCarAssigned = false;
            float accumulatedMinutes = 0f;

            // Car 1 (Ballast): 1000 to load / 10 = 100 mins.
            TransferQueueHelper.SequenceQueue(
                trackCars, loader, ballast, industry,
                bufferAvailable: 2000f, nominalRatePerMin: 10f, isUnload: false,
                matchingBuffer: null, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true);

            Assert.IsTrue(activeCarAssigned);
            Assert.AreEqual(CarStatus.ActiveTransfer, carInfo1.Status);
            Assert.AreEqual("Loading", carInfo1.StatusDetail);
            Assert.AreEqual(TimeSpan.FromMinutes(100), carInfo1.EstimatedTimeToCompletion);

            // Car 2 (Ties): 500 to load / 10 = 50 mins.
            // Should be Queued with ETA = 100 + 50 = 150 mins.
            TransferQueueHelper.SequenceQueue(
                trackCars, loader, ties, industry,
                bufferAvailable: 2000f, nominalRatePerMin: 10f, isUnload: false,
                matchingBuffer: null, ref activeCarAssigned, ref accumulatedMinutes, multiLoadComponent: true);

            Assert.AreEqual(CarStatus.Queued, carInfo2.Status);
            Assert.AreEqual("Queued", carInfo2.StatusDetail);
            Assert.AreEqual(TimeSpan.FromMinutes(150), carInfo2.EstimatedTimeToCompletion);
        }

        [Test]
        public void SingleLoadOverload_ResetsFreshQueueState_IndependentOfPreviousComponent()
        {
            Industry industry = new Industry { identifier = "mill" };
            IndustryUnloader unloader = new IndustryUnloader { Industry = industry, subIdentifier = "unloader" };
            industry.Components.Add(unloader);

            Load logs = CreateTestLoad("logs", "Logs");

            OpsCarPosition pos = new OpsCarPosition("Mill Unloader", unloader.Identifier, null!);
            Waybill waybill = new Waybill(default, null, pos, 100, false, "logs", 3);

            FakeOpsCar opsCar = new FakeOpsCar
            {
                Id = "CAR1",
                DisplayName = "LOG CAR 1",
                Waybill = waybill,
                CurrentLoad = logs,
                CurrentQuantity = 500f,
                MaxCapacity = 1000f
            };

            SpottedCarInfo carInfo = new SpottedCarInfo(null, opsCar, 0);
            List<SpottedCarInfo> trackCars = new List<SpottedCarInfo> { carInfo };

            // Call the overload without ref params
            TransferQueueHelper.SequenceQueue(
                trackCars, unloader, logs, industry,
                bufferAvailable: 1000f, nominalRatePerMin: 10f, isUnload: true, matchingBuffer: null);

            Assert.AreEqual(CarStatus.ActiveTransfer, carInfo.Status);
            Assert.AreEqual("Unloading", carInfo.StatusDetail);
            Assert.AreEqual(TimeSpan.FromMinutes(50), carInfo.EstimatedTimeToCompletion);
        }
    }
}
