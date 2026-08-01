using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Media;

namespace GothiaGripPlugin
{
    [PluginDescription("Detects wheel spin, brake lock and sideways grip loss, with a dashboard-ready warning blink.")]
    [PluginAuthor("Gothia Racing Performance")]
    [PluginName("Gothia Grip Monitor")]
    public sealed class GothiaGrip : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string PluginVersion = "1.2.1";
        private const double TestWarningSeconds = 5.0;
        private const double TestBlinkOnSeconds = 0.55;
        private const double TestBlinkOffSeconds = 0.35;

        private readonly GripDetector detector = new GripDetector();
        private readonly TemperatureWarningDetector temperatureDetector = new TemperatureWarningDetector();
        private GripSnapshot snapshot = GripSnapshot.Unavailable;
        private TemperatureSnapshot temperatureSnapshot = TemperatureSnapshot.Unavailable;
        private bool errorLogged;
        private bool sessionKnown;
        private Guid currentSessionId;
        private long testWarningStartedTicks;
        private long testWarningUntilTicks;
        private long temperatureTestStartedTicks;
        private long temperatureTestUntilTicks;

        public PluginManager PluginManager { get; set; }
        public string LeftMenuTitle { get { return "Gothia Grip"; } }
        public ImageSource PictureIcon { get { return GothiaGripSettingsControl.CreateMenuIcon(); } }

        private GripSnapshot CurrentSnapshot
        {
            get { return Volatile.Read(ref snapshot); }
        }

        private TemperatureSnapshot CurrentTemperatureSnapshot
        {
            get { return Volatile.Read(ref temperatureSnapshot); }
        }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            detector.Reset();
            temperatureDetector.Reset();
            Volatile.Write(ref snapshot, GripSnapshot.Unavailable);
            Volatile.Write(ref temperatureSnapshot, TemperatureSnapshot.Unavailable);
            sessionKnown = false;
            currentSessionId = Guid.Empty;
            Interlocked.Exchange(ref testWarningStartedTicks, 0L);
            Interlocked.Exchange(ref testWarningUntilTicks, 0L);
            Interlocked.Exchange(ref temperatureTestStartedTicks, 0L);
            Interlocked.Exchange(ref temperatureTestUntilTicks, 0L);

            this.AttachDelegate("GripLost", () => CurrentSnapshot.GripLost || IsTestWarningActive());
            this.AttachDelegate("WarningBlink", GetWarningBlink);
            this.AttachDelegate("SlipLevel", () => IsTestWarningActive() ? 100.0 : CurrentSnapshot.SlipLevel);
            this.AttachDelegate("Active", () => CurrentSnapshot.Active);
            this.AttachDelegate("WheelSpin", () => CurrentSnapshot.WheelSpin);
            this.AttachDelegate("BrakeLock", () => CurrentSnapshot.BrakeLock);
            this.AttachDelegate("SideSlip", () => CurrentSnapshot.SideSlip);
            this.AttachDelegate("WarningReason", () => IsTestWarningActive() ? "Test" : CurrentSnapshot.WarningReason);
            this.AttachDelegate("TypeCode", () => CurrentSnapshot.TypeCode);
            this.AttachDelegate("DataAvailable", () => CurrentSnapshot.DataAvailable);
            this.AttachDelegate("TestActive", IsTestWarningActive);

            // Diagnostic values make threshold tuning possible without changing the dashboard.
            this.AttachDelegate("WheelDifferenceKmh", () => CurrentSnapshot.WheelDifferenceKmh);
            this.AttachDelegate("SpinDifferenceKmh", () => CurrentSnapshot.SpinDifferenceKmh);
            this.AttachDelegate("LockDifferenceKmh", () => CurrentSnapshot.LockDifferenceKmh);
            this.AttachDelegate("SideSlipRatio", () => CurrentSnapshot.SideSlipRatio);
            this.AttachDelegate("SlipAngleDegrees", () => CurrentSnapshot.SlipAngleDegrees);
            this.AttachDelegate("FrontLeftSlipPercent", () => CurrentSnapshot.FrontLeftSlipPercent);
            this.AttachDelegate("FrontRightSlipPercent", () => CurrentSnapshot.FrontRightSlipPercent);
            this.AttachDelegate("RearLeftSlipPercent", () => CurrentSnapshot.RearLeftSlipPercent);
            this.AttachDelegate("RearRightSlipPercent", () => CurrentSnapshot.RearRightSlipPercent);

            this.AttachDelegate("OilTemperatureC", () => CurrentTemperatureSnapshot.OilTemperatureC);
            this.AttachDelegate("WaterTemperatureC", () => CurrentTemperatureSnapshot.WaterTemperatureC);
            this.AttachDelegate("TemperatureDataAvailable", () => CurrentTemperatureSnapshot.DataAvailable);
            this.AttachDelegate("OilTemperatureWarning", () => CurrentTemperatureSnapshot.OilWarning);
            this.AttachDelegate("WaterTemperatureWarning", () => CurrentTemperatureSnapshot.WaterWarning);
            this.AttachDelegate(
                "TemperatureWarning",
                () => CurrentTemperatureSnapshot.WarningActive || IsTemperatureTestActive());
            this.AttachDelegate("TemperatureWarningBlink", GetTemperatureWarningBlink);
            this.AttachDelegate(
                "TemperatureWarningReason",
                () => IsTemperatureTestActive() ? "Test" : CurrentTemperatureSnapshot.WarningReason);
            this.AttachDelegate(
                "OilWarningThresholdC",
                () => TemperatureWarningDetector.OilWarningThresholdC);
            this.AttachDelegate(
                "WaterWarningThresholdC",
                () => TemperatureWarningDetector.WaterWarningThresholdC);
            this.AttachDelegate("TemperatureTestActive", IsTemperatureTestActive);
            this.AttachDelegate("Version", () => PluginVersion);

