using System;
using Game;
using Model.Definition.Data;

namespace PrettyConvenientMenu.UI.Common
{
    public static class FormatHelper
    {
        public const float ShortTonInPounds = 2000f;
        public const float MinimumTonThreshold = 200f; // 0.1 Ton

        // =========================================================================
        // Direct Unit Formatters
        // =========================================================================

        public static string FormatWeight(float pounds)
        {
            if (pounds < 0f)
            {
                pounds = 0f;
            }

            if (pounds >= MinimumTonThreshold || Math.Abs(pounds) < 0.001f)
            {
                float tons = pounds / ShortTonInPounds;
                return $"{tons:#,##0.0}T";
            }

            return $"{pounds:#,##0} lbs";
        }

        public static string FormatGallons(float gallons)
        {
            if (gallons < 0f)
            {
                gallons = 0f;
            }

            switch (gallons)
            {
                case <= 0.001f:
                    return "0 gal";
                case >= 1000f:
                {
                    float kGal = gallons / 1000f;
                    return $"{kGal:#,##0.0} kGal";
                }
                default:
                    return $"{gallons:#,##0} gal";
            }
        }

        public static string FormatPieces(float pieces)
        {
            if (pieces < 0f)
            {
                pieces = 0f;
            }

            switch (pieces)
            {
                case <= 0.001f:
                    return "0 pcs";
                case >= 1000f:
                {
                    float kPcs = pieces / 1000f;
                    return $"{kPcs:#,##0.0}k pcs";
                }
                default:
                    return $"{pieces:#,##0} pcs";
            }
        }

        // =========================================================================
        // Generic Quantity Dispatcher
        // =========================================================================

        public static string FormatQuantity(float amount, LoadUnits units = LoadUnits.Pounds) => units switch
        {
            LoadUnits.Pounds => FormatWeight(amount),
            LoadUnits.Gallons => FormatGallons(amount),
            LoadUnits.Quantity => FormatPieces(amount),
            _ => throw new ArgumentOutOfRangeException(nameof(units), units, $"Unsupported LoadUnits: {units}")
        };

        public static string FormatCarLoad(float amount, float percent, LoadUnits units = LoadUnits.Pounds) => $"{FormatQuantity(amount, units)} ({percent:F0}%)";

        public static string FormatDailyRate(float amountPerDay, LoadUnits units = LoadUnits.Pounds) => $"{FormatQuantity(amountPerDay, units)}/day";

        // =========================================================================
        // Direct Transfer Rate Formatters
        // =========================================================================

        public static string FormatWeightTransferRate(float poundsPerMinute)
        {
            // 1. If >= 0.1 T/min (>= 200 lbs/min), display as T/min
            if (poundsPerMinute >= MinimumTonThreshold)
            {
                float tonsPerMin = poundsPerMinute / ShortTonInPounds;
                return $"{tonsPerMin:#,##0.0}T/min";
            }

            // 2. Otherwise check if >= 0.1 T/hr (>= 200 lbs/hr)
            float poundsPerHour = poundsPerMinute * 60f;
            if (poundsPerHour >= MinimumTonThreshold)
            {
                float tonsPerHour = poundsPerHour / ShortTonInPounds;
                return $"{tonsPerHour:#,##0.0}T/hr";
            }

            // 3. Ultra-slow rate (< 0.1 T/hr): display as lbs/min
            return $"{poundsPerMinute:#,##0} lbs/min";
        }

        public static string FormatGallonsTransferRate(float gallonsPerMinute)
        {
            if (gallonsPerMinute >= 1000f)
            {
                float kGalPerMin = gallonsPerMinute / 1000f;
                return $"{kGalPerMin:#,##0.0} kGal/min";
            }

            return $"{gallonsPerMinute:#,##0} gpm";
        }

        public static string FormatPiecesTransferRate(float piecesPerMinute)
        {
            if (piecesPerMinute >= 1000f)
            {
                float kPcsPerMin = piecesPerMinute / 1000f;
                return $"{kPcsPerMin:#,##0.0}k pcs/min";
            }

            return $"{piecesPerMinute:#,##0} pcs/min";
        }

        // =========================================================================
        // Generic Transfer Rate Dispatcher
        // =========================================================================

        public static string FormatTransferRate(float amountPerMinute, LoadUnits units = LoadUnits.Pounds)
        {
            if (amountPerMinute <= 0.0001f)
            {
                return "--";
            }

            return units switch
            {
                LoadUnits.Pounds => FormatWeightTransferRate(amountPerMinute),
                LoadUnits.Gallons => FormatGallonsTransferRate(amountPerMinute),
                LoadUnits.Quantity => FormatPiecesTransferRate(amountPerMinute),
                _ => throw new ArgumentOutOfRangeException(nameof(units), units, $"Unsupported LoadUnits: {units}")
            };
        }

        // =========================================================================
        // Game Time & Duration Formatters
        // =========================================================================

        public static string FormatInGameCompletion(float minutesFromNow)
        {
            if (minutesFromNow <= 0.001f)
            {
                return "Now";
            }

            GameDateTime now = TimeWeather.Now;
            GameDateTime finishTime = new GameDateTime(now.TotalSeconds + (minutesFromNow * 60.0));

            return finishTime.Day == now.Day ? finishTime.TimeString() : finishTime.ToString();
        }

        public static string FormatInGameRunway(float daysFromNow)
        {
            if (daysFromNow <= 0.0001f)
            {
                return "Immediate";
            }

            GameDateTime now = TimeWeather.Now;
            GameDateTime finishTime = new GameDateTime(now.TotalSeconds + (daysFromNow * 86400.0));

            return finishTime.Day == now.Day ? finishTime.TimeString() : finishTime.ToString();
        }

        public static string FormatDuration(TimeSpan? duration)
        {
            if (!duration.HasValue)
            {
                return "--";
            }

            TimeSpan val = duration.Value;
            if (val.TotalMinutes < 1.0)
            {
                return "< 1 min";
            }

            if (val.TotalHours < 1.0)
            {
                return $"~{val.Minutes} min";
            }

            if (val.TotalDays < 1.0)
            {
                return $"~{val.Hours}h {val.Minutes:D2}m";
            }

            int totalDays = (int)val.TotalDays;
            return $"~{totalDays}d {val.Hours}h";
        }
    }
}
