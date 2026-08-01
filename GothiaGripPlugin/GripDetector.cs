using System;

namespace GothiaGripPlugin
{
    internal struct GripFrame
    {
        public GripFrame(
            double speedKmh,
            double frontLeftWheelSpeed,
            double frontRightWheelSpeed,
            double rearLeftWheelSpeed,
            double rearRightWheelSpeed,
            double forwardVelocity,
            double lateralVelocity,
            double throttle,
            double brake)
        {
            SpeedKmh = speedKmh;
            FrontLeftWheelSpeed = frontLeftWheelSpeed;
            FrontRightWheelSpeed = frontRightWheelSpeed;
            RearLeftWheelSpeed = rearLeftWheelSpeed;
            RearRightWheelSpeed = rearRightWheelSpeed;
            ForwardVelocity = forwardVelocity;
            LateralVelocity = lateralVelocity;
            Throttle = throttle;
            Brake = brake;
        }

        public double SpeedKmh { get; private set; }
        public double FrontLeftWheelSpeed { get; private set; }
        public double FrontRightWheelSpeed { get; private set; }
        public double RearLeftWheelSpeed { get; private set; }
        public double RearRightWheelSpeed { get; private set; }
        public double ForwardVelocity { get; private set; }
        public double LateralVelocity { get; private set; }
        public double Throttle { get; private set; }
        public double Brake { get; private set; }
    }

    internal sealed class GripSnapshot
    {
        public static readonly GripSnapshot Unavailable = new GripSnapshot(
            false, false, 0.0, false, false, false, false, string.Empty, 0, false,
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        public GripSnapshot(
            bool gripLost,
            bool warningBlink,
            double slipLevel,
            bool active,
            bool wheelSpin,
            bool brakeLock,
            bool sideSlip,
            string warningReason,
            int typeCode,
            bool dataAvailable,
            double wheelDifferenceKmh,
            double spinDifferenceKmh,
            double lockDifferenceKmh,
            double sideSlipRatio,
            double slipAngleDegrees,
            double frontLeftSlipPercent,
            double frontRightSlipPercent,
            double rearLeftSlipPercent,
            double rearRightSlipPercent)
        {
            GripLost = gripLost;
            WarningBlink = warningBlink;
            SlipLevel = slipLevel;
            Active = active;
            WheelSpin = wheelSpin;
            BrakeLock = brakeLock;
            SideSlip = sideSlip;
            WarningReason = warningReason;
            TypeCode = typeCode;
            DataAvailable = dataAvailable;
            WheelDifferenceKmh = wheelDifferenceKmh;
            SpinDifferenceKmh = spinDifferenceKmh;
            LockDifferenceKmh = lockDifferenceKmh;
            SideSlipRatio = sideSlipRatio;
            SlipAngleDegrees = slipAngleDegrees;
            FrontLeftSlipPercent = frontLeftSlipPercent;
            FrontRightSlipPercent = frontRightSlipPercent;
            RearLeftSlipPercent = rearLeftSlipPercent;
            RearRightSlipPercent = rearRightSlipPercent;
        }

        public bool GripLost { get; private set; }
        public bool WarningBlink { get; private set; }
        public double SlipLevel { get; private set; }
        public bool Active { get; private set; }
        public bool WheelSpin { get; private set; }
        public bool BrakeLock { get; private set; }
        public bool SideSlip { get; private set; }
        public string WarningReason { get; private set; }
        public int TypeCode { get; private set; }
        public bool DataAvailable { get; private set; }
        public double WheelDifferenceKmh { get; private set; }
        public double SpinDifferenceKmh { get; private set; }
        public double LockDifferenceKmh { get; private set; }
        public double SideSlipRatio { get; private set; }
        public double SlipAngleDegrees { get; private set; }
        public double FrontLeftSlipPercent { get; private set; }
        public double FrontRightSlipPercent { get; private set; }
        public double RearLeftSlipPercent { get; private set; }
        public double RearRightSlipPercent { get; private set; }
    }

    internal sealed class GripDetector
    {
        private const double WarningHoldSeconds = 4.0;
        private const double BlinkOnSeconds = 0.55;
        private const double BlinkOffSeconds = 0.35;
        private const double ReleaseDelaySeconds = 0.15;
        private const double MaximumUpdateGapSeconds = 0.50;

        private readonly GripFrame[] history = new GripFrame[3];
        private int historyCount;
        private int historyIndex;
        private bool wheelDataPrimed;

