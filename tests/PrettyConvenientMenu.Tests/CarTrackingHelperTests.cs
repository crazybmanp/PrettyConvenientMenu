using System;
using System.Collections.Generic;
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

        [Test]
        public void IsWaybilledHere_IndustryLoader_WhenCarHasNoWaybill_ReturnsFalse()
        {
            Industry industry = new Industry { identifier = "stenzel" };
            IndustryLoader loader = new IndustryLoader { Industry = industry, subIdentifier = "loader" };
            industry.Components.Add(loader);

            bool result = CarTrackingHelper.IsWaybilledHere(null, industry, loader);

            Assert.IsFalse(result, "A car on an IndustryLoader spur with no waybill must be ignored.");
        }

        [Test]
        public void IsWaybilledHere_IndustryLoader_WhenCarWaybilledToAnotherDestination_ReturnsFalse()
        {
            Industry industry = new Industry { identifier = "stenzel" };
            IndustryLoader loader = new IndustryLoader { Industry = industry, subIdentifier = "loader" };
            IndustryUnloader unloader = new IndustryUnloader { Industry = industry, subIdentifier = "unloader" };
            industry.Components.Add(loader);
            industry.Components.Add(unloader);

            // Car unloaded and given outbound waybill to interchange
            OpsCarPosition interchangePos = new OpsCarPosition("Whittier Interchange", "interchange.whittier", null!);
            Waybill interchangeWaybill = new Waybill(default, null, interchangePos, 100, false, "tag", 3);

            bool resultInterchange = CarTrackingHelper.IsWaybilledHere(interchangeWaybill, industry, loader);
            Assert.IsFalse(resultInterchange, "A car waybilled to interchange must not match IndustryLoader.");

            // Car waybilled to the sibling unloader on the same track
            OpsCarPosition unloaderPos = new OpsCarPosition("Stenzel Unloader", unloader.Identifier, null!);
            Waybill unloaderWaybill = new Waybill(default, null, unloaderPos, 100, false, "tag", 3);

            bool resultUnloader = CarTrackingHelper.IsWaybilledHere(unloaderWaybill, industry, loader);
            Assert.IsFalse(resultUnloader, "A car waybilled to IndustryUnloader must not match IndustryLoader.");
        }

        [Test]
        public void IsWaybilledHere_IndustryLoader_WhenCarWaybilledToLoaderComponent_ReturnsTrue()
        {
            Industry industry = new Industry { identifier = "stenzel" };
            IndustryLoader loader = new IndustryLoader { Industry = industry, subIdentifier = "loader" };
            industry.Components.Add(loader);

            OpsCarPosition loaderPos = new OpsCarPosition("Stenzel Loader", loader.Identifier, null!);
            Waybill loaderWaybill = new Waybill(default, null, loaderPos, 100, false, "tag", 3);

            bool result = CarTrackingHelper.IsWaybilledHere(loaderWaybill, industry, loader);
            Assert.IsTrue(result, "A car on an IndustryLoader spur with a valid waybill to component must be accepted.");
        }

        [Test]
        public void IsWaybilledHere_TeamTrack_WhenCarWaybilledElsewhere_ReturnsFalse()
        {
            Industry industry = new Industry { identifier = "stenzel" };
            TeamTrack teamTrack = new TeamTrack { Industry = industry, subIdentifier = "team_track" };
            industry.Components.Add(teamTrack);

            OpsCarPosition elsewherePos = new OpsCarPosition("Other Spur", "other_industry.spur", null!);
            Waybill elsewhereWaybill = new Waybill(default, null, elsewherePos, 100, false, "tag", 3);

            bool result = CarTrackingHelper.IsWaybilledHere(elsewhereWaybill, industry, teamTrack);
            Assert.IsFalse(result, "A team track car waybilled elsewhere must be ignored.");
        }

        [Test]
        public void IsWaybilledHere_TeamTrack_WhenCarWaybilledToTeamTrack_ReturnsTrue()
        {
            Industry industry = new Industry { identifier = "stenzel" };
            TeamTrack teamTrack = new TeamTrack { Industry = industry, subIdentifier = "team_track" };
            industry.Components.Add(teamTrack);

            OpsCarPosition teamTrackPos = new OpsCarPosition("Team Track", teamTrack.Identifier, null!);
            Waybill teamTrackWaybill = new Waybill(default, null, teamTrackPos, 100, false, "tag", 3);

            bool result = CarTrackingHelper.IsWaybilledHere(teamTrackWaybill, industry, teamTrack);
            Assert.IsTrue(result, "A team track car waybilled to this team track must be accepted.");
        }

        [Test]
        public void IsWaybilledHere_ProgressionIndustryComponent_WhenWaybilledElsewhereOrHere_EvaluatesCorrectly()
        {
            Industry industry = new Industry { identifier = "stenzel" };
            ProgressionIndustryComponent prog = new ProgressionIndustryComponent { Industry = industry, subIdentifier = "material_spur" };
            industry.Components.Add(prog);

            OpsCarPosition elsewherePos = new OpsCarPosition("Interchange", "interchange.yard", null!);
            Waybill elsewhereWaybill = new Waybill(default, null, elsewherePos, 100, false, "tag", 3);

            Assert.IsFalse(CarTrackingHelper.IsWaybilledHere(null, industry, prog), "Null waybill on progression spur must be ignored.");
            Assert.IsFalse(CarTrackingHelper.IsWaybilledHere(elsewhereWaybill, industry, prog), "Car waybilled elsewhere must be ignored on progression spur.");

            OpsCarPosition progPos = new OpsCarPosition("Material Delivery", prog.Identifier, null!);
            Waybill progWaybill = new Waybill(default, null, progPos, 100, false, "tag", 3);

            Assert.IsTrue(CarTrackingHelper.IsWaybilledHere(progWaybill, industry, prog), "Car waybilled to progression component must be accepted.");
        }

        [Test]
        public void IsCaptiveAutoStop_WhenLoadedStopIsThisUnloader_ReturnsTrue()
        {
            Industry industry = new Industry { identifier = "whittier-sawmill" };
            IndustryUnloader logs = new IndustryUnloader { Industry = industry, subIdentifier = "logs", orderLoads = false };
            industry.Components.Add(logs);

            bool result = CarTrackingHelper.IsCaptiveAutoStop(
                new List<IndustryComponent> { logs },
                loadedAutoDestinationId: logs.Identifier,
                emptyAutoDestinationId: "connelly0.l1");

            Assert.IsTrue(result);
        }

        [Test]
        public void IsCaptiveAutoStop_WhenEmptyStopIsThisLoader_ReturnsTrue()
        {
            Industry industry = new Industry { identifier = "connelly0" };
            IndustryLoader logs = new IndustryLoader { Industry = industry, subIdentifier = "l1", orderEmpties = false };
            industry.Components.Add(logs);

            bool result = CarTrackingHelper.IsCaptiveAutoStop(
                new List<IndustryComponent> { logs },
                loadedAutoDestinationId: "whittier-sawmill.logs",
                emptyAutoDestinationId: logs.Identifier);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsCaptiveAutoStop_WhenComponentStillOrdersCars_ReturnsFalse()
        {
            Industry industry = new Industry { identifier = "whittier-sawmill" };
            IndustryUnloader logs = new IndustryUnloader { Industry = industry, subIdentifier = "logs", orderLoads = true };
            industry.Components.Add(logs);

            bool result = CarTrackingHelper.IsCaptiveAutoStop(
                new List<IndustryComponent> { logs },
                loadedAutoDestinationId: logs.Identifier,
                emptyAutoDestinationId: "connelly0.l1");

            Assert.IsFalse(result);
        }

        [Test]
        public void IsCaptiveAutoStop_WhenEmptyStopIsIndustrySeller_ReturnsTrue()
        {
            Industry industry = new Industry { identifier = "sylva-ties" };
            IndustrySeller tiesSeller = new IndustrySeller { Industry = industry, subIdentifier = "ties-seller" };
            industry.Components.Add(tiesSeller);

            bool isCaptive = CarTrackingHelper.IsCaptiveServiceComponent(tiesSeller);
            Assert.IsTrue(isCaptive, "IndustrySeller must be recognized as captive service component.");

            bool result = CarTrackingHelper.IsCaptiveAutoStop(
                new List<IndustryComponent> { tiesSeller },
                loadedAutoDestinationId: "workcar.mow",
                emptyAutoDestinationId: tiesSeller.Identifier);

            Assert.IsTrue(result, "When empty auto stop matches IndustrySeller, must return true.");
        }

        [Test]
        public void IsWaybilledHere_IndustrySeller_WhenCarWaybilledToSellerComponent_ReturnsTrue()
        {
            Industry industry = new Industry { identifier = "sylva-ties" };
            IndustrySeller seller = new IndustrySeller { Industry = industry, subIdentifier = "ties-seller" };
            industry.Components.Add(seller);

            OpsCarPosition sellerPos = new OpsCarPosition("Scott Tie Plant TO1", seller.Identifier, null!);
            Waybill sellerWaybill = new Waybill(default, null, sellerPos, 0, false, null, 0);

            Assert.IsTrue(CarTrackingHelper.IsWaybilledHere(sellerWaybill, industry, seller), "Car waybilled to IndustrySeller must match specificComponent.");
        }

        [Test]
        public void IsCaptiveAutoStop_WhenCarAutoStopsAreElsewhere_ReturnsFalse()
        {
            Industry industry = new Industry { identifier = "whittier-sawmill" };
            IndustryUnloader logs = new IndustryUnloader { Industry = industry, subIdentifier = "logs", orderLoads = false };
            industry.Components.Add(logs);

            bool result = CarTrackingHelper.IsCaptiveAutoStop(
                new List<IndustryComponent> { logs },
                loadedAutoDestinationId: "sylva-ties.logs",
                emptyAutoDestinationId: "connelly0.l1");

            Assert.IsFalse(result);
        }
    }
}
