# Gothia Grip Monitor

This SimHub data plugin publishes dashboard properties for wheel spin, brake lock
and lateral grip loss. It uses normalized telemetry already received by SimHub,
so it does not open a second GT7 UDP connection.

Dashboard properties:

- `GothiaGrip.WarningBlink`
- `GothiaGrip.GripLost`
- `GothiaGrip.SlipLevel`
- `GothiaGrip.Active`
- `GothiaGrip.WheelSpin`
- `GothiaGrip.BrakeLock`
- `GothiaGrip.SideSlip`
- `GothiaGrip.WarningReason`
- `GothiaGrip.TypeCode`
- `GothiaGrip.DataAvailable`

Diagnostics are also published for tuning:

- `GothiaGrip.WheelDifferenceKmh`
- `GothiaGrip.SpinDifferenceKmh`
- `GothiaGrip.LockDifferenceKmh`
- `GothiaGrip.SideSlipRatio`
- `GothiaGrip.SlipAngleDegrees`
- `GothiaGrip.FrontLeftSlipPercent`
- `GothiaGrip.FrontRightSlipPercent`
- `GothiaGrip.RearLeftSlipPercent`
- `GothiaGrip.RearRightSlipPercent`
- `GothiaGrip.OilTemperatureC`
- `GothiaGrip.WaterTemperatureC`
- `GothiaGrip.TemperatureDataAvailable`
- `GothiaGrip.OilTemperatureWarning`
- `GothiaGrip.WaterTemperatureWarning`
- `GothiaGrip.TemperatureWarning`
- `GothiaGrip.TemperatureWarningBlink`
- `GothiaGrip.TemperatureWarningReason`
- `GothiaGrip.OilWarningThresholdC`
- `GothiaGrip.WaterWarningThresholdC`
- `GothiaGrip.Version`

For a white base icon with a red warning icon above it, keep the white image
visible. Bind the red image's visibility directly to
`GothiaGrip.WarningBlink`. Do not also enable the image's built-in blink option.

The detector recognizes longitudinal wheel spin, wheel lock under braking and
vehicle sideslip. With the verified GT7 fields it cannot reliably distinguish
understeer from oversteer.

The temperature warning activates at 125 °C oil temperature or 105 °C water
temperature, with hysteresis to prevent flickering around the threshold. In the
verified GT7 feed oil temperature is available while water temperature is zero.