        private double lastUpdateSeconds = double.NaN;
        private bool spinActive;
        private bool lockActive;
        private bool sideActive;
        private double spinReleaseTime;
        private double lockReleaseTime;
        private double sideReleaseTime;
        private double lowSpeedTime;
        private double filteredSeverity;
        private double warningUntilSeconds = double.NegativeInfinity;
        private double blinkStartedSeconds;
        private string lastWarningReason = string.Empty;
        private int lastWarningTypeCode;

        public void Reset()
        {
            historyCount = 0;
            historyIndex = 0;
            wheelDataPrimed = false;
            lastUpdateSeconds = double.NaN;
            spinActive = false;
            lockActive = false;
            sideActive = false;
            spinReleaseTime = 0.0;
            lockReleaseTime = 0.0;
            sideReleaseTime = 0.0;
            lowSpeedTime = 0.0;
            filteredSeverity = 0.0;
            warningUntilSeconds = double.NegativeInfinity;
            blinkStartedSeconds = 0.0;
            lastWarningReason = string.Empty;
            lastWarningTypeCode = 0;
        }

        public GripSnapshot Update(GripFrame rawFrame, double nowSeconds)
        {
            if (!IsFinite(nowSeconds) || !IsTelemetryValue(Math.Abs(rawFrame.SpeedKmh), 800.0))
            {
                Reset();
                return GripSnapshot.Unavailable;
            }

            if (IsFinite(lastUpdateSeconds) &&
                (nowSeconds < lastUpdateSeconds || nowSeconds - lastUpdateSeconds > MaximumUpdateGapSeconds))
            {
                Reset();
            }

            double deltaTime = GetDeltaTime(nowSeconds);
            AddToHistory(rawFrame);
            GripFrame frame = GetMedianFrame();

            double speedKmh = Math.Abs(frame.SpeedKmh);
            double frontLeft = Math.Abs(frame.FrontLeftWheelSpeed);
            double frontRight = Math.Abs(frame.FrontRightWheelSpeed);
            double rearLeft = Math.Abs(frame.RearLeftWheelSpeed);
            double rearRight = Math.Abs(frame.RearRightWheelSpeed);

            bool wheelsFinite =
                IsTelemetryValue(frontLeft, 800.0) &&
                IsTelemetryValue(frontRight, 800.0) &&
                IsTelemetryValue(rearLeft, 800.0) &&
                IsTelemetryValue(rearRight, 800.0);

            if (wheelsFinite && speedKmh > 10.0 &&
                frontLeft > 1.0 && frontRight > 1.0 && rearLeft > 1.0 && rearRight > 1.0)
            {
                wheelDataPrimed = true;
            }

            double maximumWheelSpeed = wheelsFinite
                ? Math.Max(Math.Max(frontLeft, frontRight), Math.Max(rearLeft, rearRight))
                : 0.0;
            bool allWheelsMissingWhileMoving = speedKmh > 20.0 && maximumWheelSpeed <= 1.0;
            bool wheelDataAvailable = wheelsFinite && wheelDataPrimed && !allWheelsMissingWhileMoving;

            bool velocityFinite =
                IsFinite(frame.ForwardVelocity) &&
                IsFinite(frame.LateralVelocity) &&
                Math.Abs(frame.ForwardVelocity) <= 250.0 &&
                Math.Abs(frame.LateralVelocity) <= 150.0;
            bool velocityMissingWhileMoving = speedKmh > 20.0 &&
                Math.Abs(frame.ForwardVelocity) < 0.05 &&
                Math.Abs(frame.LateralVelocity) < 0.05;
            bool velocityDataAvailable = velocityFinite && !velocityMissingWhileMoving;

            bool dataAvailable = wheelDataAvailable || velocityDataAvailable;
            if (!dataAvailable)
            {
                Reset();
                return GripSnapshot.Unavailable;
            }

            double denominator = Math.Max(speedKmh, 30.0);
            double minimumWheelSpeed = wheelsFinite
                ? Math.Min(Math.Min(frontLeft, frontRight), Math.Min(rearLeft, rearRight))
                : 0.0;
            double wheelDifferenceKmh = wheelDataAvailable ? maximumWheelSpeed - minimumWheelSpeed : 0.0;
            double spinDifferenceKmh = wheelDataAvailable ? Math.Max(0.0, maximumWheelSpeed - speedKmh) : 0.0;
            double lockDifferenceKmh = wheelDataAvailable ? Math.Max(0.0, speedKmh - minimumWheelSpeed) : 0.0;
            double spinRatio = spinDifferenceKmh / denominator;
            double lockRatio = lockDifferenceKmh / denominator;
            bool spreadEvidence = wheelDifferenceKmh >= Math.Max(12.0, speedKmh * 0.07);

            double throttlePercent = NormalizePedal(frame.Throttle);
            double brakePercent = NormalizePedal(frame.Brake);
            bool enoughHistory = historyCount >= history.Length;

            bool softSpin =
                enoughHistory &&
                wheelDataAvailable &&
                speedKmh >= 12.0 &&
                throttlePercent >= 12.0 &&
                spinDifferenceKmh >= 8.0 &&
                spinRatio >= (spreadEvidence ? 0.08 : 0.12);
            bool softLock =
                enoughHistory &&
                wheelDataAvailable &&
                speedKmh >= 20.0 &&
                brakePercent >= 10.0 &&
                lockDifferenceKmh >= 8.0 &&
                lockRatio >= (spreadEvidence ? 0.10 : 0.14);

            double sideSlipRatio = 0.0;
            if (velocityDataAvailable)
            {
                sideSlipRatio = Math.Abs(frame.LateralVelocity) /
                    Math.Max(Math.Abs(frame.ForwardVelocity), 5.0);
            }
            bool softSide =
                enoughHistory &&
                velocityDataAvailable &&
                speedKmh >= 20.0 &&
                sideSlipRatio >= 0.045;

            RawSignals rawSignals = GetRawSignals(rawFrame, wheelDataPrimed);

            if (!wheelDataAvailable)
            {
                spinActive = false;
                lockActive = false;
                spinReleaseTime = 0.0;
                lockReleaseTime = 0.0;
            }
            else
            {
                UpdateActiveState(
                    softSpin || rawSignals.HardSpin,
                    spinRatio < 0.055 || throttlePercent < 6.0 || speedKmh < 8.0,
                    deltaTime,
                    ref spinActive,
                    ref spinReleaseTime);
                UpdateActiveState(
                    softLock || rawSignals.HardLock,
                    lockRatio < 0.065 || brakePercent < 5.0 || speedKmh < 15.0,
                    deltaTime,
                    ref lockActive,
                    ref lockReleaseTime);
            }

            if (!velocityDataAvailable)
            {
                sideActive = false;
                sideReleaseTime = 0.0;
            }
            else
            {
                UpdateActiveState(
                    softSide || rawSignals.HardSide,
                    sideSlipRatio < 0.030 || speedKmh < 15.0,
                    deltaTime,
                    ref sideActive,
                    ref sideReleaseTime);
            }

            if (speedKmh < 5.0)
            {
                lowSpeedTime += deltaTime;
            }
            else
            {
                lowSpeedTime = 0.0;
            }

            if (lowSpeedTime >= 0.50)
            {
                spinActive = false;
                lockActive = false;
                sideActive = false;
                spinReleaseTime = 0.0;
                lockReleaseTime = 0.0;
                sideReleaseTime = 0.0;
                warningUntilSeconds = double.NegativeInfinity;
                lastWarningReason = string.Empty;
                lastWarningTypeCode = 0;
            }

            bool active = spinActive || lockActive || sideActive;
            bool warningWasActive = nowSeconds < warningUntilSeconds;
            if (active)
            {
                warningUntilSeconds = nowSeconds + WarningHoldSeconds;
                lastWarningReason = BuildReason(spinActive, lockActive, sideActive);
                lastWarningTypeCode = GetTypeCode(spinActive, lockActive, sideActive);
                if (!warningWasActive)
                {
                    blinkStartedSeconds = nowSeconds;
                }
            }

            bool warningActive = nowSeconds < warningUntilSeconds;
            if (!warningActive)
            {
                lastWarningReason = string.Empty;
                lastWarningTypeCode = 0;
            }

            double blinkCycle = BlinkOnSeconds + BlinkOffSeconds;
            double blinkPosition = PositiveModulo(nowSeconds - blinkStartedSeconds, blinkCycle);
            bool warningBlink = warningActive && blinkPosition < BlinkOnSeconds;

            double spinSeverity = wheelDataAvailable && speedKmh >= 12.0 && throttlePercent >= 6.0
                ? ScaleToPercent(spinRatio, 0.055, 0.25)
                : 0.0;
            double lockSeverity = wheelDataAvailable && speedKmh >= 15.0 && brakePercent >= 5.0
                ? ScaleToPercent(lockRatio, 0.065, 0.35)
                : 0.0;
            double sideSeverity = velocityDataAvailable && speedKmh >= 15.0
                ? ScaleToPercent(sideSlipRatio, 0.030, 0.13)
                : 0.0;
            double targetSeverity = Math.Max(spinSeverity, Math.Max(lockSeverity, sideSeverity));
            double severityTimeConstant = targetSeverity > filteredSeverity ? 0.05 : 0.30;
            double severityWeight = 1.0 - Math.Exp(-deltaTime / severityTimeConstant);
            filteredSeverity += (targetSeverity - filteredSeverity) * severityWeight;
            double displayedSeverity = warningActive ? Math.Max(25.0, filteredSeverity) : filteredSeverity;

            double slipAngleDegrees = Math.Atan(sideSlipRatio) * (180.0 / Math.PI);
            double frontLeftSlipPercent = wheelDataAvailable ? ((frontLeft - speedKmh) / denominator) * 100.0 : 0.0;
            double frontRightSlipPercent = wheelDataAvailable ? ((frontRight - speedKmh) / denominator) * 100.0 : 0.0;
            double rearLeftSlipPercent = wheelDataAvailable ? ((rearLeft - speedKmh) / denominator) * 100.0 : 0.0;
            double rearRightSlipPercent = wheelDataAvailable ? ((rearRight - speedKmh) / denominator) * 100.0 : 0.0;

            return new GripSnapshot(
                warningActive,
                warningBlink,
                Math.Round(Clamp(displayedSeverity, 0.0, 100.0), 1),
                active,
                spinActive,
                lockActive,
                sideActive,
                lastWarningReason,
                lastWarningTypeCode,
                true,
                Math.Round(wheelDifferenceKmh, 2),
                Math.Round(spinDifferenceKmh, 2),
                Math.Round(lockDifferenceKmh, 2),
                Math.Round(sideSlipRatio, 4),
                Math.Round(slipAngleDegrees, 2),
                Math.Round(frontLeftSlipPercent, 2),
                Math.Round(frontRightSlipPercent, 2),
                Math.Round(rearLeftSlipPercent, 2),
                Math.Round(rearRightSlipPercent, 2));
        }

