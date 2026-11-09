using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PrettyConvenientMenu.Settings;

namespace PrettyConvenientMenu.UI.Preferences
{
    /// <summary>
    /// Coordinates UI settings registration with the shared Preferences > Mods tab.
    /// Kept in a dedicated class with object-typed fields to prevent Mono type loading
    /// issues if game UI types change.
    /// </summary>
    internal static class SettingsUi
    {
        private static object? _section;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Start(Harmony harmony, string settingsPath)
        {
            Stop();

            SettingField[] fields = new[]
            {
                SettingField.Choice(
                    "trackModifier",
                    "Track Click Modifier",
                    new[] { "None", "Any", "Control", "Alt", "Shift" },
                    0, // None
                    true,
                    "Requires holding this modifier key when clicking tracks or rails in the world to open the menu."
                ),
                SettingField.Choice(
                    "buildingModifier",
                    "Building Click Modifier",
                    new[] { "None", "Any", "Control", "Alt", "Shift" },
                    0, // None
                    true,
                    "Requires holding this modifier key when clicking buildings or facilities in the world to open the menu."
                ),
                SettingField.Bool(
                    "pinByDefault",
                    "Pin Windows by Default",
                    false,
                    true,
                    "When enabled, newly opened facility windows are pinned by default. When disabled, opening a new window automatically closes unpinned windows."
                ),
            };

            Action<string> log = m => ModEntryPoint.Log.Warning("Pretty Convenient Menu: " + m);
            _section = new SettingsSection("A Pretty Convenient Menu", settingsPath, fields, ModSettings.ApplySaved, log);
            PreferencesModsTab.Add(harmony, b => (_section as SettingsSection)?.Build(b, true), log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Stop()
        {
            PreferencesModsTab.Remove();
            _section = null;
        }
    }
}
