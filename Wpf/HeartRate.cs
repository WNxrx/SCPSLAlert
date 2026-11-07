using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
#if BLE
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;
#endif

namespace SCPSLAlert
{
    internal sealed class HeartRateSample
    {
        public int Bpm;
        public int? RrMs;
        public DateTime At;
        public bool Contact;
        public string Source;
    }

    internal interface IHeartRateSource : IDisposable
    {
        string Name { get; }
        bool IsRunning { get; }
        event Action<HeartRateSample> Sample;
        event Action<string> StatusChanged;

        void Start();
        void Stop();
    }

    /// <summary>
    /// 全局心率出口。以后别的功能直接读 Bpm / 订阅 BpmChanged 就行，
    /// 不需要知道数据来自手环、模拟还是未来的其它来源。
    /// </summary>
    internal static class HeartRateHub
    {
        private static readonly object Gate = new object();
        private static int _bpm;
        private static DateTime _lastAt = DateTime.MinValue;
        private static bool _bleConnected;
        private static string _bleName = "";

        public static event Action<int> BpmChanged;
        public static event Action<bool> BleConnectionChanged;

        public static int Bpm
        {
            get
            {
                lock (Gate)
                {
                    if (!Fresh()) return 0;
                    return _bpm;
                }
            }
        }

        public static bool HasSignal
        {
            get
            {
                lock (Gate) { return Fresh(); }
            }
        }

        public static string SourceName
        {
            get { return HasSignal ? "BLE" : "SIM"; }
        }

        public static bool BleConnected
        {
            get { lock (Gate) { return _bleConnected; } }
        }

        public static string BleDeviceName
        {
            get { lock (Gate) { return _bleName; } }
        }

        private static bool Fresh()
        {
            return _bpm > 0 && _lastAt != DateTime.MinValue
                && (DateTime.UtcNow - _lastAt).TotalSeconds < 6.0;
        }

        public static void Publish(int bpm, int? rrMs, string source)
        {
            if (bpm <= 0) return;
            if (bpm < 25) bpm = 25;
            if (bpm > 230) bpm = 230;

            bool changed;
            lock (Gate)
            {
                changed = _bpm != bpm;
                _bpm = bpm;
                _lastAt = DateTime.UtcNow;
            }
            if (changed && BpmChanged != null)
            {
                try { BpmChanged(bpm); } catch { }
            }
        }

        public static void Clear()
        {
            int old;
            lock (Gate)
            {
                old = _bpm;
                _bpm = 0;
                _lastAt = DateTime.MinValue;
            }
            if (old != 0 && BpmChanged != null)
            {
                try { BpmChanged(0); } catch { }
            }
        }

        public static void SetBleConnected(bool connected, string name)
        {
            bool changed;
            lock (Gate)
            {
                changed = _bleConnected != connected || _bleName != (name ?? "");
                _bleConnected = connected;
                _bleName = name ?? "";
            }
            if (!connected) Clear();
            if (changed && BleConnectionChanged != null)
            {
                try { BleConnectionChanged(connected); } catch { }
            }
        }
    }

    /// <summary>扫描结果里的一个蓝牙心率设备。</summary>
    internal sealed class BleHeartDevice
    {
        public ulong Address;
        public string Name = "";
        public short Rssi;

        public string AddressText
        {
            get
            {
                string hex = Address.ToString("X12");
                return hex.Substring(0, 2) + ":" + hex.Substring(2, 2) + ":" + hex.Substring(4, 2) + ":"
                    + hex.Substring(6, 2) + ":" + hex.Substring(8, 2) + ":" + hex.Substring(10, 2);
            }
        }
    }

    /// <summary>
    /// 游戏角色生死状态。心跳曲线只负责画，报警音由窗口在 Died 时播放。
    /// </summary>
    internal static class HeartMonitor
    {
        public static bool GameRunning;
        public static bool Dead;
        public static bool DeathHadRealBpm;
        public static bool Alive { get { return !Dead; } }

        public static event Action<bool> Died;

