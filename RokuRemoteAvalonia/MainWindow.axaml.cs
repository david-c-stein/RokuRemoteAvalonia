using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using DialogHostAvalonia;
using RokuRemoteAvalonia.Controls;
using RokuRemoteAvalonia.Models;
using RRemote.Roku;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static RRemote.Roku.RokuClient;

namespace RokuRemoteAvalonia
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private RokuDevice? _selectedDevice;
        private ObservableCollection<RokuDevice> _discoveredDevices;
        private ObservableCollection<RokuApp> _deviceApps;
        private ObservableCollection<RokuApp> _filteredDeviceApps;
        private string _statusMessage;
        private bool _isConnected;
        private string _searchText = "";

        // Add HttpClient for downloading images
        private static readonly HttpClient _httpClient = new();

        // Quick Launch Button Properties
        private string _quickLaunchButton1Text = "Q1";
        private string _quickLaunchButton2Text = "Q2";
        private string _quickLaunchButton3Text = "Q3";
        private string _quickLaunchButton4Text = "Q4";
        private bool _quickLaunchButton1Visible = true;
        private bool _quickLaunchButton2Visible = true;
        private bool _quickLaunchButton3Visible = false;
        private bool _quickLaunchButton4Visible = false;
        private string _quickLaunchButton1Tooltip = "Quick Launch 1";
        private string _quickLaunchButton2Tooltip = "Quick Launch 2";
        private string _quickLaunchButton3Tooltip = "Quick Launch 3";
        private string _quickLaunchButton4Tooltip = "Quick Launch 4";

        // Quick Launch Sequences
        private QuickLaunchSequence _quickLaunchSequence1 = new() { Name = "Button 1" };
        private QuickLaunchSequence _quickLaunchSequence2 = new() { Name = "Button 2" };
        private QuickLaunchSequence _quickLaunchSequence3 = new() { Name = "Button 3" };
        private QuickLaunchSequence _quickLaunchSequence4 = new() { Name = "Button 4" };

        // Recording state
        private bool _isRecording = false;
        private int _currentRecordingButton = 0;
        private QuickLaunchSequence? _currentRecordingSequence;
        private List<QuickLaunchAction> _tempRecordedActions = new();

        // View state properties
        private bool _showInstalledApps = true;
        private bool _showQuickLaunchConfig = false;
        private bool _showKeyboard = false;

        // Remote-only mode: hides everything except the remote control panel
        private bool _isRemoteOnlyMode = false;
        private double _savedWidth;
        private double _savedHeight;
        private double _savedMinWidth;
        private double _savedMinHeight;
        private bool _savedCanResize;
        private WindowDecorations _savedWindowDecorations;
        private Avalonia.Media.IBrush? _savedBackground;
        private Avalonia.Media.IBrush? _savedHostBackground;
        private IReadOnlyList<WindowTransparencyLevel>? _savedTransparencyLevelHint;
        private Thickness _savedRootGridMargin;
        private Thickness _savedContentGridMargin;
        private GridLength _savedSpacerColumnWidth;
        private GridLength _savedAppsColumnWidth;

        // Keyboard mode
        private bool _keyboardEnabled = false;
        private RokuKeyboard? _directKeyboard;

        // Cross-platform mic capture + WebSocket streamer
        private readonly RRemote.Roku.VoiceStreamer _voiceStreamer = new();

        public MainWindow()
        {
            InitializeComponent();
            _discoveredDevices = new ObservableCollection<RokuDevice>();
            _deviceApps = new ObservableCollection<RokuApp>();
            _filteredDeviceApps = new ObservableCollection<RokuApp>();
            _statusMessage = "Click 'Discover Devices' to find Roku devices on your network";
            DataContext = this;

            // Button consumes PointerPressed/Released for its own Click logic, so we must
            // subscribe with handledEventsToo:true to receive them for hold-to-talk.
            MicButton.AddHandler(
                Avalonia.Input.InputElement.PointerPressedEvent,
                OnMicrophonePressed,
                Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
                handledEventsToo: true);

            MicButton.AddHandler(
                Avalonia.Input.InputElement.PointerReleasedEvent,
                OnMicrophoneReleased,
                Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        // Recording Properties
        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                _isRecording = value;
                OnPropertyChanged(nameof(IsRecording));
                OnPropertyChanged(nameof(RecordingStatusText));
                OnPropertyChanged(nameof(RecordingInstructions));
                OnPropertyChanged(nameof(RecordedActionsText));
                OnPropertyChanged(nameof(RecordingModeInstructions));
            }
        }

        public string RecordingStatusText => 
            IsRecording ? $"Button {_currentRecordingButton}" : "";

        public string RecordingInstructions =>
            IsRecording ? 
            "Use the remote control or click apps to record actions. When finished, click Save to complete the sequence." : 
            "";

        public string RecordedActionsText =>
            IsRecording ? 
            $"Actions: {_tempRecordedActions.Count}\n{string.Join(", ", _tempRecordedActions.Take(2).Select(a => a.DisplayName))}{(_tempRecordedActions.Count > 2 ? "..." : "")}" :
            "";

        public string RecordingModeInstructions =>
            IsRecording ? 
            "🔴 RECORDING MODE: Click on an app to add it to your sequence" : 
            "Click on an app to launch it";

        // Quick Launch Summary Properties
        public string QuickLaunchButton1Summary => _quickLaunchSequence1.GetDisplaySummary();
        public string QuickLaunchButton2Summary => _quickLaunchSequence2.GetDisplaySummary();
        public string QuickLaunchButton3Summary => _quickLaunchSequence3.GetDisplaySummary();
        public string QuickLaunchButton4Summary => _quickLaunchSequence4.GetDisplaySummary();

        // Existing properties
        public ObservableCollection<RokuDevice> DiscoveredDevices
        {
            get => _discoveredDevices;
            set
            {
                _discoveredDevices = value;
                OnPropertyChanged(nameof(DiscoveredDevices));
            }
        }

        public ObservableCollection<RokuApp> DeviceApps
        {
            get => _deviceApps;
            set
            {
                _deviceApps = value;
                OnPropertyChanged(nameof(DeviceApps));
                FilterApps();
            }
        }

        public ObservableCollection<RokuApp> FilteredDeviceApps
        {
            get => _filteredDeviceApps;
            set
            {
                _filteredDeviceApps = value;
                OnPropertyChanged(nameof(FilteredDeviceApps));
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                OnPropertyChanged(nameof(HasSearchText));
                FilterApps();
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        public RokuDevice? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(IsDeviceSelected));
                if (value != null)
                {
                    LoadDeviceApps();
                    IsConnected = true;
                    StatusMessage = $"Connected to {value.model_name} ({value.serial_number})";
                }
            }
        }

        public bool IsDeviceSelected => _selectedDevice != null;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }

        public bool KeyboardEnabled
        {
            get => _keyboardEnabled;
            set
            {
                _keyboardEnabled = value;
                OnPropertyChanged(nameof(KeyboardEnabled));
                OnPropertyChanged(nameof(KeyboardButtonText));
                
                if (value)
                {
                    ShowDirectKeyboard();
                }
                else
                {
                    HideDirectKeyboard();
                }
            }
        }

        public string KeyboardButtonText => _keyboardEnabled ? "🔤 ON" : "🔤";

        public bool ShowKeyboard
        {
            get => _showKeyboard;
            set
            {
                _showKeyboard = value;
                OnPropertyChanged(nameof(ShowKeyboard));
            }
        }

        public bool IsRemoteOnlyMode
        {
            get => _isRemoteOnlyMode;
            set
            {
                _isRemoteOnlyMode = value;
                OnPropertyChanged(nameof(IsRemoteOnlyMode));
                OnPropertyChanged(nameof(RemoteOnlyToggleTooltip));
            }
        }

        public string RemoteOnlyToggleTooltip =>
            IsRemoteOnlyMode ? "Show Full UI" : "Show Remote Only";

        // Quick Launch Button Properties
        public string QuickLaunchButton1Text
        {
            get => _quickLaunchButton1Text;
            set
            {
                _quickLaunchButton1Text = value;
                OnPropertyChanged(nameof(QuickLaunchButton1Text));
            }
        }

        public string QuickLaunchButton2Text
        {
            get => _quickLaunchButton2Text;
            set
            {
                _quickLaunchButton2Text = value;
                OnPropertyChanged(nameof(QuickLaunchButton2Text));
            }
        }

        public string QuickLaunchButton3Text
        {
            get => _quickLaunchButton3Text;
            set
            {
                _quickLaunchButton3Text = value;
                OnPropertyChanged(nameof(QuickLaunchButton3Text));
            }
        }

        public string QuickLaunchButton4Text
        {
            get => _quickLaunchButton4Text;
            set
            {
                _quickLaunchButton4Text = value;
                OnPropertyChanged(nameof(QuickLaunchButton4Text));
            }
        }

        public bool QuickLaunchButton1Visible
        {
            get => _quickLaunchButton1Visible;
            set
            {
                _quickLaunchButton1Visible = value;
                OnPropertyChanged(nameof(QuickLaunchButton1Visible));
            }
        }

        public bool QuickLaunchButton2Visible
        {
            get => _quickLaunchButton2Visible;
            set
            {
                _quickLaunchButton2Visible = value;
                OnPropertyChanged(nameof(QuickLaunchButton2Visible));
            }
        }

        public bool QuickLaunchButton3Visible
        {
            get => _quickLaunchButton3Visible;
            set
            {
                _quickLaunchButton3Visible = value;
                OnPropertyChanged(nameof(QuickLaunchButton3Visible));
            }
        }

        public bool QuickLaunchButton4Visible
        {
            get => _quickLaunchButton4Visible;
            set
            {
                _quickLaunchButton4Visible = value;
                OnPropertyChanged(nameof(QuickLaunchButton4Visible));
            }
        }

        public string QuickLaunchButton1Tooltip
        {
            get => _quickLaunchButton1Tooltip;
            set
            {
                _quickLaunchButton1Tooltip = value;
                OnPropertyChanged(nameof(QuickLaunchButton1Tooltip));
            }
        }

        public string QuickLaunchButton2Tooltip
        {
            get => _quickLaunchButton2Tooltip;
            set
            {
                _quickLaunchButton2Tooltip = value;
                OnPropertyChanged(nameof(QuickLaunchButton2Tooltip));
            }
        }

        public string QuickLaunchButton3Tooltip
        {
            get => _quickLaunchButton3Tooltip;
            set
            {
                _quickLaunchButton3Tooltip = value;
                OnPropertyChanged(nameof(QuickLaunchButton3Tooltip));
            }
        }

        public string QuickLaunchButton4Tooltip
        {
            get => _quickLaunchButton4Tooltip;
            set
            {
                _quickLaunchButton4Tooltip = value;
                OnPropertyChanged(nameof(QuickLaunchButton4Tooltip));
            }
        }

        public bool ShowInstalledApps
        {
            get => _showInstalledApps;
            set
            {
                _showInstalledApps = value;
                OnPropertyChanged(nameof(ShowInstalledApps));
            }
        }

        public bool ShowQuickLaunchConfig
        {
            get => _showQuickLaunchConfig;
            set
            {
                _showQuickLaunchConfig = value;
                OnPropertyChanged(nameof(ShowQuickLaunchConfig));
            }
        }

        // Recording Event Handlers
        public void OnRecordButton1(object? sender, RoutedEventArgs e) => StartRecording(1, _quickLaunchSequence1);
        public void OnRecordButton2(object? sender, RoutedEventArgs e) => StartRecording(2, _quickLaunchSequence2);
        public void OnRecordButton3(object? sender, RoutedEventArgs e) => StartRecording(3, _quickLaunchSequence3);
        public void OnRecordButton4(object? sender, RoutedEventArgs e) => StartRecording(4, _quickLaunchSequence4);

        private void StartRecording(int buttonNumber, QuickLaunchSequence sequence)
        {
            _currentRecordingButton = buttonNumber;
            _currentRecordingSequence = sequence;
            _tempRecordedActions.Clear();
            IsRecording = true;
            
            // Return to apps view so user can select apps during recording
            ShowInstalledApps = true;
            ShowQuickLaunchConfig = false;
            
            StatusMessage = $"🔴 Recording sequence for Button {buttonNumber}. Use remote or click apps to record actions.";
        }

        public void OnSaveRecording(object? sender, RoutedEventArgs e)
        {
            if (_currentRecordingSequence != null)
            {
                _currentRecordingSequence.Actions.Clear();
                _currentRecordingSequence.Actions.AddRange(_tempRecordedActions);
                
                // Update the summary display
                OnPropertyChanged(nameof(QuickLaunchButton1Summary));
                OnPropertyChanged(nameof(QuickLaunchButton2Summary));
                OnPropertyChanged(nameof(QuickLaunchButton3Summary));
                OnPropertyChanged(nameof(QuickLaunchButton4Summary));
                
                // Update HasActions properties
                OnPropertyChanged(nameof(QuickLaunchButton1HasActions));
                OnPropertyChanged(nameof(QuickLaunchButton2HasActions));
                OnPropertyChanged(nameof(QuickLaunchButton3HasActions));
                OnPropertyChanged(nameof(QuickLaunchButton4HasActions));
            }

            IsRecording = false;
            
            // Return to configuration view
            ShowInstalledApps = false;
            ShowQuickLaunchConfig = true;
            
            StatusMessage = $"✅ Sequence saved for Button {_currentRecordingButton} ({_tempRecordedActions.Count} actions)";
        }

        public void OnCancelRecording(object? sender, RoutedEventArgs e)
        {
            IsRecording = false;
            _tempRecordedActions.Clear();
            
            // Return to configuration view
            ShowInstalledApps = false;
            ShowQuickLaunchConfig = true;
            
            StatusMessage = "❌ Recording cancelled";
        }

        private void RecordAction(ActionType type, string command, string parameter, string displayName)
        {
            if (!IsRecording) return;

            var action = new QuickLaunchAction
            {
                Type = type,
                Command = command,
                Parameter = parameter,
                DisplayName = displayName
            };

            _tempRecordedActions.Add(action);
            OnPropertyChanged(nameof(RecordedActionsText));
        }

        // Remote Control Key Commands with recording support
        public async void OnHomeKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Home", "", "Home");
            await SendKey(Keys.Home);
        }

        public async void OnBackKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Back", "", "Back");
            await SendKey(Keys.Back);
        }

        public async void OnUpKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Up", "", "Up");
            await SendKey(Keys.Up);
        }

        public async void OnDownKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Down", "", "Down");
            await SendKey(Keys.Down);
        }

        public async void OnLeftKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Left", "", "Left");
            await SendKey(Keys.Left);
        }

        public async void OnRightKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Right", "", "Right");
            await SendKey(Keys.Right);
        }

        public async void OnSelectKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Select", "", "OK");
            await SendKey(Keys.Select);
        }

        public async void OnPlayPauseKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Play", "", "Play/Pause");
            await SendKey(Keys.Play);
        }

        public async void OnRewindKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Rev", "", "Rewind");
            await SendKey(Keys.Rev);
        }

        public async void OnFastForwardKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Fwd", "", "Fast Forward");
            await SendKey(Keys.Fwd);
        }

        public async void OnVolumeUpKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "VolumeUp", "", "Volume Up");
            await SendKey(Keys.VolumeUp);
        }

        public async void OnVolumeDownKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "VolumeDown", "", "Volume Down");
            await SendKey(Keys.VolumeDown);
        }

        public async void OnMuteKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "VolumeMute", "", "Mute");
            await SendKey(Keys.VolumeMute);
        }

        public async void OnPowerKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Power", "", "Power");
            await SendKey(Keys.Power);
        }

        public async void OnStarKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Info", "", "Info/Star");
            await SendKey(Keys.Info);
        }

        public async void OnOptionsKey(object? sender, RoutedEventArgs e)
        {
            RecordAction(ActionType.KeyPress, "Info", "", "Options");
            await SendKey(Keys.Info);
        }

        // Quick Launch Button Event Handlers - Updated to use sequences
        public async void OnQuickLaunch1(object? sender, RoutedEventArgs e) => await ExecuteSequence(_quickLaunchSequence1);
        public async void OnQuickLaunch2(object? sender, RoutedEventArgs e) => await ExecuteSequence(_quickLaunchSequence2);
        public async void OnQuickLaunch3(object? sender, RoutedEventArgs e) => await ExecuteSequence(_quickLaunchSequence3);
        public async void OnQuickLaunch4(object? sender, RoutedEventArgs e) => await ExecuteSequence(_quickLaunchSequence4);

        private async Task ExecuteSequence(QuickLaunchSequence sequence)
        {
            if (sequence.Actions.Count == 0)
            {
                StatusMessage = "No actions recorded for this button";
                return;
            }

            StatusMessage = $"Executing {sequence.Actions.Count} actions...";

            foreach (var action in sequence.Actions)
            {
                try
                {
                    switch (action.Type)
                    {
                        case ActionType.KeyPress:
                            await SendKey(action.Command);
                            break;
                        case ActionType.AppLaunch:
                            if (SelectedDevice?.Endpoint != null)
                                await RokuClient.LaunchApp(SelectedDevice.Endpoint, action.Parameter);
                            break;
                        case ActionType.Wait:
                            await Task.Delay(action.DelayMs);
                            break;
                    }

                    // Add delay between actions if specified
                    if (action.DelayMs > 0)
                        await Task.Delay(action.DelayMs);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error in sequence: {ex.Message}";
                    return;
                }
            }

            StatusMessage = "Sequence completed successfully";
        }

        // App launch with recording support
        public async void OnLaunchApp(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is RokuApp selectedApp)
            {
                try
                {
                    RecordAction(ActionType.AppLaunch, "LaunchApp", selectedApp.Id, selectedApp.Name);
                    
                    // Only actually launch if not recording (during recording we just capture the action)
                    if (!IsRecording && SelectedDevice?.Endpoint != null)
                    {
                        await RokuClient.LaunchApp(SelectedDevice.Endpoint, selectedApp.Id);
                        StatusMessage = $"Launched {selectedApp.Name}";
                    }
                    else if (IsRecording)
                    {
                        StatusMessage = $"Added {selectedApp.Name} to sequence ({_tempRecordedActions.Count} actions)";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error launching app: {ex.Message}";
                }
                finally
                {
                    listBox.SelectedItem = null;
                }
            }
        }

        // Filter and search methods
        private void FilterApps()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredDeviceApps.Clear();
                foreach (var app in DeviceApps)
                {
                    FilteredDeviceApps.Add(app);
                }
            }
            else
            {
                var filtered = DeviceApps
                    .Where(app => app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                FilteredDeviceApps.Clear();
                foreach (var app in filtered)
                {
                    FilteredDeviceApps.Add(app);
                }
            }
        }

        // Top Function Button Event Handlers
        public async void OnSearchClick(object? sender, RoutedEventArgs e)
        {
            await ShowSearchKeyboardDialog();
        }

        public async void OnMicrophoneClick(object? sender, RoutedEventArgs e)
        {
            if (SelectedDevice?.Endpoint == null)
            {
                StatusMessage = "No device selected";
                return;
            }

            try
            {
                RecordAction(ActionType.KeyPress, "Search", "", "Voice Search");
                await RokuClient.PressKey(SelectedDevice.Endpoint, "Search");
                StatusMessage = "Launched voice search";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error launching voice search: {ex.Message}";
            }
        }

        public void OnKeyboardToggle(object? sender, RoutedEventArgs e)
        {
            KeyboardEnabled = !KeyboardEnabled;
            StatusMessage = KeyboardEnabled 
                ? "Keyboard mode enabled - keypresses will be sent to TV" 
                : "Keyboard mode disabled";
        }

        public void OnToggleRemoteOnlyMode(object? sender, RoutedEventArgs e)
        {
            IsRemoteOnlyMode = !IsRemoteOnlyMode;
            ApplyRemoteOnlyMode(IsRemoteOnlyMode);
            StatusMessage = IsRemoteOnlyMode
                ? "Remote-only view - showing just the remote control"
                : "Full view restored";
        }

        // Lets the user reposition the window by dragging the ROKU logo, since the
        // window has no title bar while in remote-only (frameless) mode.
        public void OnRemoteDragHandlePressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (IsRemoteOnlyMode && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        // Strips the window down to a frameless, transparent shell containing only the
        // remote panel, or restores the previously saved full-window chrome and layout.
        private void ApplyRemoteOnlyMode(bool enable)
        {
            if (enable)
            {
                _savedWidth = Width;
                _savedHeight = Height;
                _savedMinWidth = MinWidth;
                _savedMinHeight = MinHeight;
                _savedCanResize = CanResize;
                _savedWindowDecorations = WindowDecorations;
                _savedBackground = Background;
                _savedHostBackground = MainHost.Background;
                _savedTransparencyLevelHint = TransparencyLevelHint;
                _savedRootGridMargin = RootGrid.Margin;
                _savedContentGridMargin = ContentGrid.Margin;
                _savedSpacerColumnWidth = ContentGrid.ColumnDefinitions[1].Width;
                _savedAppsColumnWidth = ContentGrid.ColumnDefinitions[2].Width;

                RootGrid.Margin = new Thickness(0);
                ContentGrid.Margin = new Thickness(0);
                ContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                ContentGrid.ColumnDefinitions[2].Width = new GridLength(0);
                ContentGrid.ColumnDefinitions[0].Width = GridLength.Auto;

                // null (not Transparent) => the area is not hit-testable, so clicks
                // fall through to whatever is behind the window.
                Background = null;
                MainHost.Background = null;

                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                WindowDecorations = WindowDecorations.None;
                CanResize = false;

                // Must drop the min-size *before* SizeToContent, otherwise the window
                // is pinned at 750x850 and the empty region remains part of the window.
                MinWidth = 0;
                MinHeight = 0;
                SizeToContent = SizeToContent.WidthAndHeight;
            }
            else
            {
                SizeToContent = SizeToContent.Manual;
                WindowDecorations = _savedWindowDecorations;
                Background = _savedBackground;
                MainHost.Background = _savedHostBackground;
                TransparencyLevelHint = _savedTransparencyLevelHint ?? Array.Empty<WindowTransparencyLevel>();
                CanResize = _savedCanResize;

                RootGrid.Margin = _savedRootGridMargin;
                ContentGrid.Margin = _savedContentGridMargin;
                ContentGrid.ColumnDefinitions[1].Width = _savedSpacerColumnWidth;
                ContentGrid.ColumnDefinitions[2].Width = _savedAppsColumnWidth;
                ContentGrid.ColumnDefinitions[0].Width = new GridLength(350);

                // Restore min-size first so the explicit Width/Height are not clamped.
                MinWidth = _savedMinWidth;
                MinHeight = _savedMinHeight;
                Width = _savedWidth;
                Height = _savedHeight;
            }
        }

        // Info Button Click Handler - NEW
        public async void OnInfoClick(object? sender, RoutedEventArgs e)
        {
            if (SelectedDevice == null)
            {
                StatusMessage = "No device selected";
                return;
            }

            try
            {
                await ShowDeviceInfoDialog();
                StatusMessage = "Device information displayed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error showing device info: {ex.Message}";
            }
        }

        public void OnSettingsClick(object? sender, RoutedEventArgs e)
        {
            StatusMessage = "Settings feature coming soon...";
        }

        public async void OnKeyboardClick(object? sender, RoutedEventArgs e)
        {
            await ShowSearchKeyboardDialog();
        }

        public void OnClearSearch(object? sender, RoutedEventArgs e)
        {
            SearchText = "";
            StatusMessage = "Search cleared";
        }

        private async Task ShowSearchKeyboardDialog()
        {
            var keyboard = new RokuKeyboard
            {
                Text = SearchText,
                Title = "Enter Search Text",
                ShowOkCancel = true
            };

            keyboard.OkClicked += (s, e) =>
            {
                DialogHost.Close("MainDialogHost", keyboard.Text);
            };

            keyboard.CancelClicked += (s, e) =>
            {
                DialogHost.Close("MainDialogHost", null);
            };

            var result = await DialogHost.Show(keyboard, "MainDialogHost");

            if (result is string searchResult && !string.IsNullOrWhiteSpace(searchResult))
            {
                SearchText = searchResult;
                
                if (SelectedDevice?.Endpoint != null)
                {
                    try
                    {
                        await RokuClient.Search(SelectedDevice.Endpoint, searchResult);
                        StatusMessage = $"Searching for: {searchResult}";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error performing search: {ex.Message}";
                    }
                }
            }
        }

        // Device Information Dialog - NEW
        private async Task ShowDeviceInfoDialog()
        {
            if (SelectedDevice == null) return;

            var deviceInfoDialog = CreateDeviceInfoDialog();
            await DialogHost.Show(deviceInfoDialog, "MainDialogHost");
        }

        private UserControl CreateDeviceInfoDialog()
        {
            var device = SelectedDevice!;

            // Create main container
            var dialogContent = new StackPanel
            {
                Spacing = 15,
                Width = 500,
                Margin = new Avalonia.Thickness(20)
            };

            // Header
            var header = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var infoIcon = new TextBlock
            {
                Text = "ℹ️",
                FontSize = 24,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = "Device Information",
                FontSize = 20,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            header.Children.Add(infoIcon);
            header.Children.Add(title);
            dialogContent.Children.Add(header);

            // Device details in a scrollable area
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 400,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

            var detailsPanel = new StackPanel { Spacing = 10 };

            // Helper method to create info rows
            void AddInfoRow(string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;

                var row = new Grid
                {
                    ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("150,*")
                };

                var labelBlock = new TextBlock
                {
                    Text = label + ":",
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                };

                var valueBlock = new TextBlock
                {
                    Text = value,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                };

                Grid.SetColumn(labelBlock, 0);
                Grid.SetColumn(valueBlock, 1);

                row.Children.Add(labelBlock);
                row.Children.Add(valueBlock);
                detailsPanel.Children.Add(row);
            }

            // Add device information
            AddInfoRow("Device Name", device.user_device_name);
            AddInfoRow("Model Name", device.model_name);
            AddInfoRow("Model Number", device.model_number);
            AddInfoRow("Serial Number", device.serial_number);
            AddInfoRow("Vendor", device.vendor_name);
            AddInfoRow("Software Version", device.software_version);
            AddInfoRow("Software Build", device.software_build);
            AddInfoRow("Device ID", device.device_id);
            AddInfoRow("Network Type", device.network_type);
            AddInfoRow("WiFi MAC", device.wifi_mac);
            AddInfoRow("Ethernet MAC", device.ethernet_mac);
            AddInfoRow("IP Address", device.Endpoint?.Replace("http://", "").Replace(":8060", ""));
            AddInfoRow("Model Region", device.model_region);
            AddInfoRow("Language", device.language);
            AddInfoRow("Country", device.country);
            AddInfoRow("Locale", device.locale);
            AddInfoRow("Time Zone", device.time_zone);
            AddInfoRow("Time Zone Offset", device.time_zone_offset);
            AddInfoRow("Power Mode", device.power_mode);
            AddInfoRow("Secure Device", device.secure_device == "true" ? "Yes" : "No");
            AddInfoRow("Developer Mode", device.developer_enabled == "true" ? "Enabled" : "Disabled");
            AddInfoRow("Supports Ethernet", device.supports_ethernet == "true" ? "Yes" : "No");
            AddInfoRow("Supports Suspend", device.supports_suspend == "true" ? "Yes" : "No");
            AddInfoRow("Supports Find Remote", device.supports_find_remote == "true" ? "Yes" : "No");
            AddInfoRow("Supports Audio Guide", device.supports_audio_guide == "true" ? "Yes" : "No");
            AddInfoRow("UDN", device.udn);

            scrollViewer.Content = detailsPanel;
            dialogContent.Children.Add(scrollViewer);

            // Close button
            var closeButton = new Button
            {
                Content = "Close",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Padding = new Avalonia.Thickness(30, 10),
                Background = Avalonia.Media.Brushes.DodgerBlue,
                Foreground = Avalonia.Media.Brushes.White,
                CornerRadius = new Avalonia.CornerRadius(5)
            };

            closeButton.Click += (s, e) => DialogHost.Close("MainDialogHost");

            dialogContent.Children.Add(closeButton);

            // Create the final user control
            var userControl = new UserControl
            {
                Content = dialogContent
            };

            return userControl;
        }

        // Direct keyboard functionality
        private void ShowDirectKeyboard()
        {
            if (_directKeyboard != null) return;

            _directKeyboard = new RokuKeyboard
            {
                Title = "Direct Keyboard Input - TV",
                ShowOkCancel = false
            };

            _directKeyboard.KeyPressed += async (s, key) =>
            {
                await SendKeyboardInput(key);
            };

            ShowInstalledApps = false;
            ShowQuickLaunchConfig = false;
            ShowKeyboard = true;

            var appsGrid = this.FindControl<Grid>("InstalledAppsView")?.Parent as Grid;
            if (appsGrid != null)
            {
                var installedAppsView = this.FindControl<Grid>("InstalledAppsView");
                var quickLaunchConfigView = this.FindControl<StackPanel>("QuickLaunchConfigView");
                
                if (installedAppsView != null) installedAppsView.IsVisible = false;
                if (quickLaunchConfigView != null) quickLaunchConfigView.IsVisible = false;

                appsGrid.Children.Add(_directKeyboard);
            }
        }

        private void HideDirectKeyboard()
        {
            if (_directKeyboard == null) return;

            var appsGrid = this.FindControl<Grid>("InstalledAppsView")?.Parent as Grid;
            if (appsGrid != null)
            {
                appsGrid.Children.Remove(_directKeyboard);
                
                var installedAppsView = this.FindControl<Grid>("InstalledAppsView");
                if (installedAppsView != null) installedAppsView.IsVisible = true;
            }

            _directKeyboard.KeyPressed -= async (s, key) => await SendKeyboardInput(key);
            _directKeyboard = null;

            ShowKeyboard = false;
            ShowInstalledApps = true;
        }

        private async Task SendKeyboardInput(string key)
        {
            if (SelectedDevice?.Endpoint == null)
            {
                StatusMessage = "No device selected";
                return;
            }

            try
            {
                string rokuKey = key switch
                {
                    "Backspace" => "Backspace",
                    "Enter" => "Enter", 
                    "Clear" => "Backspace",
                    _ => key
                };

                if (rokuKey == "Backspace" || rokuKey == "Enter")
                {
                    await RokuClient.PressKey(SelectedDevice.Endpoint, rokuKey);
                }
                else
                {
                    await RokuClient.SendText(SelectedDevice.Endpoint, key);
                }
                
                StatusMessage = $"Sent: {key}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error sending key: {ex.Message}";
            }
        }

        // Device discovery and management
        public async void OnDiscoverDevices(object? sender, RoutedEventArgs e)
        {
            try
            {
                StatusMessage = "Discovering Roku devices...";
                DiscoveredDevices.Clear();
                
                await RokuClient.Discover(device =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        DiscoveredDevices.Add(device);
                        StatusMessage = $"Found {DiscoveredDevices.Count} Roku device(s)";
                    });
                });
                
                if (DiscoveredDevices.Count == 0)
                {
                    StatusMessage = "No Roku devices found on the network";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error discovering devices: {ex.Message}";
            }
        }

        // FIXED LoadDeviceApps method
        private async void LoadDeviceApps()
        {
            if (SelectedDevice?.Endpoint == null) return;
            
            try
            {
                StatusMessage = "Loading apps...";
                var apps = await RokuClient.ListDeviceApps(SelectedDevice.Endpoint);
                
                // Clear and populate on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DeviceApps.Clear();
                    foreach (var app in apps)
                    {
                        app.Endpoint = SelectedDevice.Endpoint;
                        DeviceApps.Add(app);
                    }
                });
                
                StatusMessage = $"Loaded {apps.Count()} apps, downloading icons...";
                
                // Download icons in background
                var iconTasks = apps.Select(async app =>
                {
                    try
                    {
                        var iconBitmap = await DownloadAppIcon(app.IconUrl);
                        if (iconBitmap != null)
                        {
                            // Update on UI thread - the IconImage property change notification will update UI
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                app.IconImage = iconBitmap;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to download icon for {app.Name}: {ex.Message}");
                    }
                });
                
                // Wait for all icon downloads to complete (or timeout)
                await Task.WhenAll(iconTasks);
                
                StatusMessage = $"Loaded {apps.Count()} apps with icons";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading apps: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadDeviceApps Error: {ex.Message}");
            }
        }

        private async Task<Bitmap?> DownloadAppIcon(string iconUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(iconUrl))
                    return null;

                var response = await _httpClient.GetAsync(iconUrl);
                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    using var stream = new MemoryStream(imageBytes);
                    return new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading icon from {iconUrl}: {ex.Message}");
            }
            
            return null;
        }

        // Configuration Event Handlers
        public void OnConfigureQuickLaunch(object? sender, RoutedEventArgs e)
        {
            ShowInstalledApps = false;
            ShowQuickLaunchConfig = true;
            KeyboardEnabled = false;
            StatusMessage = "Configuring Quick Launch buttons...";
        }

        public void OnSaveQuickLaunchConfig(object? sender, RoutedEventArgs e)
        {
            SaveQuickLaunchConfiguration();
            ShowInstalledApps = true;
            ShowQuickLaunchConfig = false;
            StatusMessage = "Quick Launch configuration saved";
        }

        public void OnCancelQuickLaunchConfig(object? sender, RoutedEventArgs e)
        {
            ShowInstalledApps = true;
            ShowQuickLaunchConfig = false;
            StatusMessage = "Quick Launch configuration cancelled";
        }

        private void SaveQuickLaunchConfiguration()
        {
            var button1TextBox = this.FindControl<TextBox>("QuickLaunch1TextTB");
            var button2TextBox = this.FindControl<TextBox>("QuickLaunch2TextTB");
            var button3TextBox = this.FindControl<TextBox>("QuickLaunch3TextTB");
            var button4TextBox = this.FindControl<TextBox>("QuickLaunch4TextTB");

            var button1EnabledCB = this.FindControl<CheckBox>("QuickLaunch1EnabledCB");
            var button2EnabledCB = this.FindControl<CheckBox>("QuickLaunch2EnabledCB");
            var button3EnabledCB = this.FindControl<CheckBox>("QuickLaunch3EnabledCB");
            var button4EnabledCB = this.FindControl<CheckBox>("QuickLaunch4EnabledCB");

            if (button1TextBox != null) QuickLaunchButton1Text = button1TextBox.Text ?? "Q1";
            if (button2TextBox != null) QuickLaunchButton2Text = button2TextBox.Text ?? "Q2";
            if (button3TextBox != null) QuickLaunchButton3Text = button3TextBox.Text ?? "Q3";
            if (button4TextBox != null) QuickLaunchButton4Text = button4TextBox.Text ?? "Q4";

            QuickLaunchButton1Visible = button1EnabledCB?.IsChecked ?? true;
            QuickLaunchButton2Visible = button2EnabledCB?.IsChecked ?? true;
            QuickLaunchButton3Visible = button3EnabledCB?.IsChecked ?? false;
            QuickLaunchButton4Visible = button4EnabledCB?.IsChecked ?? false;
        }

        private async Task SendKey(string keyName)
        {
            if (SelectedDevice?.Endpoint == null)
            {
                StatusMessage = "No device selected";
                return;
            }

            try
            {
                await RokuClient.PressKey(SelectedDevice.Endpoint, keyName);
                StatusMessage = $"Sent {keyName} command";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error sending {keyName}: {ex.Message}";
            }
        }

        // Properties to enable/disable review buttons
        public bool QuickLaunchButton1HasActions => _quickLaunchSequence1.Actions.Count > 0;
        public bool QuickLaunchButton2HasActions => _quickLaunchSequence2.Actions.Count > 0;
        public bool QuickLaunchButton3HasActions => _quickLaunchSequence3.Actions.Count > 0;
        public bool QuickLaunchButton4HasActions => _quickLaunchSequence4.Actions.Count > 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Review Event Handlers
        public async void OnReviewButton1(object? sender, RoutedEventArgs e) => await ShowReviewDialog(1, _quickLaunchSequence1);
        public async void OnReviewButton2(object? sender, RoutedEventArgs e) => await ShowReviewDialog(2, _quickLaunchSequence2);
        public async void OnReviewButton3(object? sender, RoutedEventArgs e) => await ShowReviewDialog(3, _quickLaunchSequence3);
        public async void OnReviewButton4(object? sender, RoutedEventArgs e) => await ShowReviewDialog(4, _quickLaunchSequence4);

        private async Task ShowReviewDialog(int buttonNumber, QuickLaunchSequence sequence)
        {
            var reviewDialog = new Controls.ActionReviewDialog();
            reviewDialog.LoadSequence(sequence);

            reviewDialog.SaveChanges += (s, e) =>
            {
                var modifiedActions = reviewDialog.GetModifiedActions();
                sequence.Actions.Clear();
                sequence.Actions.AddRange(modifiedActions);
                
                // Update summary displays
                OnPropertyChanged(nameof(QuickLaunchButton1Summary));
                OnPropertyChanged(nameof(QuickLaunchButton2Summary));
                OnPropertyChanged(nameof(QuickLaunchButton3Summary));
                OnPropertyChanged(nameof(QuickLaunchButton4Summary));
                OnPropertyChanged(nameof(QuickLaunchButton1HasActions));
                OnPropertyChanged(nameof(QuickLaunchButton2HasActions));
                OnPropertyChanged(nameof(QuickLaunchButton3HasActions));
                OnPropertyChanged(nameof(QuickLaunchButton4HasActions));
                
                DialogHost.Close("MainDialogHost", true);
                StatusMessage = $"Actions updated for Button {buttonNumber}";
            };

            reviewDialog.CancelChanges += (s, e) =>
            {
                DialogHost.Close("MainDialogHost", false);
                StatusMessage = "Review cancelled";
            };

            reviewDialog.TestSequence += async (s, e) =>
            {
                try
                {
                    StatusMessage = "Testing sequence...";
                    await ExecuteSequence(sequence);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Test failed: {ex.Message}";
                }
            };

            reviewDialog.ClearAll += (s, e) =>
            {
                StatusMessage = $"All actions cleared for Button {buttonNumber}";
            };

            await DialogHost.Show(reviewDialog, "MainDialogHost");
        }

        // Mic button DOWN -> open socket + start streaming
        private async void OnMicrophonePressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (SelectedDevice?.Endpoint is null)
            {
                StatusMessage = "No device selected";
                return;
            }

            try
            {
                var endpoint = SelectedDevice.Endpoint;
                var host = new Uri(endpoint).Host;
                var wsUri = new Uri($"ws://{host}:8000");

                // 1) Send Search keypress FIRST so the TV opens the audio stream port
                StatusMessage = "Enabling voice mode...";
                bool ok = await RRemote.Roku.RokuClient.StartVoiceSearch(endpoint);
                if (!ok)
                {
                    StatusMessage = "Roku did not accept the Search command";
                    return;
                }

                // 2) Only now look for the audio stream port and start streaming
                StatusMessage = "🎤 Listening...";
                await _voiceStreamer.StartAsync(wsUri);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Voice error: {ex.Message}";
                System.Diagnostics.Debug.Write(StatusMessage);
            }
        }

        // Mic button UP -> stop streaming + close socket
        private async void OnMicrophoneReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            try
            {
                await _voiceStreamer.StopAsync();
                StatusMessage = "Voice transmission ended";
                Debug.Write(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Voice error: {ex.Message}";
                Debug.Write(StatusMessage);
            }
        }
        
    }
}