            SimHub.Logging.Current.Info("Gothia Grip Monitor " + PluginVersion + " started.");
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new GothiaGripSettingsControl(this);
        }

        public void StartTestWarning()
        {
            long nowTicks = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref testWarningStartedTicks, nowTicks);
            Interlocked.Exchange(
                ref testWarningUntilTicks,
                nowTicks + (long)(TestWarningSeconds * Stopwatch.Frequency));
        }

        public void StartTemperatureTestWarning()
        {
            long nowTicks = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref temperatureTestStartedTicks, nowTicks);
            Interlocked.Exchange(
                ref temperatureTestUntilTicks,
                nowTicks + (long)(TestWarningSeconds * Stopwatch.Frequency));
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                if (!data.GameRunning ||
                    data.GamePaused ||
                    data.GameInMenu ||
                    data.GameReplay ||
                    data.NewData == null ||
                    data.NewData.IsGameReplay ||
                    data.NewData.Spectating)
                {
                    detector.Reset();
                    temperatureDetector.Reset();
                    Volatile.Write(ref snapshot, GripSnapshot.Unavailable);
                    Volatile.Write(ref temperatureSnapshot, TemperatureSnapshot.Unavailable);
                    return;
                }

                if (!sessionKnown || data.SessionId != currentSessionId)
                {
                    detector.Reset();
                    temperatureDetector.Reset();
                    currentSessionId = data.SessionId;
                    sessionKnown = true;
                }

                double nowSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                TemperatureSnapshot nextTemperature = temperatureDetector.Update(
                    data.NewData.OilTemperature,
                    data.NewData.WaterTemperature,
                    nowSeconds);
                Volatile.Write(ref temperatureSnapshot, nextTemperature);

                GameReaderCommon.Feedback.FeedbackData feedback = data.NewData.FeedbackData;
                if (feedback == null)
                {
                    detector.Reset();
                    Volatile.Write(ref snapshot, GripSnapshot.Unavailable);
                    return;
                }

                GameReaderCommon.Feedback.LocalVelocity localVelocity = feedback.LocalVelocity;
                double forward = localVelocity == null ? double.NaN : localVelocity.Forward;
                double lateral = localVelocity == null ? double.NaN : localVelocity.Lateral;

                GripFrame frame = new GripFrame(
                    data.NewData.SpeedKmh,
                    feedback.FrontLeftWheelSpeed,
                    feedback.FrontRightWheelSpeed,
                    feedback.RearLeftWheelSpeed,
                    feedback.RearRightWheelSpeed,
                    forward,
                    lateral,
                    data.NewData.Throttle,
                    data.NewData.Brake);

                GripSnapshot next = detector.Update(frame, nowSeconds);
                Volatile.Write(ref snapshot, next);
                errorLogged = false;
            }
            catch (Exception exception)
            {
                detector.Reset();
                temperatureDetector.Reset();
                Volatile.Write(ref snapshot, GripSnapshot.Unavailable);
                Volatile.Write(ref temperatureSnapshot, TemperatureSnapshot.Unavailable);

                if (!errorLogged)
                {
                    SimHub.Logging.Current.Error("Gothia Grip Monitor could not read the current telemetry frame.", exception);
                    errorLogged = true;
                }
            }
        }

        public void End(PluginManager pluginManager)
        {
            detector.Reset();
            temperatureDetector.Reset();
            Volatile.Write(ref snapshot, GripSnapshot.Unavailable);
            Volatile.Write(ref temperatureSnapshot, TemperatureSnapshot.Unavailable);
            sessionKnown = false;
            currentSessionId = Guid.Empty;
            Interlocked.Exchange(ref testWarningStartedTicks, 0L);
            Interlocked.Exchange(ref testWarningUntilTicks, 0L);
            Interlocked.Exchange(ref temperatureTestStartedTicks, 0L);
            Interlocked.Exchange(ref temperatureTestUntilTicks, 0L);
        }

        private bool IsTestWarningActive()
        {
            return Stopwatch.GetTimestamp() < Interlocked.Read(ref testWarningUntilTicks);
        }

        private bool GetWarningBlink()
        {
            long nowTicks = Stopwatch.GetTimestamp();
            long untilTicks = Interlocked.Read(ref testWarningUntilTicks);
            if (nowTicks < untilTicks)
            {
                long startedTicks = Interlocked.Read(ref testWarningStartedTicks);
                double elapsedSeconds = (nowTicks - startedTicks) / (double)Stopwatch.Frequency;
                double cycleSeconds = TestBlinkOnSeconds + TestBlinkOffSeconds;
                double cyclePosition = elapsedSeconds % cycleSeconds;
                return cyclePosition < TestBlinkOnSeconds;
            }

            return CurrentSnapshot.WarningBlink;
        }

        private bool IsTemperatureTestActive()
        {
            return Stopwatch.GetTimestamp() < Interlocked.Read(ref temperatureTestUntilTicks);
        }

        private bool GetTemperatureWarningBlink()
        {
            long nowTicks = Stopwatch.GetTimestamp();
            long untilTicks = Interlocked.Read(ref temperatureTestUntilTicks);
            if (nowTicks < untilTicks)
            {
                long startedTicks = Interlocked.Read(ref temperatureTestStartedTicks);
                double elapsedSeconds = (nowTicks - startedTicks) / (double)Stopwatch.Frequency;
                double cycleSeconds = TestBlinkOnSeconds + TestBlinkOffSeconds;
                double cyclePosition = elapsedSeconds % cycleSeconds;
                return cyclePosition < TestBlinkOnSeconds;
            }

            return CurrentTemperatureSnapshot.WarningBlink;
        }
    }
}