        public static void Update(bool gameRunning, bool dead)
        {
            bool wasDead = Dead;
            bool nextDead = gameRunning && dead;
            GameRunning = gameRunning;
            Dead = nextDead;

            if (!wasDead && nextDead)
            {
                DeathHadRealBpm = HeartRateHub.HasSignal;
                if (Died != null)
                {
                    try { Died(DeathHadRealBpm); } catch { }
                }
            }
            else if (wasDead && !nextDead)
            {
                DeathHadRealBpm = false;
            }
        }
    }

#if BLE
    /// <summary>
    /// Windows 10/11 原生 BLE 心率读取。
    /// 只订阅标准 Heart Rate Service (0x180D) / Measurement (0x2A37)。
    /// </summary>
    internal sealed class BleHeartRateSource : IHeartRateSource
    {
        private readonly Dispatcher _ui;
        private readonly object _gate = new object();
        private BluetoothLEAdvertisementWatcher _watcher;
        private BluetoothLEDevice _device;
        private GattDeviceService _service;
        private GattCharacteristic _characteristic;
        private Timer _retry;
        private ulong _preferredAddress;
        private string _preferredName = "";
        private bool _enabled;
        private bool _scanning;
        private bool _connecting;
        private bool _connected;
        private string _status = "";

        public string Name { get { return "小米手环 / BLE"; } }
        public bool IsRunning { get { return _enabled; } }
        public string Status { get { return _status; } }
        public ulong ConnectedAddress
        {
            get { return _connected && _device != null ? _device.BluetoothAddress : 0; }
        }

        public event Action<HeartRateSample> Sample;
        public event Action<string> StatusChanged;
        public event Action<BleHeartDevice> DeviceFound;

        public BleHeartRateSource(Dispatcher ui, ulong preferredAddress, string preferredName)
        {
            _ui = ui;
            _preferredAddress = preferredAddress;
            _preferredName = preferredName ?? "";
        }

        public static void Probe(Action<bool> callback)
        {
            if (callback == null) return;
            if (Environment.OSVersion.Version.Major < 10)
            {
                callback(false);
                return;
            }
            try
            {
                IAsyncOperation<BluetoothAdapter> op = BluetoothAdapter.GetDefaultAsync();
                RunAsync(op, delegate(BluetoothAdapter adapter)
                {
                    callback(adapter != null && adapter.IsLowEnergySupported);
                }, delegate { callback(false); });
            }
            catch { callback(false); }
        }

        private static void RunAsync<T>(IAsyncOperation<T> op, Action<T> ok, Action<Exception> fail)
        {
            if (op == null)
            {
                if (fail != null) fail(new Exception("操作不可用"));
                return;
            }
            op.Completed += delegate(IAsyncOperation<T> info, AsyncStatus status)
            {
                try
                {
                    if (status == AsyncStatus.Completed)
                    {
                        if (ok != null) ok(info.GetResults());
                    }
                    else
                    {
                        if (fail != null) fail(info.ErrorCode ?? new Exception("蓝牙操作失败"));
                    }
                }
                catch (Exception ex)
                {
                    if (fail != null) fail(ex);
                }
            };
        }

        public void SetPreferred(ulong address, string name)
        {
            _preferredAddress = address;
            _preferredName = name ?? "";
            if (_connected && _device != null && _device.BluetoothAddress != address)
                Disconnect(false);
            if (_enabled) RestartScan(0);
        }

        public void ClearPreferred()
        {
            _preferredAddress = 0;
            _preferredName = "";
            if (_enabled)
            {
                if (_connected) Disconnect(false);
                RestartScan(0);
            }
        }

        public void Scan()
        {
            if (!_enabled) return;
            RestartScan(0);
        }

        public void Start()
        {
            if (_enabled) return;
            _enabled = true;
            StartScan();
        }

        public void Stop()
        {
            _enabled = false;
            StopScan();
            Disconnect(true);
            HeartRateHub.Clear();
            HeartRateHub.SetBleConnected(false, "");
            SetStatus("已关闭");
        }

        public void Dispose()
        {
            Stop();
        }

        private void SetStatus(string text)
        {
            _status = text ?? "";
            Post(delegate
            {
                if (StatusChanged != null)
                {
                    try { StatusChanged(_status); } catch { }
                }
            });
        }

        private void Post(Action action)
        {
            if (action == null) return;
            if (_ui == null)
            {
                try { action(); } catch { }
                return;
            }
            try { _ui.BeginInvoke(DispatcherPriority.Background, action); }
            catch { }
        }

