using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace PrettyConvenientMenu.Settings
{
    /// <summary>
    /// Configuration options for PrettyConvenientMenu, persisted to settings.json in the mod directory.
    /// </summary>
    public sealed class ModSettings
    {
        private const string SettingsFileName = "settings.json";

        public static ModSettings Current { get; internal set; } = new ModSettings();

        [JsonProperty("trackModifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ClickModifierMode TrackModifier { get; set; } = ClickModifierMode.None;

        [JsonProperty("buildingModifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ClickModifierMode BuildingModifier { get; set; } = ClickModifierMode.None;

        [JsonProperty("pinByDefault")]
        public bool PinByDefault { get; set; } = true;

        [JsonProperty("modifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        internal ClickModifierMode? LegacyModifier
        {
            get => null;
            set
            {
                if (value.HasValue)
                {
                    TrackModifier = value.Value;
                    BuildingModifier = value.Value;
                }
            }
        }

        [JsonIgnore]
        public string? FilePath { get; private set; }

        public static ModSettings Load(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory))
            {
                ModEntryPoint.Log.Warning("ModDirectory not provided for settings. Using default settings in-memory.");
                return Current = new ModSettings();
            }

            string settingsPath = Path.Combine(modDirectory, SettingsFileName);

            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    ModSettings? loaded = JsonConvert.DeserializeObject<ModSettings>(json);
                    if (loaded != null)
                    {
                        loaded.FilePath = settingsPath;
                        ModEntryPoint.Log.Info($"Loaded PrettyConvenientMenu settings from {settingsPath} (Track: {loaded.TrackModifier}, Building: {loaded.BuildingModifier})");
                        return Current = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to read settings from {settingsPath}. Generating fresh defaults.");
            }

            // File does not exist or failed to parse: create defaults and save
            ModSettings fresh = new ModSettings
            {
                FilePath = settingsPath,
                TrackModifier = ClickModifierMode.None,
                BuildingModifier = ClickModifierMode.None,
                PinByDefault = true
            };

            fresh.Save();
            return Current = fresh;
        }

        public static void ApplySaved(JObject jobject)
        {
            if (jobject != null)
            {
                try
                {
                    ModSettings? updated = jobject.ToObject<ModSettings>();
                    if (updated != null)
                    {
                        Current.TrackModifier = updated.TrackModifier;
                        Current.BuildingModifier = updated.BuildingModifier;
                        Current.PinByDefault = updated.PinByDefault;
                        ModEntryPoint.Log.Info($"Applied PrettyConvenientMenu settings from UI (Track: {Current.TrackModifier}, Building: {Current.BuildingModifier}, PinByDefault: {Current.PinByDefault})");
                    }
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Error(ex, "Failed to apply updated settings from UI.");
                }
            }
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                ModEntryPoint.Log.Warning("Cannot save settings: FilePath is not configured.");
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(FilePath))
                {
                    File.Replace(tempPath, FilePath, null);
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }

                ModEntryPoint.Log.Info($"Saved PrettyConvenientMenu settings to {FilePath}");
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to save PrettyConvenientMenu settings to {FilePath}");
            }
        }
    }
}