        private RawSignals GetRawSignals(GripFrame frame, bool wheelsPrimed)
        {
            double speedKmh = Math.Abs(frame.SpeedKmh);
            double frontLeft = Math.Abs(frame.FrontLeftWheelSpeed);
            double frontRight = Math.Abs(frame.FrontRightWheelSpeed);
            double rearLeft = Math.Abs(frame.RearLeftWheelSpeed);
            double rearRight = Math.Abs(frame.RearRightWheelSpeed);
            bool wheelsValid =
                wheelsPrimed &&
                IsTelemetryValue(frontLeft, 800.0) &&
                IsTelemetryValue(frontRight, 800.0) &&
                IsTelemetryValue(rearLeft, 800.0) &&
                IsTelemetryValue(rearRight, 800.0) &&
                !(speedKmh > 20.0 &&
                  Math.Max(Math.Max(frontLeft, frontRight), Math.Max(rearLeft, rearRight)) <= 1.0);

            double denominator = Math.Max(speedKmh, 30.0);
            double maximumWheelSpeed = wheelsValid
                ? Math.Max(Math.Max(frontLeft, frontRight), Math.Max(rearLeft, rearRight))
                : speedKmh;
            double minimumWheelSpeed = wheelsValid
                ? Math.Min(Math.Min(frontLeft, frontRight), Math.Min(rearLeft, rearRight))
                : speedKmh;
            double spinDelta = Math.Max(0.0, maximumWheelSpeed - speedKmh);
            double lockDelta = Math.Max(0.0, speedKmh - minimumWheelSpeed);
            double throttlePercent = NormalizePedal(frame.Throttle);
            double brakePercent = NormalizePedal(frame.Brake);

            bool velocityValid =
                IsFinite(frame.ForwardVelocity) &&
                IsFinite(frame.LateralVelocity) &&
                Math.Abs(frame.ForwardVelocity) <= 250.0 &&
                Math.Abs(frame.LateralVelocity) <= 150.0 &&
                !(speedKmh > 20.0 &&
                  Math.Abs(frame.ForwardVelocity) < 0.05 &&
                  Math.Abs(frame.LateralVelocity) < 0.05);
            double sideRatio = velocityValid
                ? Math.Abs(frame.LateralVelocity) / Math.Max(Math.Abs(frame.ForwardVelocity), 5.0)
                : 0.0;

            return new RawSignals(
                wheelsValid &&
                speedKmh >= 12.0 &&
                throttlePercent >= 12.0 &&
                spinDelta >= 15.0 &&
                spinDelta / denominator >= 0.18,
                wheelsValid &&
                speedKmh >= 20.0 &&
                brakePercent >= 10.0 &&
                lockDelta >= 15.0 &&
                lockDelta / denominator >= 0.22,
                velocityValid &&
                speedKmh >= 20.0 &&
                sideRatio >= 0.075);
        }

