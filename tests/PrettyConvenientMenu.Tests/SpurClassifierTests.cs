using System.Collections.Generic;
using Model.Ops;
using NUnit.Framework;
using PrettyConvenientMenu.Data.Spurs;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class SpurClassifierTests
    {
        [Test]
        public void SpurCapabilities_WhenComponentsNullOrEmpty_ReturnsAllFalse()
        {
            SpurCapabilities nullCaps = SpurCapabilities.FromComponents(null);
            Assert.IsFalse(nullCaps.CanUnload);
            Assert.IsFalse(nullCaps.CanLoad);
            Assert.IsFalse(nullCaps.CanRepair);
            Assert.IsFalse(nullCaps.CanTeamTrack);
            Assert.IsFalse(nullCaps.CanStorageTransfer);

            SpurCapabilities emptyCaps = SpurCapabilities.FromComponents(new List<IndustryComponent>());
            Assert.IsFalse(emptyCaps.CanUnload);
            Assert.IsFalse(emptyCaps.CanLoad);
            Assert.IsFalse(emptyCaps.CanRepair);
            Assert.IsFalse(emptyCaps.CanTeamTrack);
            Assert.IsFalse(emptyCaps.CanStorageTransfer);
        }

        [Test]
        public void SpurCapabilities_FromUnloaderAndLoader_DetectsBothCommercialCapabilities()
        {
            List<IndustryComponent> components = new List<IndustryComponent>
            {
                new IndustryUnloader(),
                new IndustryLoader()
            };

            SpurCapabilities caps = SpurCapabilities.FromComponents(components);
            Assert.IsTrue(caps.CanUnload);
            Assert.IsTrue(caps.CanLoad);
            Assert.IsFalse(caps.CanRepair);
            Assert.IsFalse(caps.CanTeamTrack);
            Assert.IsFalse(caps.CanStorageTransfer);
        }

        [Test]
        public void SpurCapabilities_FromRepairTrack_DetectsRepairCapability()
        {
            List<IndustryComponent> components = new List<IndustryComponent>
            {
                new RepairTrack()
            };

            SpurCapabilities caps = SpurCapabilities.FromComponents(components);
            Assert.IsTrue(caps.CanRepair);
            Assert.IsFalse(caps.CanUnload);
            Assert.IsFalse(caps.CanLoad);
        }

        [Test]
        public void SpurCapabilities_FromStorageComponents_DetectsStorageTransfer_WithoutCommercialContamination()
        {
            List<IndustryComponent> components = new List<IndustryComponent>
            {
                new StorageUnloader(),
                new StorageLoader()
            };

            SpurCapabilities caps = SpurCapabilities.FromComponents(components);
            Assert.IsTrue(caps.CanStorageTransfer);
            Assert.IsFalse(caps.CanUnload, "StorageUnloader must not contaminate commercial CanUnload");
            Assert.IsFalse(caps.CanLoad, "StorageLoader must not contaminate commercial CanLoad");
        }

        [Test]
        public void ResolveSpurRole_WhenCanStorageTransfer_ResolvesMowMaterialHandling()
        {
            SpurCapabilities caps = new SpurCapabilities(canUnload: false, canLoad: false, canRepair: false, canTeamTrack: false, canStorageTransfer: true);
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.MowMaterialHandling, role);
        }

        [Test]
        public void ResolveSpurRole_WhenCanRepair_ResolvesLocomotiveRepair()
        {
            SpurCapabilities caps = new SpurCapabilities(canUnload: false, canLoad: false, canRepair: true, canTeamTrack: false, canStorageTransfer: false);
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.LocomotiveRepair, role);
        }

        [Test]
        public void ResolveSpurRole_WhenTeamTrack_ResolvesTeamTrack()
        {
            SpurCapabilities caps = new SpurCapabilities(canUnload: false, canLoad: false, canRepair: false, canTeamTrack: true, canStorageTransfer: false);
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.TeamTrack, role);
        }

        [Test]
        public void ResolveSpurRole_WhenCanLoadAndUnload_ResolvesCommercialDual()
        {
            SpurCapabilities caps = new SpurCapabilities(canUnload: true, canLoad: true, canRepair: false, canTeamTrack: false, canStorageTransfer: false);
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.CommercialDual, role);
        }

        [Test]
        public void ResolveSpurRole_WhenLoadOnly_ResolvesCommercialOutbound()
        {
            SpurCapabilities caps = new SpurCapabilities(canUnload: false, canLoad: true, canRepair: false, canTeamTrack: false, canStorageTransfer: false);
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.CommercialOutbound, role);
        }

        [Test]
        public void ResolveSpurRole_WhenUnloadOnly_ResolvesCommercialInbound()
        {
            SpurCapabilities caps = new SpurCapabilities(canUnload: true, canLoad: false, canRepair: false, canTeamTrack: false, canStorageTransfer: false);
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.CommercialInbound, role);
        }

        [Test]
        public void ResolveSpurRole_WhenNoCapabilities_ResolvesUnknown()
        {
            SpurCapabilities caps = default;
            SpurRole role = SpurClassifier.ResolveSpurRole(caps);
            Assert.AreEqual(SpurRole.Unknown, role);
        }
    }
}
