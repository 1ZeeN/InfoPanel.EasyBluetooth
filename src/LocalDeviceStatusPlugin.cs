using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoPanel.Plugins;

namespace InfoPanel.EasyBT;

public class EasyBTPlugin : BasePlugin
{
    private readonly HttpClient _http = new();
    private readonly List<DeviceEntries> _deviceEntries = new();
    private readonly object _logLock = new();

    private int _port = 18080;
    private int _updateIntervalSeconds = 2;
    private int _maxDevices = 10;

    private LogLevel _logLevel = LogLevel.Info;
    private bool _detailedDebug = false;
    private int _maxLogSizeKb = 1024;

    public EasyBTPlugin()
        : base("easybt", "InfoPanel Easy Bluetooth", "Open Config Files to change PORT and Max Devices.")
    {
    }

    [Obsolete]
    public override string ConfigFilePath => GetConfigFilePath();

    public override TimeSpan UpdateInterval =>
        TimeSpan.FromSeconds(_updateIntervalSeconds);

    public override void Initialize()
    {
        EnsureConfigExists();
        LoadConfig();
        RotateLogIfNeeded();

        Log(LogLevel.Info, "Plugin initialized");
        Log(LogLevel.Info, $"ApiUrl={GetApiUrl()}, Interval={_updateIntervalSeconds}, MaxDevices={_maxDevices}");
        Log(LogLevel.Debug, $"LogLevel={_logLevel}, DetailedDebug={_detailedDebug}, MaxLogSizeKb={_maxLogSizeKb}");
    }

    public override void Load(List<IPluginContainer> containers)
    {
        _deviceEntries.Clear();

        for (var i = 0; i < _maxDevices; i++)
        {
            var entries = new DeviceEntries(i);
            _deviceEntries.Add(entries);

            var container = new PluginContainer($"device_{i}", $"Device {i + 1}");

            container.Entries.Add(entries.Title);
            container.Entries.Add(entries.Brand);
            container.Entries.Add(entries.Type);
            container.Entries.Add(entries.Status);
            container.Entries.Add(entries.Connection);
            container.Entries.Add(entries.Battery);
            container.Entries.Add(entries.Charging);
            container.Entries.Add(entries.ChargingBinary);
            container.Entries.Add(entries.Sleeping);
            container.Entries.Add(entries.BatteryUpdatedAt);

            containers.Add(container);
        }

        Log(LogLevel.Info, $"Loaded {_deviceEntries.Count} device container(s)");
    }

