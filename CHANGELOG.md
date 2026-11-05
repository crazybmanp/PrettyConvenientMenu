# Changelog

## 0.3.0
- Fixed logging camps and multi-track facilities only showing data for a single track, adding composite buffers to aggregate capacity, stock, and recipes across all tracks (#7).
- Expanded the scroll area to encompass the entire window rather than just the rolling stock section, allowing all buffers and tracks to scroll together so nothing is cut off on large facilities (#5).
- Added configurable modifier keys for clicking tracks vs. buildings (`None`, `Any`, `Control`, `Alt`, `Shift`) to prevent accidental opening during switching operations, configurable in **Preferences -> Mods** via `railroader-mod-settings` by ashenmare (#9).
- Set mod scope to `optional` in `Info.json`.

## 0.2.1
- Fixed MOW supply cars (ties, ballast, concrete) being incorrectly marked as Misspotted at IndustrySeller spurs like Scott Tie Plant and Rock Products (#4).
- Added support for TeleportLoadingIndustry raw material mines (Alarka Copper Mines, Robinson Gap Coal, Nantahala Talc & Limestone): empty inbound tracks are now classified as loading spurs, queue ETAs are accurately projected from the mine's loading cycle, and buffer capacities are correctly reported.
- Fixed MOW depot multi-load storage unloaders and parallel batch loaders reporting incorrect concurrent transfer stats or empty car cargo (#3).

## 0.2.0 - Refactoring and Bugfixes, with a small UI rebrand
- Renamed the window to **Facility Monitor** (was "Production Monitor"); the Company window button is now **Open Facility Monitor**.
- Rolling stock is now classified per track (spur) instead of per industry component, so every facility type shows its cars grouped by the track they are actually on.
- Added an **Arriving** group showing waybilled cars en route to locomotive service terminals, repair tracks, and MOW yards.
- Repair tracks now have their own table columns (**Cond** and **Parts**) instead of the generic Load/Rate columns.
- Transfer queue ETAs are now sequenced across the cars on a spur.
- Fixed incorrect waybill checks on industry tracks, team tracks, and progression tracks (#1).
- Fixed unloaded cars still waybilled to the facility being marked as Misspotted.
- Fixed the rolling stock table leaving an uneven gap against the window edge or the scrollbar, including when resizing the window.

## 0.1.0
- Initial experimental release.
