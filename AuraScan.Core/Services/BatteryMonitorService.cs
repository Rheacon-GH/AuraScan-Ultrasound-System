using System.Runtime.InteropServices;

namespace AuraScan_Ultrasound_System.Services
{
    public enum PowerSource
    {
        AC,
        Battery,
        Unknown
    }

    public enum BatteryChargeStatus
    {
        Discharging,
        Charging,
        Full,
        NoBattery,
        Unknown
    }

    public class BatteryStatus
    {
        public PowerSource PowerSource { get; set; } = PowerSource.Unknown;
        public BatteryChargeStatus ChargeStatus { get; set; } = BatteryChargeStatus.Unknown;
        public int PercentRemaining { get; set; } = 100;
        public TimeSpan? EstimatedRuntime { get; set; }
        public bool HasBattery { get; set; }
        public bool IsOnBattery => PowerSource == PowerSource.Battery;
    }

    public class BatteryMonitorService : IDisposable
    {
        // Win32 SYSTEM_POWER_STATUS
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;        // 0=Offline, 1=Online, 255=Unknown
            public byte BatteryFlag;         // 1=High, 2=Low, 4=Critical, 8=Charging, 128=NoBattery, 255=Unknown
            public byte BatteryLifePercent;  // 0-100 or 255=Unknown
            public byte SystemStatusFlag;
            public int BatteryLifeTime;      // seconds remaining, -1=Unknown
            public int BatteryFullLifeTime;  // seconds at full charge, -1=Unknown
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private readonly System.Timers.Timer _pollTimer;
        private bool _disposed;

        public BatteryStatus CurrentStatus { get; private set; } = new();
        public event EventHandler<BatteryStatus>? StatusChanged;

        public BatteryMonitorService(int pollIntervalSeconds = 30)
        {
            // Initial read
            Refresh();

            _pollTimer = new System.Timers.Timer(pollIntervalSeconds * 1000);
            _pollTimer.Elapsed += (_, _) => Refresh();
            _pollTimer.AutoReset = true;
            _pollTimer.Start();
        }

        public void Refresh()
        {
            var status = new BatteryStatus();

            try
            {
                if (GetSystemPowerStatus(out var ps))
                {
                    // Power source
                    status.PowerSource = ps.ACLineStatus switch
                    {
                        0 => PowerSource.Battery,
                        1 => PowerSource.AC,
                        _ => PowerSource.Unknown
                    };

                    // Battery presence
                    status.HasBattery = (ps.BatteryFlag & 128) == 0 && ps.BatteryFlag != 255;

                    // Charge status
                    if (!status.HasBattery)
                    {
                        status.ChargeStatus = BatteryChargeStatus.NoBattery;
                    }
                    else if ((ps.BatteryFlag & 8) != 0)
                    {
                        status.ChargeStatus = BatteryChargeStatus.Charging;
                    }
                    else if (ps.ACLineStatus == 1 && ps.BatteryLifePercent >= 95)
                    {
                        status.ChargeStatus = BatteryChargeStatus.Full;
                    }
                    else
                    {
                        status.ChargeStatus = BatteryChargeStatus.Discharging;
                    }

                    // Percentage
                    status.PercentRemaining = ps.BatteryLifePercent is >= 0 and <= 100
                        ? ps.BatteryLifePercent
                        : 0;

                    // Estimated runtime
                    if (ps.BatteryLifeTime > 0)
                    {
                        status.EstimatedRuntime = TimeSpan.FromSeconds(ps.BatteryLifeTime);
                    }
                }
            }
            catch
            {
                status.PowerSource = PowerSource.Unknown;
                status.HasBattery = false;
            }

            CurrentStatus = status;
            StatusChanged?.Invoke(this, status);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer.Stop();
            _pollTimer.Dispose();
        }
    }
}
