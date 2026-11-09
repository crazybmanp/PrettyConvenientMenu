using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PrettyConvenientMenu.Settings;

namespace PrettyConvenientMenu.Tests
{
    [TestFixture]
    public class ModSettingsTests
    {
        private string testDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), "PCM_SettingsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, true);
                }
                catch
                {
                    // Ignore cleanup failure in temp directory
                }
            }
        }

        [Test]
        public void DefaultConstructor_DefaultsModifiersToNone_AndPinByDefaultToFalse()
        {
            var settings = new ModSettings();
            Assert.AreEqual(ClickModifierMode.None, settings.TrackModifier);
            Assert.AreEqual(ClickModifierMode.None, settings.BuildingModifier);
            Assert.IsFalse(settings.PinByDefault);
        }

        [Test]
        public void JsonSerialization_SerializesEnumAsString_AndPinByDefault()
        {
            var settings = new ModSettings
            {
                TrackModifier = ClickModifierMode.Shift,
                BuildingModifier = ClickModifierMode.Alt,
                PinByDefault = true
            };
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            StringAssert.Contains("\"Shift\"", json);
            StringAssert.Contains("\"Alt\"", json);
            StringAssert.Contains("\"pinByDefault\": true", json);

            var deserialized = JsonConvert.DeserializeObject<ModSettings>(json);
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(ClickModifierMode.Shift, deserialized!.TrackModifier);
            Assert.AreEqual(ClickModifierMode.Alt, deserialized.BuildingModifier);
            Assert.IsTrue(deserialized.PinByDefault);
        }

        [Test]
        public void Load_GeneratesDefaultSettingsFile_WhenFileDoesNotExist()
        {
            string settingsPath = Path.Combine(testDirectory, "settings.json");
            Assert.IsFalse(File.Exists(settingsPath));

            var loaded = ModSettings.Load(testDirectory);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(ClickModifierMode.None, loaded.TrackModifier);
            Assert.AreEqual(ClickModifierMode.None, loaded.BuildingModifier);
            Assert.IsFalse(loaded.PinByDefault);
            Assert.IsTrue(File.Exists(settingsPath));

            string json = File.ReadAllText(settingsPath);
            StringAssert.Contains("\"trackModifier\": \"None\"", json);
            StringAssert.Contains("\"buildingModifier\": \"None\"", json);
            StringAssert.Contains("\"pinByDefault\": false", json);
        }

        [Test]
        public void Load_ReadsExistingSettingsFile()
        {
            string settingsPath = Path.Combine(testDirectory, "settings.json");
            File.WriteAllText(settingsPath, "{\n  \"trackModifier\": \"Alt\",\n  \"buildingModifier\": \"Any\",\n  \"pinByDefault\": true\n}");

            var loaded = ModSettings.Load(testDirectory);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(ClickModifierMode.Alt, loaded.TrackModifier);
            Assert.AreEqual(ClickModifierMode.Any, loaded.BuildingModifier);
            Assert.IsTrue(loaded.PinByDefault);
            Assert.AreEqual(settingsPath, loaded.FilePath);
        }

        [Test]
        public void Load_LegacyModifier_MigratesBothTrackAndBuilding()
        {
            string settingsPath = Path.Combine(testDirectory, "settings.json");
            File.WriteAllText(settingsPath, "{\n  \"modifier\": \"Shift\"\n}");

            var loaded = ModSettings.Load(testDirectory);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(ClickModifierMode.Shift, loaded.TrackModifier);
            Assert.AreEqual(ClickModifierMode.Shift, loaded.BuildingModifier);
        }

        [Test]
        public void ApplySaved_UpdatesCurrentSettings()
        {
            var jObject = JObject.Parse("{\n  \"trackModifier\": \"Any\",\n  \"buildingModifier\": \"Control\",\n  \"pinByDefault\": true\n}");
            ModSettings.ApplySaved(jObject);

            Assert.AreEqual(ClickModifierMode.Any, ModSettings.Current.TrackModifier);
            Assert.AreEqual(ClickModifierMode.Control, ModSettings.Current.BuildingModifier);
            Assert.IsTrue(ModSettings.Current.PinByDefault);
        }

        [Test]
        public void Save_WritesUpdatedSettingsToDisk()
        {
            var loaded = ModSettings.Load(testDirectory);
            loaded.TrackModifier = ClickModifierMode.Shift;
            loaded.BuildingModifier = ClickModifierMode.Control;
            loaded.PinByDefault = true;
            loaded.Save();

            string settingsPath = Path.Combine(testDirectory, "settings.json");
            string json = File.ReadAllText(settingsPath);
            StringAssert.Contains("\"Shift\"", json);
            StringAssert.Contains("\"Control\"", json);
            StringAssert.Contains("\"pinByDefault\": true", json);

            // Re-load to verify persistence
            var reloaded = ModSettings.Load(testDirectory);
            Assert.AreEqual(ClickModifierMode.Shift, reloaded.TrackModifier);
            Assert.AreEqual(ClickModifierMode.Control, reloaded.BuildingModifier);
            Assert.IsTrue(reloaded.PinByDefault);
        }

        [Test]
        public void Load_RecoversToDefaults_WhenSettingsFileIsCorrupt()
        {
            string settingsPath = Path.Combine(testDirectory, "settings.json");
            File.WriteAllText(settingsPath, "{ invalid json content !!! }");

            var loaded = ModSettings.Load(testDirectory);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(ClickModifierMode.None, loaded.TrackModifier);
            Assert.AreEqual(ClickModifierMode.None, loaded.BuildingModifier);
            Assert.IsFalse(loaded.PinByDefault);
        }
    }
}