    public override async Task UpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_detailedDebug)
                Log(LogLevel.Debug, $"Requesting {GetApiUrl()}");

            var json = await _http.GetStringAsync(GetApiUrl(), cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = root.GetProperty("data");
            var devices = data.GetProperty("devices");

            var count = Math.Min(devices.GetArrayLength(), _deviceEntries.Count);

            Log(LogLevel.Debug, $"Devices from API={devices.GetArrayLength()}, displayed={count}");

            for (var i = 0; i < _deviceEntries.Count; i++)
            {
                if (i >= count)
                {
                    _deviceEntries[i].Clear();
                    continue;
                }

                _deviceEntries[i].Update(
                    devices[i],
                    _detailedDebug ? msg => Log(LogLevel.Debug, msg) : null
                );
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Update failed: {ex}");

            foreach (var d in _deviceEntries)
                d.Clear();
        }
    }

    public override void Update() { }

    public override void Close()
    {
        Log(LogLevel.Info, "Plugin closing");
        _http.Dispose();
    }

    // ================= CONFIG =================

    private string GetApiUrl()
    {
        return $"http://localhost:{_port}/api/v1/status";
    }

    private string GetConfigFilePath()
    {
        var dllPath = GetType().Assembly.Location
            ?? Assembly.GetExecutingAssembly().Location
            ?? Path.Combine(AppContext.BaseDirectory, "InfoPanel.EasyBT.dll");

        var dir = Path.GetDirectoryName(dllPath) ?? AppContext.BaseDirectory;
        var dllName = Path.GetFileNameWithoutExtension(dllPath);

        return Path.Combine(dir, $"{dllName}.ini");
    }

    private string GetPluginDirectory()
    {
        var dllPath = GetType().Assembly.Location
            ?? Assembly.GetExecutingAssembly().Location
            ?? AppContext.BaseDirectory;

        return Path.GetDirectoryName(dllPath) ?? AppContext.BaseDirectory;
    }

    private string GetLogFilePath()
    {
        var dir = GetPluginDirectory();
        var dllName = Path.GetFileNameWithoutExtension(GetType().Assembly.Location);

        if (string.IsNullOrWhiteSpace(dllName))
            dllName = "InfoPanel.EasyBT";

        return Path.Combine(dir, $"{dllName}.log");
    }

    private void EnsureConfigExists()
    {
        var path = GetConfigFilePath();
        if (File.Exists(path)) return;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var template = new[]
            {
                "[General]",
                "Port=18080",
                "UpdateIntervalSeconds=2",
                "MaxDevices=10",
                "",
                "[Logging]",
                "# Options: Debug, Info, Error, Off",
                "LogLevel=Info",
                "DetailedDebug=false",
                "# Max size before rotation, in KB. 1024 = 1 MB",
                "MaxLogSizeKb=1024",
                ""
            };

            File.WriteAllText(path, string.Join(Environment.NewLine, template));
        }
        catch
        {
        }
    }

    private void LoadConfig()
    {
        var path = GetConfigFilePath();
        if (!File.Exists(path)) return;

        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("["))
                    continue;

                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();

                if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value, out var port))
                        _port = port;
                }
                else if (key.Equals("UpdateIntervalSeconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value, out var interval))
                        _updateIntervalSeconds = interval;
                }
                else if (key.Equals("MaxDevices", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value, out var maxDevices))
                        _maxDevices = maxDevices;
                }
                else if (key.Equals("LogLevel", StringComparison.OrdinalIgnoreCase))
                {
                    _logLevel = ParseLogLevel(value);
                }
                else if (key.Equals("DetailedDebug", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(value, out var detailedDebug))
                        _detailedDebug = detailedDebug;
                }
                else if (key.Equals("MaxLogSizeKb", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value, out var maxLogSizeKb))
                        _maxLogSizeKb = maxLogSizeKb;
                }
            }
        }
        catch
        {
        }

        _port = Math.Clamp(_port, 1, 65535);
        _updateIntervalSeconds = Math.Clamp(_updateIntervalSeconds, 1, 60);
        _maxDevices = Math.Clamp(_maxDevices, 1, 50);
        _maxLogSizeKb = Math.Clamp(_maxLogSizeKb, 64, 10240);
    }

    // ================= LOGGING =================

    private enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Error = 2,
        Off = 3
    }

    private static LogLevel ParseLogLevel(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Info,
            "error" => LogLevel.Error,
            "off" => LogLevel.Off,
            _ => LogLevel.Info
        };
    }

    private void Log(LogLevel level, string message)
    {
        if (_logLevel == LogLevel.Off)
            return;

        if (level < _logLevel)
            return;

        try
        {
            var path = GetLogFilePath();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level.ToString().ToUpperInvariant()}] {message}";

            lock (_logLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private void RotateLogIfNeeded()
    {
        try
        {
            var path = GetLogFilePath();
            if (!File.Exists(path)) return;

            var maxBytes = _maxLogSizeKb * 1024L;
            var file = new FileInfo(path);

            if (file.Length <= maxBytes)
                return;

            var dir = Path.GetDirectoryName(path) ?? GetPluginDirectory();
            var name = Path.GetFileNameWithoutExtension(path);
            var rotated = Path.Combine(dir, $"{name}.{DateTime.Now:yyyyMMdd_HHmmss}.log");

            File.Move(path, rotated, true);
        }
        catch
        {
        }
    }

    // ================= DEVICE =================

    private sealed class DeviceEntries
    {
        public PluginText Title { get; }
        public PluginText Brand { get; }
        public PluginText Type { get; }
        public PluginText Status { get; }
        public PluginText Connection { get; }
        public PluginSensor Battery { get; }
        public PluginText Charging { get; }
        public PluginSensor ChargingBinary { get; }
        public PluginText Sleeping { get; }
        public PluginText BatteryUpdatedAt { get; }

        public DeviceEntries(int index)
        {
            var p = $"device_{index}_";

            Title = new PluginText(p + "title", "Device", "-");
            Brand = new PluginText(p + "brand", "Brand", "-");
            Type = new PluginText(p + "type", "Type", "-");
            Status = new PluginText(p + "status", "Status", "-");
            Connection = new PluginText(p + "connection", "Connection", "-");
            Battery = new PluginSensor(p + "battery", "Battery", 0, "%");
            Charging = new PluginText(p + "charging", "Charging", "-");
            ChargingBinary = new PluginSensor(p + "charging_binary", "Charging State", 0, "");
            Sleeping = new PluginText(p + "sleeping", "Sleeping", "-");
            BatteryUpdatedAt = new PluginText(p + "updated", "Updated", "-");
        }

        public void Update(JsonElement d, Action<string>? debugLog)
        {
            var name = GetString(d, "renamedName");
            if (name == "-") name = GetString(d, "name");

            Title.Value = name;
            Brand.Value = (GetString(d, "source"));
            Type.Value = (GetString(d, "deviceType"));
            Status.Value = (GetString(d, "status"));
            Connection.Value = TranslateConnection(GetString(d, "connectionStatus"));

            if (d.TryGetProperty("battery", out var b) &&
                b.ValueKind != JsonValueKind.Null &&
                b.TryGetInt32(out var val))
            {
                Battery.Value = val;
            }
            else
            {
                Battery.Value = 0;
            }

            var isCharging = GetBool(d, "isCharging");
            Charging.Value = isCharging ? "Yes" : "No";
            ChargingBinary.Value = isCharging ? 1 : 0;
            Sleeping.Value = GetBoolText(d, "isSleeping");
            BatteryUpdatedAt.Value = FormatDate(GetString(d, "batteryLastUpdatedUtc"));

            debugLog?.Invoke(
                $"Device updated: Name={Title.Value}, Brand={Brand.Value}, Type={Type.Value}, Status={Status.Value}, Connection={Connection.Value}, Battery={Battery.Value}, Charging={ChargingBinary.Value}"
            );
        }

        public void Clear()
        {
            Title.Value = "-";
            Brand.Value = "-";
            Type.Value = "-";
            Status.Value = "-";
            Connection.Value = "-";
            Battery.Value = 0;
            Charging.Value = "-";
            ChargingBinary.Value = 0;
            Sleeping.Value = "-";
            BatteryUpdatedAt.Value = "-";
        }
    }

    // ================= HELPERS =================

    private static string GetString(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null
            ? p.ToString()
            : "-";
    }

    private static bool GetBool(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    }

    private static string GetBoolText(JsonElement e, string name)
    {
        return GetBool(e, name) ? "Yes" : "No";
    }

    private static string FormatDate(string v)
    {
        return DateTimeOffset.TryParse(v, out var d)
            ? d.ToLocalTime().ToString("HH:mm:ss")
            : "-";
    }

    private static string TranslateConnection(string v)
    {
        return v switch
        {
            "使用中" => "In use",
            "未使用" => "Not in use",
            "在线" => "Online",
            "CONNECTED" => "Connected",
            "DISCONNECTED" => "Disconnected",
            _ => v
        };
    }
}