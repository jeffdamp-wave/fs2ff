﻿using fs2ff.Models;
using fs2ff.SimConnect;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

#pragma warning disable 67

namespace fs2ff
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local", Justification = "DispatcherTimer field is kept to prevent premature GC")]
    public class MainViewModel : INotifyPropertyChanged, ISimConnectMessageHandler
    {
        private readonly DataSender _dataSender;
        private readonly SimConnectAdapter _simConnect;
        private readonly IpDetectionService _ipDetectionService;
        private readonly DispatcherTimer _ipHintTimer;
        private readonly DispatcherTimer _autoConnectTimer;
        private readonly DispatcherTimer _stratusTimer;
        private readonly DispatcherTimer _gdl90Timer;
        private object _ownerLock = new object();
        private Traffic _ownerInfo;

        private uint _attitudeFrequency = Preferences.Default.att_freq.AdjustToBounds(AttitudeFrequencyMin, AttitudeFrequencyMax);
        private uint _trafficRadiusNm = Preferences.Default.traffic_radius_nautical_miles.AdjustToBounds(TrafficRadiusNmMin, TrafficRadiusNmMax);
        private bool _autoDetectIpEnabled = Preferences.Default.ip_detection_enabled;
        private bool _autoConnectEnabled = Preferences.Default.auto_connect_enabled;
        private bool _autoExit = Preferences.Default.auto_exit;
        private bool _dataAttitudeEnabled = Preferences.Default.att_enabled;
        private bool _gdl90Enabled = Preferences.Default.gdl90_enabled;
        private bool _hideTrafficEnabled = Preferences.Default.hide_static_traffic_enabled;
        private bool _settingsPanelVisible = Preferences.Default.settings_pane_visible;

        private bool _dataPositionEnabled = Preferences.Default.pos_enabled;
        private bool _dataTrafficEnabled = Preferences.Default.tfk_enabled;
        private bool _dataStratusEnabled = Preferences.Default.stratus_enabled;
        private bool _dataStratuxEnabled = Preferences.Default.stratux_enabled;
        private bool _errorOccurred;
        private IntPtr _hwnd = IntPtr.Zero;
        private readonly IDictionary<string, IPAddress> _ipAddresses = new Dictionary<string, IPAddress>();
        private uint _ipHintMinutesLeft = Preferences.Default.ip_hint_time;

        /// <summary>
        /// We keep a copy of the last Ownership record to use later against other traffic.
        /// GDL90 will flag other Traffic that is near us so we need global access to this value.
        /// </summary>
        public Traffic OwnerInfo
        {
            get
            {
                lock (_ownerLock)
                {
                    return _ownerInfo;
                }
            }
            set
            {
                lock (_ownerLock)
                {
                    _ownerInfo = value;
                }
            }
        }

        public MainViewModel(DataSender dataSender, SimConnectAdapter simConnect, IpDetectionService ipDetectionService)
        {
            _dataSender = dataSender;
            _simConnect = simConnect;
            _simConnect.StateChanged += SimConnectStateChanged;
            _simConnect.PositionReceived += SimConnectPositionReceived;
            _simConnect.AttitudeReceived += SimConnectAttitudeReceived;
            _simConnect.TrafficReceived += SimConnectTrafficReceived;
            _simConnect.OwnerReceived += SimConnectOwnerReceived;
            _ipDetectionService = ipDetectionService;
            _ipDetectionService.NewIpDetected += IpDetectionService_NewIpDetected;

            OpenSettingsCommand = new ActionCommand(OpenSettings);
            DismissSettingsPaneCommand = new ActionCommand(DismissSettingsPane);
            GotoNewReleaseCommand = new ActionCommand(GotoReleaseNotesPage);
            ToggleConnectCommand = new ActionCommand(ToggleConnect, CanConnect);
            ToggleSettingsPaneCommand = new ActionCommand(ToggleSettingsPane);

            var splitIps = Preferences.Default.ip_address.Split(',');
            foreach (var ipStr in splitIps)
            {
                var trimIpStr = ipStr.Trim();
                if (IPAddress.TryParse(trimIpStr, out var ip))
                {
                    _ipAddresses.Add(ipStr, ip);
                }
            }

            _ipHintTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Normal, IpHintCallback, Dispatcher.CurrentDispatcher);
            _autoConnectTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, AutoConnectCallback, Dispatcher.CurrentDispatcher);
            _stratusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(800), DispatcherPriority.Normal, SimConnectDeviceStatusUpdate, Dispatcher.CurrentDispatcher);
            _gdl90Timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, SimConnectGdl90Update, Dispatcher.CurrentDispatcher);
            _ownerInfo = new Traffic(new TrafficData(), Gdl90Traffic.SelfIaco, SimConnectAdapter.OBJECT_ID_USER_RESULT, DateTime.MinValue);
            _ownerInfo.LastUpdate = DateTime.MinValue;

            ManageAutoConnect();
            CheckForUpdates();
            UpdateVisualState();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static uint AttitudeFrequencyMax => 50;
        public static uint AttitudeFrequencyMin => 5;
        public static uint TrafficRadiusNmMax => 100;
        public static uint TrafficRadiusNmMin => 5;

        public static string WindowTitle => $"fs2ff (GDL90) - {App.AssemblyVersion}";

        public uint AttitudeFrequency
        {
            get => _attitudeFrequency;
            set
            {
                if (_attitudeFrequency != value)
                {
                    _attitudeFrequency = value.AdjustToBounds(AttitudeFrequencyMin, AttitudeFrequencyMax);
                    Preferences.Default.att_freq = value;
                    Preferences.Default.Save();
                    if (_simConnect.Connected)
                    {
                        Connect();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or Sets the Distance from the player aircraft to look for other Traffic in nautical miles
        /// BUGBUG: Seem like there is an issue in the SDK where it works at 5, 10, but above 10 it seem to go to far out.
        /// </summary>
        public uint TrafficRadiusNm
        {
            get => _trafficRadiusNm;
            set
            {
                if (value != _trafficRadiusNm)
                {
                    _trafficRadiusNm = value.AdjustToBounds(TrafficRadiusNmMin, TrafficRadiusNmMax);
                    Preferences.Default.traffic_radius_nautical_miles = value;
                    Preferences.Default.Save();
                    // Requires connection Reset
                    if (_simConnect.Connected)
                    {
                        Connect();
                    }
                }
            }
        }

        public bool AutoConnectEnabled
        {
            get => _autoConnectEnabled;
            set
            {
                if (value != _autoConnectEnabled)
                {
                    _autoConnectEnabled = value;
                    Preferences.Default.auto_connect_enabled = value;
                    Preferences.Default.Save();

                    // If auto connect was running and the sim wasn't then there is likely
                    // an error flagged. Clear it whenever AutoConnectEnabled changes state
                    // so in the event auto connect is disabled the window won't show
                    // a meaningless connection error to the user.
                    _errorOccurred = false;

                    ManageAutoConnect();
                    UpdateVisualState();
                }
            }
        }

        public bool AutoExitEnabled
        {
            get => _autoExit;
            set
            {
                if (value != _autoExit)
                {
                    _autoExit = value;
                    Preferences.Default.auto_exit = value;
                    Preferences.Default.Save();
                }
            }
        }

        public bool AutoConnectLabelVisible { get; private set; }

        public bool AutoDetectIpEnabled
        {
            get => _autoDetectIpEnabled;
            set
            {
                if (value != _autoDetectIpEnabled)
                {
                    _autoDetectIpEnabled = value;
                    Preferences.Default.ip_detection_enabled = value;
                    Preferences.Default.Save();
                }
            }
        }

        public bool ConnectButtonEnabled { get => !_autoConnectEnabled; }

        public string? ConnectButtonText { get; private set; }

        public bool ConnectedLabelVisible { get; private set; }

        public bool DataAttitudeEnabled
        {
            get => _dataAttitudeEnabled;
            set
            {
                if (value != _dataAttitudeEnabled)
                {
                    _dataAttitudeEnabled = value;
                    Preferences.Default.att_enabled = value;
                    Preferences.Default.Save();
                }
            }
        }

        public bool DataGdl90Enabled
        {
            get => this._gdl90Enabled;
            set
            {
                if (value != this._gdl90Enabled)
                {
                    this._gdl90Enabled = value;
                    Preferences.Default.gdl90_enabled = value;
                    Preferences.Default.Save();
                    // Force a complete reconnect
                    if (_simConnect.Connected)
                    {
                        this.Connect();
                    }
                }
            }
        }

        public bool DataHideTrafficEnabled
        {
            get => this._hideTrafficEnabled;
            set
            {
                if (value != this._hideTrafficEnabled)
                {
                    this._hideTrafficEnabled = value;
                    Preferences.Default.hide_static_traffic_enabled = value;
                    Preferences.Default.Save();
                }
            }
        }

        public bool DataStratusEnabled
        {
            get => _dataStratusEnabled;
            set
            {
                if (value != _dataStratusEnabled)
                {
                    _dataStratusEnabled = value;
                    Preferences.Default.stratus_enabled = value;
                    Preferences.Default.Save();
                    // Force a complete reconnect
                    if (_simConnect.Connected)
                    {
                        this.Connect();
                    }
                }
            }
        }

        public bool DataStratuxEnabled
        {
            get => _dataStratuxEnabled;
            set
            {
                if (value != _dataStratuxEnabled)
                {
                    _dataStratuxEnabled = value;
                    Preferences.Default.stratux_enabled = value;
                    Preferences.Default.Save();
                    // Force a complete reconnect
                    if (_simConnect.Connected)
                    {
                        this.Connect();
                    }
                }
            }
        }

        public bool DataPositionEnabled
        {
            get => _dataPositionEnabled;
            set
            {
                if (value != _dataPositionEnabled)
                {
                    _dataPositionEnabled = value;
                    Preferences.Default.pos_enabled = value;
                    Preferences.Default.Save();
                }
            }
        }

        public bool DataTrafficEnabled
        {
            get => _dataTrafficEnabled;
            set
            {
                if (value != _dataTrafficEnabled)
                {
                    _dataTrafficEnabled = value;
                    Preferences.Default.tfk_enabled = value;
                    Preferences.Default.Save();
                }
            }
        }

        public ICommand DismissSettingsPaneCommand { get; }

        public bool ErrorLabelVisible { get; private set; }

        public ICommand GotoNewReleaseCommand { get; }

        public bool IndicatorVisible { get; private set; }

        public bool IpHintVisible => _ipAddresses.Count == 0 && IpHintMinutesLeft == 0;

        public bool NotLabelVisible { get; private set; }

        public ICommand OpenSettingsCommand { get; }

        public bool SettingsPaneVisible
        {
            get => _settingsPanelVisible;
            set
            {
                if (value != _settingsPanelVisible)
                {
                    _settingsPanelVisible = value;
                    Preferences.Default.settings_pane_visible = value;
                    Preferences.Default.Save();
                }
            }
        }

        public string IpAddressString
        {
            get
            {
                return ConvertIpsToString();
            }
            set
            {
                _ipAddresses.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var splitIps = value.Split(',');
                    foreach (var ipStr in splitIps)
                    {
                        var trimIpStr = ipStr.Trim();
                        if (IPAddress.TryParse(trimIpStr, out var address))
                        {
                            if (!_ipAddresses.ContainsKey(trimIpStr))
                            {
                                _ipAddresses.Add(trimIpStr, address);
                            }
                        }
                    }
                }

                Preferences.Default.ip_address = ConvertIpsToString();
            }
        }

        private string ConvertIpsToString()
        {
            var strBldr = new StringBuilder();
            foreach (var ip in _ipAddresses.Keys)
            {
                if (strBldr.Length > 0)
                {
                    strBldr.Append(", ");
                }
                strBldr.Append(ip);
            }

            return strBldr.ToString();
        }

        public ActionCommand ToggleConnectCommand { get; }

        public ICommand ToggleSettingsPaneCommand { get; }

        public bool UpdateMsgVisible => UpdateInfo != null && !SettingsPaneVisible;

        public IntPtr WindowHandle
        {
            get => _hwnd;
            set
            {
                _hwnd = value;
                ToggleConnectCommand.TriggerCanExecuteChanged();
            }
        }

        private FlightSimState CurrentFlightSimState =>
            _simConnect.Connected ? FlightSimState.Connected :
            AutoConnectEnabled ? FlightSimState.AutoConnecting :
            _errorOccurred ? FlightSimState.ErrorOccurred :
            FlightSimState.Disconnected;

        private uint IpHintMinutesLeft
        {
            get => _ipHintMinutesLeft;
            set
            {
                if (value != _ipHintMinutesLeft)
                {
                    _ipHintMinutesLeft = value;
                    Preferences.Default.ip_hint_time = value;
                    Preferences.Default.Save();
                }
            }
        }

        private UpdateInformation? UpdateInfo { get; set; }

        public void Dispose()
        {
            _ipDetectionService.NewIpDetected -= IpDetectionService_NewIpDetected;

            _simConnect.TrafficReceived -= SimConnectTrafficReceived;
            _simConnect.AttitudeReceived -= SimConnectAttitudeReceived;
            _simConnect.PositionReceived -= SimConnectPositionReceived;
            _simConnect.StateChanged -= SimConnectStateChanged;
            _simConnect.OwnerReceived -= SimConnectOwnerReceived;
            _simConnect.Dispose();

            _dataSender.Dispose();
        }

        public void ReceiveFlightSimMessage() => _simConnect.ReceiveMessage();

        private void AutoConnectCallback(object? sender, EventArgs e) => Connect();

        private bool CanConnect() => WindowHandle != IntPtr.Zero;

        private void CheckForUpdates()
        {
            UpdateChecker.Check().ContinueWith(task => UpdateInfo = task.Result, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void Connect() => _simConnect.Connect(WindowHandle);

        private void Disconnect() => _simConnect.Disconnect();

        private void DismissSettingsPane() => SettingsPaneVisible = false;


        private void GotoReleaseNotesPage()
        {
            if (UpdateInfo != null)
            {
                Process.Start("explorer.exe", UpdateInfo.DownloadLink.ToString());
            }
        }

        private void IpDetectionService_NewIpDetected(IPAddress ip)
        {
            if (AutoDetectIpEnabled)
            {
                if (!_ipAddresses.ContainsKey(ip.ToString()))
                {
                    _ipAddresses.Add(ip.ToString(), ip);
                }
            }
        }


        private void IpHintCallback(object? sender, EventArgs e)
        {
            if (IpHintMinutesLeft > 0 && _ipAddresses?.Count == 0 && _simConnect.Connected)
            {
                IpHintMinutesLeft--;
            }
        }

        private void ManageAutoConnect()
        {
            if (!AutoConnectEnabled || CurrentFlightSimState == FlightSimState.Connected)
            {
                _autoConnectTimer.Stop();
            }
            else
            {
                _autoConnectTimer.Start();
            }
        }

        private void OpenSettings() => SettingsPaneVisible = true;

        private void ResetDataSenderConnection()
        {
            if (CurrentFlightSimState == FlightSimState.Connected)
            {
                _dataSender.Connect(_ipAddresses);
                if (this._gdl90Enabled)
                {
                    _stratusTimer.Start();
                    _gdl90Timer.Start();
                }
            }
            else
            {
                if (_stratusTimer.IsEnabled)
                {
                    _stratusTimer.Stop();
                    _gdl90Timer.Stop();
                }

                _dataSender.Disconnect();
            }
        }

        private void ResetIpHintMinutesLeft()
        {
            Preferences.Default.PropertyValues["ip_hint_time"].SerializedValue = Preferences.Default.Properties["ip_hint_time"].DefaultValue;
            Preferences.Default.PropertyValues["ip_hint_time"].Deserialized = false;
            IpHintMinutesLeft = Preferences.Default.ip_hint_time;
        }

        private async Task SimConnectAttitudeReceived(Attitude att)
        {
            if (DataAttitudeEnabled)
            {
                await _dataSender.Send(att).ConfigureAwait(false);
            }
        }

        private void SimConnectStateChanged(FlightSimState state)
        {
            _errorOccurred = state.HasFlag(FlightSimState.ErrorOccurred);
            if (state == FlightSimState.Quit && this._autoExit)
            {
                Preferences.Default.Save();
                System.Windows.Application.Current.Shutdown();
            }

            ManageAutoConnect();
            ResetDataSenderConnection();
            UpdateVisualState();
        }

        /// <summary>
        /// GDL90 spec messages
        /// </summary>
        /// <returns></returns>
        private async void SimConnectGdl90Update(object? sender, EventArgs e)
        {
            if (!DataGdl90Enabled)
            {
                return;
            }

            var hb = new Gdl90Heartbeat();
            var data = hb.ToGdl90Message();
            await _dataSender.Send(data).ConfigureAwait(false);

            if (ViewModelLocator.Main.DataStratuxEnabled)
            {
                var shb = new Gdl90StratuxHeartbeat();
                data = shb.ToGdl90Message();
                await _dataSender.Send(data).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send emulated device specific status messages
        /// </summary>
        /// <returns></returns>
        private async void SimConnectDeviceStatusUpdate(object? sender, EventArgs e)
        {
            if (!DataGdl90Enabled)
            {
                return;
            }

            if (ViewModelLocator.Main.DataStratusEnabled)
            {
                var status = new Gdl90StratusStatus();
                await _dataSender.Send(status.ToGdl90Message()).ConfigureAwait(false);
            }

            if (ViewModelLocator.Main.DataStratuxEnabled)
            {
                var stratux = new Gdl90StratuxStatus();
                await _dataSender.Send(stratux.ToGdl90Message()).ConfigureAwait(false);
            }

            var ffmId = new Gdl90FfmId();
            await _dataSender.Send(ffmId.ToGdl90Message()).ConfigureAwait(false);
        }

        private async Task SimConnectPositionReceived(Position pos)
        {
            if (DataPositionEnabled && pos.IsValid())
            {
                await _dataSender.Send(pos).ConfigureAwait(false);
            }
        }

        private async Task SimConnectTrafficReceived(Traffic tfk)
        {
            // Ignore traffic with id=1, that's our own aircraft
            if (DataTrafficEnabled && !tfk.IsOwner)
            {
                bool send = true;
                if (OwnerInfo.IsValid())
                {
                    // MSFS Traffic Request bubble isn't so good. It will either do < 10nm or <60nm doesn't seem to do any less than that.
                    var curRadius = TrafficRadiusNm.NmToMeters();
                    var distance = OwnerInfo.Td.DistanceMeters(tfk.Td);
                    if (Convert.ToUInt32(distance.Horizontal) > curRadius)
                    {
                        send = false;
                    }
                }

                if (send)
                {
                    await _dataSender.Send(tfk).ConfigureAwait(false);
                }
            }
        }

        private async Task SimConnectOwnerReceived(Traffic tfk)
        {
            OwnerInfo = tfk;
            if (DataGdl90Enabled)
            {
                await _dataSender.Send(tfk).ConfigureAwait(false);
            }
        }

        private void ToggleConnect()
        {
            if (_simConnect.Connected) Disconnect();
            else Connect();
        }

        private void ToggleSettingsPane() => SettingsPaneVisible = !SettingsPaneVisible;

        private void UpdateVisualState()
        {
            (IndicatorVisible, AutoConnectLabelVisible, NotLabelVisible, ErrorLabelVisible, ConnectedLabelVisible, ConnectButtonText) = CurrentFlightSimState switch
            {
                FlightSimState.AutoConnecting => (false, true, false, false, false, "Connect"),
                FlightSimState.Connected => (true, false, false, false, true, "Disconnect"),
                FlightSimState.Disconnected => (false, false, true, false, true, "Connect"),
                FlightSimState.ErrorOccurred => (false, false, false, true, false, "Connect"),
                _ => (false, false, true, false, true, "Connect")
            };
        }
    }
}