        private void AddToHistory(GripFrame frame)
        {
            history[historyIndex] = frame;
            historyIndex = (historyIndex + 1) % history.Length;
            if (historyCount < history.Length)
            {
                historyCount++;
            }
        }

        private GripFrame GetMedianFrame()
        {
            return new GripFrame(
                MedianHistory(history[0].SpeedKmh, history[1].SpeedKmh, history[2].SpeedKmh),
                MedianHistory(history[0].FrontLeftWheelSpeed, history[1].FrontLeftWheelSpeed, history[2].FrontLeftWheelSpeed),
                MedianHistory(history[0].FrontRightWheelSpeed, history[1].FrontRightWheelSpeed, history[2].FrontRightWheelSpeed),
                MedianHistory(history[0].RearLeftWheelSpeed, history[1].RearLeftWheelSpeed, history[2].RearLeftWheelSpeed),
                MedianHistory(history[0].RearRightWheelSpeed, history[1].RearRightWheelSpeed, history[2].RearRightWheelSpeed),
                MedianHistory(history[0].ForwardVelocity, history[1].ForwardVelocity, history[2].ForwardVelocity),
                MedianHistory(history[0].LateralVelocity, history[1].LateralVelocity, history[2].LateralVelocity),
                MedianHistory(history[0].Throttle, history[1].Throttle, history[2].Throttle),
                MedianHistory(history[0].Brake, history[1].Brake, history[2].Brake));
        }

