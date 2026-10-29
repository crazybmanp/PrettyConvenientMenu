using System;
using Model.Definition.Data;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Buffers;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class BufferInfoTests
    {
        [Test]
        public void ReserveBuffer_TotalsAndAvailableAmount_CalculatesCorrectly()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                MaxCapacity = 10000f,
                CurrentAmount = 5000f,
                ActiveOutbound = 1000f,
                CommittedOutbound = 500f,
                ActiveInbound = 2000f,
                ScheduledInbound = 1000f,
                Units = LoadUnits.Gallons
            };

            Assert.AreEqual(1500f, buffer.TotalOutbound);
            Assert.AreEqual(3000f, buffer.TotalInbound);
            Assert.AreEqual(3500f, buffer.AvailableAmount);
        }

        [Test]
        public void ReserveBuffer_WhenOutboundExceedsCurrent_NeverReturnsNegative()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                MaxCapacity = 10000f,
                CurrentAmount = 2000f,
                ActiveOutbound = 3000f,
                CommittedOutbound = 500f
            };

            Assert.AreEqual(3500f, buffer.TotalOutbound);
            Assert.AreEqual(0f, buffer.AvailableAmount);
        }

        [Test]
        public void CommercialBuffer_IsInputAndRunway_WorksCorrectly()
        {
            CommercialBuffer inputBuffer = new CommercialBuffer
            {
                Role = BufferRole.CommercialInput,
                DailyRate = 200f,
                RunwayDuration = TimeSpan.FromDays(2.5)
            };

            CommercialBuffer outputBuffer = new CommercialBuffer
            {
                Role = BufferRole.CommercialOutput,
                DailyRate = 100f
            };

            Assert.IsTrue(inputBuffer.IsInput);
            Assert.IsFalse(outputBuffer.IsInput);
            Assert.AreEqual(2.5, inputBuffer.RunwayDuration.Value.TotalDays, 0.001);
            Assert.IsNull(outputBuffer.RunwayDuration);
        }

        [Test]
        public void Ratios_WithinCapacity_CalculatesCorrectly()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                MaxCapacity = 10000f,
                CurrentAmount = 5000f,
                ActiveOutbound = 1000f,
                CommittedOutbound = 500f,
                ActiveInbound = 2000f,
                ScheduledInbound = 1000f
            };

            Assert.AreEqual(0.5f, buffer.FillRatio, 0.0001f);
            Assert.AreEqual(0.1f, buffer.ActiveOutboundRatio, 0.0001f);
            Assert.AreEqual(0.05f, buffer.CommittedOutboundRatio, 0.0001f);
            Assert.AreEqual(0.2f, buffer.ActiveInboundRatio, 0.0001f);
            Assert.AreEqual(0.1f, buffer.ScheduledInboundRatio, 0.0001f);
        }

        [Test]
        public void Ratios_ZeroCapacity_ReturnsZeroWithoutDivisionByZero()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                MaxCapacity = 0f,
                CurrentAmount = 5000f,
                ActiveOutbound = 1000f,
                CommittedOutbound = 500f
            };

            Assert.AreEqual(0f, buffer.FillRatio);
            Assert.AreEqual(0f, buffer.ActiveOutboundRatio);
            Assert.AreEqual(0f, buffer.CommittedOutboundRatio);
        }

        [Test]
        public void Ratios_OverCapacity_ClampedToOne()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                MaxCapacity = 10000f,
                CurrentAmount = 15000f,
                ActiveOutbound = 2000f,
                CommittedOutbound = 1000f
            };

            Assert.AreEqual(1.0f, buffer.FillRatio);
        }

        [Test]
        public void ReserveBuffer_RepairDemandAndDeficit_CalculatesCorrectly()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                IsRepairParts = true,
                CurrentAmount = 10000f,
                ProgramDemand = 6000f,
                SpotDemand = 2000f
            };

            Assert.AreEqual(8000f, buffer.TotalDemand);
            Assert.AreEqual(0f, buffer.Deficit);

            // Increase demand so a deficit occurs
            buffer.SpotDemand = 8000f;
            Assert.AreEqual(14000f, buffer.TotalDemand);
            Assert.AreEqual(4000f, buffer.Deficit);
        }

        [Test]
        public void ReserveBuffer_ProjectedInventoryWaterfall_CalculatesCorrectly()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                IsRepairParts = true,
                CurrentAmount = 10000f,
                ProgramDemand = 4000f,
                ScheduledInbound = 2000f,
                SpotDemand = 3000f
            };

            // Base(10000) - Program(4000) + Arriving(2000) - Spot(3000) = 5000
            Assert.AreEqual(5000f, buffer.ProjectedInventory);

            // Increase spot demand so projected drops below zero
            buffer.SpotDemand = 9000f;
            // Base(10000) - Program(4000) + Arriving(2000) - Spot(9000) = -1000
            Assert.AreEqual(-1000f, buffer.ProjectedInventory);
        }

        [Test]
        public void RepairParts_DemandFormula_CalculatesCorrectPounds()
        {
            // Overhaul: 70% to 100%
            float overhaulCond = 0.70f;
            float overhaulTarget = 1.0f;
            float overhaulDemand = Math.Max(0f, overhaulTarget - overhaulCond) * 12000f;
            Assert.AreEqual(3600f, overhaulDemand, 0.01f);

            // Spot / Standard: 75% to 90% cap
            float spotCond = 0.75f;
            float spotTarget = 0.90f;
            float spotDemand = Math.Max(0f, spotTarget - spotCond) * 12000f;
            Assert.AreEqual(1800f, spotDemand, 0.01f);

            // Already serviced: 95% condition with 90% cap
            float servicedCond = 0.95f;
            float servicedTarget = 0.90f;
            float servicedDemand = Math.Max(0f, servicedTarget - servicedCond) * 12000f;
            Assert.AreEqual(0f, servicedDemand);
        }

        [Test]
        public void ReserveBuffer_WithUnloadingAndEnRoute_CalculatesSeparately()
        {
            ReserveBuffer buffer = new ReserveBuffer
            {
                IsRepairParts = true,
                MaxCapacity = 200000f,
                CurrentAmount = 50000f,      // 25.0T base
                ActiveInbound = 100000f,     // 50.0T unloading (green bar)
                ScheduledInbound = 40000f,   // 20.0T en-route (caliper only)
                ProgramDemand = 58600f,      // 29.3T program
                SpotDemand = 10000f          // 5.0T spot
            };

            float onSiteStock = buffer.CurrentAmount + buffer.ActiveInbound;
            float totalStock = onSiteStock + buffer.ScheduledInbound;

            // On-site stock (75.0T) covers program demand (29.3T)
            Assert.AreEqual(150000f, onSiteStock);
            Assert.AreEqual(190000f, totalStock);
            Assert.IsTrue(totalStock >= buffer.ProgramDemand);
            Assert.IsTrue(totalStock - buffer.ProgramDemand >= buffer.SpotDemand);
            Assert.AreEqual(121400f, buffer.ProjectedInventory, 0.1f);
        }
    }
}