        private void StartScan()
        {
            if (!_enabled || _scanning || _connecting || _connected) return;
            try
            {
                StopScan();

                BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher();
                watcher.ScanningMode = BluetoothLEScanningMode.Active;
                BluetoothLEAdvertisementFilter filter = new BluetoothLEAdvertisementFilter();
                filter.Advertisement.ServiceUuids.Add(GattServiceUuids.HeartRate);
                watcher.AdvertisementFilter = filter;
                watcher.Received += OnAdvertisement;
                watcher.Stopped += OnWatcherStopped;
                _watcher = watcher;
                _scanning = true;
                watcher.Start();
                SetStatus("正在搜索手环…");
            }
            catch (Exception ex)
            {
                _scanning = false;
                SetStatus("蓝牙搜索失败：" + ex.Message);
            }
        }

        private void StopScan()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.Received -= OnAdvertisement;
                    _watcher.Stopped -= OnWatcherStopped;
                    if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                        _watcher.Stop();
                    _watcher = null;
                }
            }
            catch { }
            _scanning = false;
        }

        private void RestartScan(int delayMs)
        {
            if (!_enabled) return;
            StopScan();
            if (_retry != null) { _retry.Dispose(); _retry = null; }
            if (delayMs <= 0)
            {
                StartScan();
                return;
            }
            _retry = new Timer(delegate
            {
                if (!_enabled) return;
                Post(delegate { StartScan(); });
            }, null, delayMs, Timeout.Infinite);
        }

        private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            _scanning = false;
            if (_enabled && !_connected && !_connecting)
                RestartScan(2500);
        }

        private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                ulong address = args.BluetoothAddress;
                if (address == 0) return;

                string name = args.Advertisement.LocalName;
                if (string.IsNullOrEmpty(name))
                {
                    if (address == _preferredAddress) name = _preferredName;
                    if (string.IsNullOrEmpty(name)) name = "心率设备";
                }

                BleHeartDevice device = new BleHeartDevice();
                device.Address = address;
                device.Name = name;
                device.Rssi = args.RawSignalStrengthInDBm;

                Post(delegate
                {
                    if (DeviceFound != null)
                    {
                        try { DeviceFound(device); } catch { }
                    }
                });

                if (_connected || _connecting) return;
                if (_preferredAddress != 0 && address != _preferredAddress) return;

                // 没有指定“我的设备”时，第一个扫到的就直接连。
                BeginConnect(address, name);
            }
            catch { }
        }

        private void BeginConnect(ulong address, string name)
        {
            if (_connecting || _connected) return;
            _connecting = true;
            StopScan();
            SetStatus("正在连接 " + name + " …");
            Connect(address, name);
        }

        private void Connect(ulong address, string name)
        {
            BluetoothLEDevice device = null;
            GattDeviceService service = null;
            GattCharacteristic characteristic = null;

            Action cleanup = delegate
            {
                try { if (characteristic != null) characteristic.ValueChanged -= OnHeartRateValue; } catch { }
                try { if (device != null) device.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
                try { if (service != null) service.Dispose(); } catch { }
                try { if (device != null) device.Dispose(); } catch { }
            };

            try
            {
                IAsyncOperation<BluetoothLEDevice> op = BluetoothLEDevice.FromBluetoothAddressAsync(address);
                RunAsync(op, delegate(BluetoothLEDevice dev)
                {
                    device = dev;
                    if (device == null)
                    {
                        cleanup();
                        FailConnect("找不到设备");
                        return;
                    }

                    RunAsync(device.GetGattServicesForUuidAsync(
                        GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached),
                        delegate(GattDeviceServicesResult sr)
                        {
                            if (sr == null || sr.Status != GattCommunicationStatus.Success || sr.Services.Count == 0)
                            {
                                cleanup();
                                FailConnect("设备没有心率服务");
                                return;
                            }
                            service = sr.Services[0];

                            RunAsync(service.GetCharacteristicsForUuidAsync(
                                GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached),
                                delegate(GattCharacteristicsResult cr)
                                {
                                    if (cr == null || cr.Status != GattCommunicationStatus.Success
                                        || cr.Characteristics.Count == 0)
                                    {
                                        cleanup();
                                        FailConnect("设备没有心率测量特征");
                                        return;
                                    }
                                    characteristic = cr.Characteristics[0];

                                    RunAsync(characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                        GattClientCharacteristicConfigurationDescriptorValue.Notify),
                                        delegate(GattCommunicationStatus status)
                                        {
                                            if (status != GattCommunicationStatus.Success)
                                            {
                                                cleanup();
                                                FailConnect("订阅心率通知失败");
                                                return;
                                            }

                                            characteristic.ValueChanged += OnHeartRateValue;
                                            device.ConnectionStatusChanged += OnConnectionStatusChanged;

                                            _device = device;
                                            _service = service;
                                            _characteristic = characteristic;
                                            _connected = true;
                                            _connecting = false;
                                            string display = device.Name;
                                            if (string.IsNullOrEmpty(display)) display = name;
                                            if (string.IsNullOrEmpty(display) || display == "心率设备")
                                            {
                                                string hex = address.ToString("X12");
                                                display = "心率手环 " + hex.Substring(8, 4);
                                            }
                                            HeartRateHub.SetBleConnected(true, display);
                                            SetStatus("已连接 " + display);
                                        },
                                        delegate(Exception ex)
                                        {
                                            cleanup();
                                            FailConnect("订阅失败：" + ex.Message);
                                        });
                                },
                                delegate(Exception ex)
                                {
                                    cleanup();
                                    FailConnect("读取心率特征失败：" + ex.Message);
                                });
                        },
                        delegate(Exception ex)
                        {
                            cleanup();
                            FailConnect("读取心率服务失败：" + ex.Message);
                        });
                },
                delegate(Exception ex)
                {
                    cleanup();
                    FailConnect(ex.Message);
                });
            }
            catch (Exception ex)
            {
                cleanup();
                FailConnect(ex.Message);
            }
        }

        private void FailConnect(string why)
        {
            _connecting = false;
            _connected = false;
            HeartRateHub.SetBleConnected(false, "");
            SetStatus("连接失败：" + why);
            if (_enabled) RestartScan(3500);
        }

        private void OnHeartRateValue(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                if (args.CharacteristicValue == null) return;
                DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                if (data.Length < 2) return;

                byte flags = data[0];
                bool uint16 = (flags & 0x01) != 0;
                bool contact = (flags & 0x06) == 0x06;
                int offset = 1;
                int bpm;
                if (uint16)
                {
                    if (data.Length < 3) return;
                    bpm = data[1] | (data[2] << 8);
                    offset = 3;
                }
                else
                {
                    bpm = data[1];
                    offset = 2;
                }

                int? rr = null;
                if ((flags & 0x10) != 0 && data.Length >= offset + 2)
                {
                    int rrRaw = data[offset] | (data[offset + 1] << 8);
                    if (rrRaw > 0) rr = (int)Math.Round(rrRaw * 1000.0 / 1024.0);
                }

                HeartRateHub.Publish(bpm, rr, "BLE");
                HeartRateSample sample = new HeartRateSample();
                sample.Bpm = bpm;
                sample.RrMs = rr;
                sample.At = DateTime.UtcNow;
                sample.Contact = contact;
                sample.Source = "BLE";
                Post(delegate
                {
                    if (Sample != null)
                    {
                        try { Sample(sample); } catch { }
                    }
                });
            }
            catch { }
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Post(delegate
                {
                    Disconnect(false);
                    if (_enabled) RestartScan(3000);
                });
            }
        }

        private void Disconnect(bool silent)
        {
            _connected = false;
            _connecting = false;
            try
            {
                if (_characteristic != null) _characteristic.ValueChanged -= OnHeartRateValue;
            }
            catch { }
            try
            {
                if (_device != null) _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }
            catch { }
            try { if (_characteristic != null && _service != null) { } } catch { }
            try { if (_service != null) _service.Dispose(); } catch { }
            try { if (_device != null) _device.Dispose(); } catch { }
            _characteristic = null;
            _service = null;
            _device = null;
            HeartRateHub.Clear();
            HeartRateHub.SetBleConnected(false, "");
            if (!silent) SetStatus("已断开");
        }
    }
#else
    /// <summary>没有 Windows BLE 元数据时编译用的空实现：高级页的心率卡片会自动隐藏。</summary>
    internal sealed class BleHeartRateSource : IHeartRateSource
    {
        public string Name { get { return "BLE"; } }
        public bool IsRunning { get { return false; } }
        public string Status { get { return ""; } }
        public ulong ConnectedAddress { get { return 0; } }
        public event Action<HeartRateSample> Sample;
        public event Action<string> StatusChanged;
        public event Action<BleHeartDevice> DeviceFound;

        public BleHeartRateSource(Dispatcher ui, ulong preferredAddress, string preferredName) { }
        public static void Probe(Action<bool> callback) { if (callback != null) callback(false); }
        public void SetPreferred(ulong address, string name) { }
        public void ClearPreferred() { }
        public void Scan() { }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
#endif
}
