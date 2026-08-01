using System;

namespace GothiaGripPlugin
{
    internal static class Program
    {
        private const double StepSeconds = 1.0 / 60.0;
        private static int assertions;

        private static int Main()
        {
            try
            {
                NormalDrivingDoesNotWarn();
                OneFrameSpikeDoesNotWarn();
                WheelSpinWarnsAndHolds();
                BrakeLockWarns();
                SideSlipWarns();
                MeasuredNormalSideRatioDoesNotWarn();
                SubOnePercentThrottleDoesNotBecomeEightyPercent();
                InvalidWheelDataDoesNotLookLikeBrakeLock();
                NormalOilTemperatureDoesNotWarn();
                HighOilTemperatureWarnsWithHysteresis();
                HighWaterTemperatureWarnsWhenAvailable();

                Console.WriteLine("PASS: " + assertions + " detector assertions");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception.Message);
                return 1;
            }
        }

        private static void NormalDrivingDoesNotWarn()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 240; index++)
            {
                snapshot = detector.Update(
                    Frame(200.0, 197.0, 202.0, 198.0, 203.0, 55.56, 1.2, 80.0, 0.0),
                    now += StepSeconds);
            }

            Expect(snapshot.DataAvailable, "Normal telemetry should be available.");
            Expect(!snapshot.GripLost, "Normal high-speed driving must not warn.");
            Expect(!snapshot.WarningBlink, "Normal high-speed driving must not blink.");
        }

        private static void OneFrameSpikeDoesNotWarn()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;

            for (int index = 0; index < 60; index++)
            {
                detector.Update(Frame(100.0, 100.0, 100.0, 100.0, 100.0, 27.78, 0.2, 70.0, 0.0), now += StepSeconds);
            }

            GripSnapshot spike = detector.Update(
                Frame(100.0, 100.0, 100.0, 113.0, 113.0, 27.78, 0.2, 70.0, 0.0),
                now += StepSeconds);

            for (int index = 0; index < 30; index++)
            {
                spike = detector.Update(Frame(100.0, 100.0, 100.0, 100.0, 100.0, 27.78, 0.2, 70.0, 0.0), now += StepSeconds);
            }

            Expect(!spike.GripLost, "A one-frame telemetry spike must be debounced.");
        }

        private static void WheelSpinWarnsAndHolds()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 45; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 101.0, 101.0, 142.0, 142.0, 27.78, 0.3, 80.0, 0.0),
                    now += StepSeconds);
            }

            Expect(snapshot.GripLost, "Sustained driven-wheel spin should warn.");
            Expect(snapshot.WheelSpin, "WheelSpin should identify the cause.");
            Expect(snapshot.WarningReason.Contains("Wheel spin"), "WarningReason should include wheel spin.");

            bool sawOn = snapshot.WarningBlink;
            bool sawOff = false;
            for (int index = 0; index < 60; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 100.0, 100.0, 100.0, 100.0, 27.78, 0.2, 0.0, 0.0),
                    now += StepSeconds);
                sawOn = sawOn || snapshot.WarningBlink;
                sawOff = sawOff || (snapshot.GripLost && !snapshot.WarningBlink);
            }

            Expect(sawOn && sawOff, "The plugin-owned warning signal should visibly alternate.");

            for (int index = 0; index < 120; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 100.0, 100.0, 100.0, 100.0, 27.78, 0.2, 0.0, 0.0),
                    now += StepSeconds);
            }
            Expect(snapshot.GripLost, "The warning should still be held after roughly three seconds.");

            for (int index = 0; index < 90; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 100.0, 100.0, 100.0, 100.0, 27.78, 0.2, 0.0, 0.0),
                    now += StepSeconds);
            }
            Expect(!snapshot.GripLost, "The warning should eventually clear after grip returns.");
        }

        private static void BrakeLockWarns()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 45; index++)
            {
                snapshot = detector.Update(
                    Frame(120.0, 72.0, 72.0, 70.0, 70.0, 33.33, 0.2, 0.0, 80.0),
                    now += StepSeconds);
            }

            Expect(snapshot.GripLost, "Sustained wheel lock under braking should warn.");
            Expect(snapshot.BrakeLock, "BrakeLock should identify the cause.");
        }

        private static void SideSlipWarns()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 7; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 100.0, 100.0, 100.0, 100.0, 27.78, 3.64, 30.0, 0.0),
                    now += StepSeconds);
            }

            Expect(snapshot.GripLost, "A seven-frame side-slip event at the measured 0.131 ratio should latch.");
            Expect(snapshot.SideSlip, "SideSlip should identify the cause.");
            Expect(snapshot.SideSlipRatio > 0.12, "SideSlipRatio should expose the measured signal.");
        }

        private static void MeasuredNormalSideRatioDoesNotWarn()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 300; index++)
            {
                snapshot = detector.Update(
                    Frame(272.0, 267.0, 273.0, 269.0, 275.0, 75.0, 2.22, 60.0, 0.0),
                    now += StepSeconds);
            }

            Expect(snapshot.SideSlipRatio >= 0.0295 && snapshot.SideSlipRatio <= 0.0297,
                "The normal-run side-ratio fixture should match the measured maximum.");
            Expect(!snapshot.SideSlip, "The measured normal maximum side ratio must not activate SideSlip.");
            Expect(!snapshot.GripLost, "The measured normal maximum side ratio must never latch a warning.");
        }

        private static void SubOnePercentThrottleDoesNotBecomeEightyPercent()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 60; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 100.0, 100.0, 145.0, 145.0, 27.78, 0.2, 0.8, 0.0),
                    now += StepSeconds);
            }

            Expect(!snapshot.WheelSpin, "Throttle 0.8 on SimHub's 0 to 100 scale must remain below the spin gate.");
            Expect(!snapshot.GripLost, "Sub-one-percent throttle must not latch a wheel-spin warning.");
        }

        private static void InvalidWheelDataDoesNotLookLikeBrakeLock()
        {
            GripDetector detector = new GripDetector();
            double now = 0.0;
            GripSnapshot snapshot = GripSnapshot.Unavailable;

            for (int index = 0; index < 60; index++)
            {
                snapshot = detector.Update(
                    Frame(100.0, 20.0, 0.0, 0.0, 0.0, 27.78, 0.2, 0.0, 100.0),
                    now += StepSeconds);
            }

            Expect(snapshot.DataAvailable, "Valid velocity should keep partial telemetry available.");
            Expect(!snapshot.BrakeLock, "One nonzero wheel plus three defaults must not be treated as wheel lock.");
            Expect(!snapshot.GripLost, "Partial wheel defaults alone must not warn.");

            snapshot = detector.Update(
                Frame(100.0, 0.0, 0.0, 0.0, 0.0, double.NaN, double.NaN, 0.0, 100.0),
                now += StepSeconds);
            Expect(!snapshot.DataAvailable, "Completely invalid grip inputs should report unavailable.");
        }

        private static void NormalOilTemperatureDoesNotWarn()
        {
            TemperatureWarningDetector detector = new TemperatureWarningDetector();
            TemperatureSnapshot snapshot = detector.Update(110.0, 0.0, 0.0);

            Expect(snapshot.DataAvailable, "GT7 oil temperature should make temperature data available.");
            Expect(snapshot.OilTemperatureC == 110.0, "The oil temperature should be published.");
            Expect(snapshot.WaterTemperatureC == 0.0, "A zero water value should remain unavailable.");
            Expect(!snapshot.WarningActive, "A normal 110 degree oil temperature must not warn.");
        }

        private static void HighOilTemperatureWarnsWithHysteresis()
        {
            TemperatureWarningDetector detector = new TemperatureWarningDetector();
            TemperatureSnapshot snapshot = detector.Update(125.0, 0.0, 1.0);

            Expect(snapshot.OilWarning, "Oil at 125 degrees should activate the oil warning.");
            Expect(snapshot.WarningActive, "High oil temperature should activate the combined warning.");
            Expect(snapshot.WarningBlink, "A new temperature warning should begin in its visible phase.");

            snapshot = detector.Update(123.0, 0.0, 1.2);
            Expect(snapshot.OilWarning, "Oil warning should stay active inside the hysteresis band.");

            snapshot = detector.Update(121.0, 0.0, 1.4);
            Expect(!snapshot.WarningActive, "Oil warning should clear at the release threshold.");
        }

        private static void HighWaterTemperatureWarnsWhenAvailable()
        {
            TemperatureWarningDetector detector = new TemperatureWarningDetector();
            TemperatureSnapshot snapshot = detector.Update(110.0, 106.0, 2.0);

            Expect(snapshot.WaterWarning, "Available water temperature above 105 degrees should warn.");
            Expect(snapshot.WarningReason.Contains("Water"), "The warning reason should identify water temperature.");
        }

        private static GripFrame Frame(
            double speed,
            double frontLeft,
            double frontRight,
            double rearLeft,
            double rearRight,
            double forward,
            double lateral,
            double throttle,
            double brake)
        {
            return new GripFrame(
                speed,
                frontLeft,
                frontRight,
                rearLeft,
                rearRight,
                forward,
                lateral,
                throttle,
                brake);
        }

        private static void Expect(bool condition, string message)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
