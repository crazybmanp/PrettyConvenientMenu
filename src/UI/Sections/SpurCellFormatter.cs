using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.UI.Common;

namespace PrettyConvenientMenu.UI.Sections
{
    /// <summary>
    /// Pure presentation and formatting helper for rolling stock table cells across repair and transfer tracks.
    /// Eliminates inline formatting branches in UI section builders.
    /// </summary>
    internal static class SpurCellFormatter
    {
        /// <summary>
        /// Builds the text string for the Car column, wrapped in a clickable console hyperlink if a valid CarId or train controller match exists.
        /// </summary>
        public static string BuildCarLinkLabel(SpottedCarInfo car)
        {
            string? carId = car.CarId;
            if (string.IsNullOrEmpty(carId))
            {
                carId = global::TrainController.Shared?.CarForString(car.ReportingMark)?.id;
            }
            string carText = $"{car.ReportingMark} ({car.CarType})";

            return !string.IsNullOrEmpty(carId)
                ? $"<link=\"car:{carId}\"><style=ConsoleLink><noparse>{carText}</noparse></style></link>"
                : carText;
        }

        /// <summary>
        /// Resolves the color-coded status badge text and display label for the Status column.
        /// </summary>
        public static (string text, string color) GetStatusDisplay(SpottedCarInfo car)
        {
            string color = StatusBadges.GetCarStatusColor(car);
            string text = !string.IsNullOrEmpty(car.StatusDetail) ? car.StatusDetail : car.Status.ToString();
            return (text, color);
        }

        /// <summary>
        /// Formats Column 3 (Cond) and Column 4 (Parts) for repair track tables.
        /// Distinguishes between cars under repair (showing condition % and parts demand)
        /// and inbound supply cars unloading repair parts (showing cargo tonnage and -- condition).
        /// </summary>
        public static (string condStr, string partsStr) FormatRepairCells(SpottedCarInfo car)
        {
            // Case 1: Car undergoing repair or overhaul (has CustomLoadString formatting condition, e.g. "20%")
            if (!string.IsNullOrEmpty(car.CustomLoadString))
            {
                string cond = car.CustomLoadString!;
                string parts = car.Status == CarStatus.Serviced
                    ? "--"
                    : FormatHelper.FormatWeight(car.CurrentAmount);
                return (cond, parts);
            }

            // Case 2: Inbound boxcar delivering repair parts supply to the repair spur (+ indicates incoming supply)
            return ("--", $"+{FormatHelper.FormatCarLoad(car.CurrentAmount, car.CurrentPercent, car.Units)}");
        }

        /// <summary>
        /// Formats Column 3 (Load) and Column 4 (Rate) for transfer track tables (Commercial, MOW, Team Tracks).
        /// </summary>
        public static (string loadStr, string rateStr) FormatTransferCells(SpottedCarInfo car, FacilityType facilityType)
        {
            string loadStr;

            if (!string.IsNullOrEmpty(car.CustomLoadString))
            {
                loadStr = car.CustomLoadString!;
            }
            else if (facilityType == FacilityType.MaintenanceOfWay)
            {
                if (car.CurrentAmount > 0.001f)
                {
                    string shortAmount = FormatHelper.FormatQuantity(car.CurrentAmount, car.Units);
                    loadStr = !string.IsNullOrEmpty(car.CargoName) && car.CargoName != "Cargo" && car.CargoName != "Material"
                        ? $"{shortAmount} {car.CargoName}"
                        : $"{shortAmount} ({car.CurrentPercent:F0}%)";
                }
                else
                {
                    loadStr = "Empty";
                }
            }
            else
            {
                loadStr = FormatHelper.FormatCarLoad(car.CurrentAmount, car.CurrentPercent, car.Units);
            }

            string rateStr = (car.IsActiveTransfer && car.TransferRatePerMinute > 0.0001f)
                ? FormatHelper.FormatTransferRate(car.TransferRatePerMinute, car.Units)
                : "--";

            return (loadStr, rateStr);
        }

        /// <summary>
        /// Formats the estimated time to completion for Column 5 (ETA).
        /// </summary>
        public static string FormatEta(SpottedCarInfo car)
        {
            return car.EstimatedTimeToCompletion.HasValue
                ? FormatHelper.FormatDuration(car.EstimatedTimeToCompletion)
                : "--";
        }
    }
}