        private double MedianHistory(double first, double second, double third)
        {
            if (historyCount <= 1)
            {
                return first;
            }

            if (historyCount == 2)
            {
                return (first + second) * 0.5;
            }

            return first + second + third -
                Math.Min(first, Math.Min(second, third)) -
                Math.Max(first, Math.Max(second, third));
        }

        private double GetDeltaTime(double nowSeconds)
        {
            double deltaTime = IsFinite(lastUpdateSeconds) ? nowSeconds - lastUpdateSeconds : 1.0 / 60.0;
            lastUpdateSeconds = nowSeconds;
            return Clamp(deltaTime, 0.001, 0.100);
        }

        private static void UpdateActiveState(
            bool activate,
            bool release,
            double deltaTime,
            ref bool active,
            ref double releaseTime)
        {
            if (activate)
            {
                active = true;
                releaseTime = 0.0;
                return;
            }

            if (!active)
            {
                releaseTime = 0.0;
                return;
            }

            if (release)
            {
                releaseTime += deltaTime;
                if (releaseTime >= ReleaseDelaySeconds)
                {
                    active = false;
                    releaseTime = 0.0;
                }
            }
            else
            {
                releaseTime = 0.0;
            }
        }

        private static int GetTypeCode(bool wheelSpin, bool brakeLock, bool sideSlip)
        {
            int count = (wheelSpin ? 1 : 0) + (brakeLock ? 1 : 0) + (sideSlip ? 1 : 0);
            if (count > 1)
            {
                return 4;
            }

            if (wheelSpin)
            {
                return 1;
            }

            if (brakeLock)
            {
                return 2;
            }

            return sideSlip ? 3 : 0;
        }

        private static string BuildReason(bool wheelSpin, bool brakeLock, bool sideSlip)
        {
            string result = string.Empty;
            if (wheelSpin)
            {
                result = "Wheel spin";
            }

            if (brakeLock)
            {
                result = AppendReason(result, "Brake lock");
            }

            if (sideSlip)
            {
                result = AppendReason(result, "Side slip");
            }

            return result;
        }

        private static string AppendReason(string existing, string reason)
        {
            return existing.Length == 0 ? reason : existing + " + " + reason;
        }

        private static double NormalizePedal(double value)
        {
            if (!IsFinite(value))
            {
                return 0.0;
            }

            return Clamp(Math.Abs(value), 0.0, 100.0);
        }

        private static double ScaleToPercent(double value, double zeroPoint, double fullPoint)
        {
            return Clamp((value - zeroPoint) / (fullPoint - zeroPoint), 0.0, 1.0) * 100.0;
        }

        private static bool IsTelemetryValue(double value, double maximum)
        {
            return IsFinite(value) && value >= 0.0 && value <= maximum;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double result = value % modulus;
            return result < 0.0 ? result + modulus : result;
        }

        private struct RawSignals
        {
            public RawSignals(bool hardSpin, bool hardLock, bool hardSide)
            {
                HardSpin = hardSpin;
                HardLock = hardLock;
                HardSide = hardSide;
            }

            public bool HardSpin { get; private set; }
            public bool HardLock { get; private set; }
            public bool HardSide { get; private set; }
        }
    }
}
