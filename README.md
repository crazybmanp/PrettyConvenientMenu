# A Pretty Convenient Menu

Adds a *really convenient* **Facility Monitor** menu to Railroader that is actually convenient to get to. Click on an industry building or track in the world (or open it from the Company window) to see storage buffers, production and consumption rates, locomotive service supplies, repair track part demand, and car-by-car loading/unloading progress.

---

## Opening the Monitor

You can open the monitor for an industry or service facility by clicking on its buildings in the 3D world, clicking on its track, or using the button in the Company window.

- **Click on an Industry Building:** Left-click on an industry building, loading dock, coaling tower, diesel stand, or shop structure in the 3D world to open its monitor. The mod detects buildings by their proximity to an industry's spotting tracks. Almost any building close to a spotting track works; however, because some buildings in Railroader lack colliders, not all of them will respond to clicks.
- **Click on a Spotting Track:** If a building doesn't respond, you can always left-click directly on the facility's spotting, loading, or service tracks. Clicks on rolling stock, switch stands, turntables, signals, and cab controls are automatically ignored so normal switching and train handling are never interrupted.
- **From the Company Window:** Open the **Company** window, select the **Locations** tab, choose an industry, and click **Open Facility Monitor** under the Facility Monitor section.

### Window Links & Navigation

- **Company Panel Button:** Click the **Company Panel** button in the top-right corner of the monitor window to jump directly to that industry's page in the native Company window.
- **Car Inspector Links:** Every car in the rolling stock table is clickable. Click a car's reporting mark to open its native **Car Inspector** window, or **Shift-click** to select the car.

---

## Supported Facilities

### 1. Commercial Freight Industries
- **Contract & Operational Status:** Shows the active contract tier, throughput multiplier (`%`) or daily production recipe, and whether the facility is `Running`, `Idle` (no active contract), `Starved`, or `Blocked`.
- **Bottleneck Diagnostics:** Displays clear alerts identifying which specific input material has run dry, or which output buffer is completely full and halting production.
- **Input & Output Storage Buffers:**
  - Current stock vs. maximum storage capacity for each input and output commodity.
  - **Daily Consumption & Production Rates:** Shows exact per-day consumption (inputs) and production (outputs) scaled by your active contract tier.
  - **Runway & Fill Timers:** Estimates how long current input reserves will last before running out, or how long until output storage fills to capacity.
  - **Spotted Cargo Pipeline:** Displays how much cargo is actively unloading from or loading into cars currently spotted on the spur.

### 2. Locomotive Service & Repair Terminals
- **Fuel & Consumable Reserves:** Shows current storage levels and maximum capacity for **Coal**, **Diesel Fuel**, **Water**, and **Sand**, along with any supply cars currently unloading on the service track or arriving en route.
- **Repair Parts & Shop Demand:** Compares your available repair parts (in shop storage, actively unloading from spotted boxcars, and arriving en route) against the parts required for **Program** and **Spot** repair work:
  - **Program Demand:** Parts required by all equipment waybilled for repair or overhaul at this shop, whether already spotted on the repair track or still en route.
  - **Spot Repair Demand:** Parts required by unbilled equipment spotted on the repair track.
  - **Arriving Shipments:** Parts aboard waybilled supply cars currently en route to the terminal.
  - **Supply Warnings:** Warns you when the total parts supply cannot cover your **Program Demand**, or when combined **Program + Spot** repair work exceeds supply (meaning spot repairs may consume parts needed for program work).

### 3. Maintenance of Way (MOW) Yards
- **Material Reserves:** Shows current yard stock and maximum storage capacity for MOW commodities (ballast, ties, rail, etc.).
- **Supply & Work Train Tracking:** Distinguishes between incoming supply cars unloading materials into yard storage and MOW work cars loading materials out of the yard, plus waybilled cars arriving en route.

### 4. Station Depots & House Tracks
- Tracks local freight cars spotted on non-contracted station house tracks and public team tracks, showing cargo load, transfer rates, and completion ETAs.

---

## Rolling Stock Table

Groups all stationary cars at the facility by the specific spur or service track they are sitting on (plus an **Arriving** section for waybilled cars heading to locomotive service terminals, repair tracks, and MOW yards). Each row shows the car, its status, two columns that depend on the kind of track, and an ETA:

- **Transfer tracks** (commercial, team, house, MOW, and supply spurs) show **Load** and **Rate** columns.
- **Repair tracks** show **Cond** (the car's condition) and **Parts** (repair parts needed, or arriving on supply cars) columns.

- **Load & Fill Progress:** Shows each car's cargo type, current quantity vs. capacity (`Tons`, `Gallons`, `Pounds`, or `Qty`), and fill percentage (`%`).
- **Live Transfer Rates:** Displays the per-minute loading or unloading rate for cars actively transferring cargo.
- **Sequential Queue ETAs:** Calculates completion ETAs across the entire spur queue, accounting for cars ahead in line and remaining facility storage space or available stock.
- **Car Status Breakdown:**
  - `Loading` / `Unloading`: Actively transferring cargo with live transfer rate and time remaining.
  - `Queued`: Spotted on the track and waiting for an active car ahead to finish, showing cumulative ETA.
  - `Buffer Full` / `Buffer Empty` / `Storage Full` / `Yard Depleted`: Transfer paused because the facility's storage buffer is full or depleted.
  - `Program Overhaul` / `Program Repair` / `Spot Repair`: Equipment on a repair track, showing the remaining repair parts tonnage required and condition progress.
  - `Serviced (Ready)` / `Completed`: Servicing, loading, or unloading is finished and the car is ready to be pulled.
  - `Dest: ...`: Car is staged in a MOW yard with a waybill for another destination.
  - `En Route`: Waybilled to a service terminal, repair track, or MOW yard and currently in transit on the railroad.

---

## Installation

**Requirements:**
- **DLL Mods Enabled:** Because this is a code mod, you must enable DLL mods in Railroader's settings/mod manager for it to load.

**Steps:**
1. Download the latest `APrettyConvenientMenu_v<version>.zip` from [Releases](https://github.com/crazybmanp/PrettyConvenientMenu/releases).
2. Extract the `CRZBP.APrettyConvenientMenu` folder into your `Railroader\Mods` directory.
3. Launch Railroader, ensure **DLL Mods** are enabled in the settings, and enable **A Pretty Convenient Menu**.

---

## Multiplayer

Multiplayer support is currently **untested**. While the mod only reads local industry and car state, some data may only be available on the host.

---

## Support & Bug Reports

Please report any issues or feedback via [GitHub Issues](https://github.com/crazybmanp/PrettyConvenientMenu/issues).