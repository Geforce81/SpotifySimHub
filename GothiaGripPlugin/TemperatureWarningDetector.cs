using System;

namespace GothiaGripPlugin
{
    internal sealed class TemperatureSnapshot
    {
        public static readonly TemperatureSnapshot Unavailable = new TemperatureSnapshot(
            0.0, 0.0, false, false, false, false, false, string.Empty);

        public TemperatureSnapshot(
            double oilTemperatureC,
            double waterTemperatureC,
            bool dataAvailable,
            bool oilWarning,
            bool waterWarning,
            bool warningActive,
            bool warningBlink,
            string warningReason)
        {
            OilTemperatureC = oilTemperatureC;
            WaterTemperatureC = waterTemperatureC;
            DataAvailable = dataAvailable;
            OilWarning = oilWarning;
            WaterWarning = waterWarning;
            WarningActive = warningActive;
            WarningBlink = warningBlink;
            WarningReason = warningReason;
        }

        public double OilTemperatureC { get; private set; }
        public double WaterTemperatureC { get; private set; }
        public bool DataAvailable { get; private set; }
        public bool OilWarning { get; private set; }
        public bool WaterWarning { get; private set; }
        public bool WarningActive { get; private set; }
        public bool WarningBlink { get; private set; }
        public string WarningReason { get; private set; }
    }

    internal sealed class TemperatureWarningDetector
    {
        public const double OilWarningThresholdC = 125.0;
        public const double WaterWarningThresholdC = 105.0;

        private const double OilReleaseThresholdC = 121.0;
        private const double WaterReleaseThresholdC = 101.0;
        private const double BlinkOnSeconds = 0.55;
        private const double BlinkOffSeconds = 0.35;

        private bool oilWarning;
        private bool waterWarning;
        private bool warningWasActive;
        private double blinkStartedSeconds;

        public void Reset()
        {
            oilWarning = false;
            waterWarning = false;
            warningWasActive = false;
            blinkStartedSeconds = 0.0;
        }

        public TemperatureSnapshot Update(double oilTemperatureC, double waterTemperatureC, double nowSeconds)
        {
            if (!IsFinite(nowSeconds))
            {
                Reset();
                return TemperatureSnapshot.Unavailable;
            }

            bool oilAvailable = IsTemperatureAvailable(oilTemperatureC, 200.0);
            bool waterAvailable = IsTemperatureAvailable(waterTemperatureC, 160.0);

            if (!oilAvailable)
            {
                oilWarning = false;
            }
            else if (!oilWarning && oilTemperatureC >= OilWarningThresholdC)
            {
                oilWarning = true;
            }
            else if (oilWarning && oilTemperatureC <= OilReleaseThresholdC)
            {
                oilWarning = false;
            }

            if (!waterAvailable)
            {
                waterWarning = false;
            }
            else if (!waterWarning && waterTemperatureC >= WaterWarningThresholdC)
            {
                waterWarning = true;
            }
            else if (waterWarning && waterTemperatureC <= WaterReleaseThresholdC)
            {
                waterWarning = false;
            }

            bool warningActive = oilWarning || waterWarning;
            if (warningActive && !warningWasActive)
            {
                blinkStartedSeconds = nowSeconds;
            }
            warningWasActive = warningActive;

            double cycleSeconds = BlinkOnSeconds + BlinkOffSeconds;
            double cyclePosition = PositiveModulo(nowSeconds - blinkStartedSeconds, cycleSeconds);
            bool warningBlink = warningActive && cyclePosition < BlinkOnSeconds;

            string reason = string.Empty;
            if (oilWarning)
            {
                reason = "Oil temperature";
            }
            if (waterWarning)
            {
                reason = reason.Length == 0 ? "Water temperature" : "Oil + water temperature";
            }

            return new TemperatureSnapshot(
                oilAvailable ? Math.Round(oilTemperatureC, 1) : 0.0,
                waterAvailable ? Math.Round(waterTemperatureC, 1) : 0.0,
                oilAvailable || waterAvailable,
                oilWarning,
                waterWarning,
                warningActive,
                warningBlink,
                reason);
        }

        private static bool IsTemperatureAvailable(double value, double maximum)
        {
            return IsFinite(value) && value > 0.0 && value <= maximum;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double result = value % modulus;
            return result < 0.0 ? result + modulus : result;
        }
    }
}
