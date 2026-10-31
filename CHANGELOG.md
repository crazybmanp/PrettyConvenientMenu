# Changelog

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
