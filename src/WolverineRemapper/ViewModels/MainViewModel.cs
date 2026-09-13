using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WolverineRemapper.Models;
using WolverineRemapper.Services;

namespace WolverineRemapper.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private const string ViGEmInstallHint = "Install the ViGEmBus driver from https://github.com/nefarius/ViGEmBus/releases and restart the app.";

        private readonly XInputService _xinput = new();
        private readonly ProfileService _profiles = new();
        private readonly RemapperEngine _engine;
        private readonly DispatcherTimer _pollTimer;
        private readonly List<MButtonConfig> _subscribedConfigs = new();

        private AppSettings _settings;
        private int _monitorSlot = -1;
        private int _batteryCooldown;

        public MainViewModel()
        {
            _engine = new RemapperEngine(_xinput);
            _engine.Log += AddLog;
            _engine.KeyCaptured += OnKeyCaptured;
            _engine.CaptureEnded += OnCaptureEnded;

            ToggleRemapperCommand = new RelayCommand(_ => ToggleRemapper());
            SaveProfileCommand = new RelayCommand(_ => SaveProfile());
            LoadProfileCommand = new RelayCommand(_ => LoadProfile());
            DeleteProfileCommand = new RelayCommand(_ => DeleteProfile());
            NewProfileCommand = new RelayCommand(_ => NewProfile());
            DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile());
            CheckUpdatesCommand = new RelayCommand(_ => _ = CheckForUpdatesAsync(silent: false));
            OpenUpdateCommand = new RelayCommand(_ => OpenUrl(_updateUrl ?? UpdateService.RepoUrl));
            OpenGitHubCommand = new RelayCommand(_ => OpenUrl(UpdateService.RepoUrl));
            OpenViGEmPageCommand = new RelayCommand(_ => OpenUrl(DriverCheck.ViGEmBusUrl));
            OpenHidHidePageCommand = new RelayCommand(_ => OpenUrl(DriverCheck.HidHideUrl));
            RefreshDriversCommand = new RelayCommand(_ => RefreshDriverStatus());
            SelectMButtonCommand = new RelayCommand(SelectMButton);
            RebindCommand = new RelayCommand(BeginRebind);
            CancelCaptureCommand = new RelayCommand(_ => _engine.CancelCapture());
            ClearLogsCommand = new RelayCommand(_ => ActivityLogs.Clear());
            AddStepCommand = new RelayCommand(AddStep);
            CaptureStickCommand = new RelayCommand(CaptureStickPosition);
            ResetOverlayPositionCommand = new RelayCommand(_ => ResetOverlayPosition());
            ClearStepsCommand = new RelayCommand(_ => SelectedMButton?.Steps.Clear());
            ShowGuideCommand = new RelayCommand(_ => ShowGuide());
            CloseGuideCommand = new RelayCommand(_ => CloseGuide());
            GuideNextCommand = new RelayCommand(_ => GuidePageIndex = Math.Min(GuidePageCount - 1, GuidePageIndex + 1));
            GuideBackCommand = new RelayCommand(_ => GuidePageIndex = Math.Max(0, GuidePageIndex - 1));
            RecordMacroCommand = new RelayCommand(_ => _ = RecordMacroAsync());
            StopRecordingCommand = new RelayCommand(_ => FinishRecording(cancel: false));
            CancelRecordingCommand = new RelayCommand(_ => FinishRecording(cancel: true));
            CancelStickCaptureCommand = new RelayCommand(_ => _stickCaptureCancelled = true);
            RemoveStepCommand = new RelayCommand(p => { if (p is MacroStep s) SelectedMButton?.Steps.Remove(s); });
            MoveStepUpCommand = new RelayCommand(p => MoveStep(p, -1));
            MoveStepDownCommand = new RelayCommand(p => MoveStep(p, +1));

            MButtons.CollectionChanged += OnMButtonsCollectionChanged;

            _settings = _profiles.LoadSettings();
            L10n.I.CurrentLanguage = L10n.FromCode(_settings.Language);
            L10n.I.LanguageChanged += OnLanguageChanged;
            RefreshProfileNames();

            var last = _settings.LastProfileName != null ? _profiles.Load(_settings.LastProfileName) : null;
            if (last != null)
            {
                ApplyProfile(last);
                SetSelectedProfileSilent(last.ProfileName);
                _loadedProfileName = last.ProfileName;
                AddLog($"[+] Auto-loaded profile '{last.ProfileName}'.");
            }
            else
            {
                LoadDefaults();
                AddLog("[+] Loaded default Throne & Liberty mapping.");
            }

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ~60 FPS
            _pollTimer.Tick += PollGamepad;
            _pollTimer.Start();

            if (_settings.CheckUpdatesAtStartup) _ = CheckForUpdatesAsync(silent: true);
        }

        #region Collections

        public ObservableCollection<MButtonConfig> MButtons { get; } = new();
        public ObservableCollection<string> ActivityLogs { get; } = new();
        public ObservableCollection<string> ProfileNames { get; } = new();

        // Named accessors so fixed diagram badges can bind to "their" M-button.
        public MButtonConfig? M1 => FindButton("M1");
        public MButtonConfig? M2 => FindButton("M2");
        public MButtonConfig? M3 => FindButton("M3");
        public MButtonConfig? M4 => FindButton("M4");
        public MButtonConfig? M5 => FindButton("M5");
        public MButtonConfig? M6 => FindButton("M6");

        private MButtonConfig? FindButton(string name) =>
            MButtons.FirstOrDefault(m => m.MButtonName.Equals(name, StringComparison.OrdinalIgnoreCase));

        #endregion

        #region Engine state

        public bool IsRemapperActive => _engine.IsRunning;
        public string StatusText => IsRemapperActive ? L10n.I.T("engine_online") : L10n.I.T("engine_stopped");
        public string StatusColor => IsRemapperActive ? "#10B981" : "#F43F5E";
        public string EngineButtonText => IsRemapperActive ? "◼  " + L10n.I.T("btn_stop_engine") : "▶  " + L10n.I.T("btn_start_engine");

        private string _statusMessage = L10n.I.T("status_ready");
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        #region Language

        public IReadOnlyList<Language> Languages => L10n.Available;

        public Language CurrentLanguage
        {
            get => L10n.I.CurrentLanguage;
            set
            {
                if (value == null || value.Code == L10n.I.CurrentLanguage.Code) return;
                L10n.I.CurrentLanguage = value;
                _settings.Language = value.Code;
                _profiles.SaveSettings(_settings);
                OnPropertyChanged();
            }
        }

        /// <summary>Strings computed in code (not bound through the indexer) need a nudge.</summary>
        private void OnLanguageChanged()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(EngineButtonText));
            OnPropertyChanged(nameof(SlotInfoText));
            OnPropertyChanged(nameof(SlotInfoTooltip));
            OnPropertyChanged(nameof(PadTriggerHint));
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged(nameof(UpdateStatusText));
            OnPropertyChanged(nameof(ViGEmStatusText));
            OnPropertyChanged(nameof(HidHideStatusText));
            NotifyGuidePage();
        }

        #endregion

        #region Settings tab

        private void SaveSettings() => _profiles.SaveSettings(_settings);

        /// <summary>Per-user Run key; shared with the installer and the tray toggle.</summary>
        public bool StartWithWindows
        {
            get => StartupService.IsEnabled();
            set
            {
                StartupService.SetEnabled(value, _settings.StartMinimized);
                OnPropertyChanged();
                AddLog(value ? "[+] Start with Windows enabled." : "[−] Start with Windows disabled.");
            }
        }

        public bool StartMinimized
        {
            get => _settings.StartMinimized;
            set
            {
                _settings.StartMinimized = value;
                SaveSettings();
                if (StartWithWindows) StartupService.SetEnabled(true, value); // rewrite the Run command line
                OnPropertyChanged();
            }
        }

        public bool AutoStartEngine
        {
            get => _settings.AutoStartEngine;
            set { _settings.AutoStartEngine = value; SaveSettings(); OnPropertyChanged(); }
        }

        public bool CloseToTray
        {
            get => _settings.CloseToTray;
            set { _settings.CloseToTray = value; SaveSettings(); OnPropertyChanged(); }
        }

        public bool CheckUpdatesAtStartup
        {
            get => _settings.CheckUpdatesAtStartup;
            set { _settings.CheckUpdatesAtStartup = value; SaveSettings(); OnPropertyChanged(); }
        }

        public string AppVersion => "v" + UpdateService.Current;

        // Update check state is kept as data so the text re-localizes on language change.
        private enum UpdateState { Unknown, Checking, UpToDate, Available, Failed }
        private UpdateState _updateState = UpdateState.Unknown;
        private Version? _latestVersion;
        private string? _updateUrl;
        private string _updateError = "";

        public bool UpdateAvailable => _updateState == UpdateState.Available;

        public string UpdateStatusText => _updateState switch
        {
            UpdateState.Checking => L10n.I.T("update_checking"),
            UpdateState.UpToDate => L10n.I.F("update_latest", AppVersion),
            UpdateState.Available => L10n.I.F("update_available", "v" + _latestVersion, AppVersion),
            UpdateState.Failed => L10n.I.F("update_failed", _updateError),
            _ => L10n.I.T("update_unknown")
        };

        private async Task CheckForUpdatesAsync(bool silent)
        {
            _updateState = UpdateState.Checking;
            NotifyUpdate();
            try
            {
                var info = await UpdateService.GetLatestAsync();
                if (info == null)
                {
                    _updateState = UpdateState.Failed;
                    _updateError = "no version tag";
                }
                else
                {
                    _latestVersion = info.Latest;
                    _updateUrl = info.Url;
                    _updateState = info.Latest > UpdateService.Current ? UpdateState.Available : UpdateState.UpToDate;
                    if (_updateState == UpdateState.Available)
                    {
                        AddLog($"[i] Update available: v{info.Latest} (running {AppVersion}) — {info.Url}");
                        if (silent) StatusMessage = UpdateStatusText;
                    }
                }
            }
            catch (Exception ex)
            {
                _updateState = UpdateState.Failed;
                _updateError = ex.Message;
                if (!silent) AddLog($"[!] Update check failed: {ex.Message}");
            }
            NotifyUpdate();
        }

        private void NotifyUpdate()
        {
            OnPropertyChanged(nameof(UpdateStatusText));
            OnPropertyChanged(nameof(UpdateAvailable));
        }

        public string ViGEmStatusText => DriverCheck.ViGEmBusInstalled ? "✓ " + L10n.I.T("driver_installed") : "✕ " + L10n.I.T("driver_missing");
        public string HidHideStatusText => DriverCheck.HidHideInstalled ? "✓ " + L10n.I.T("driver_installed") : "✕ " + L10n.I.T("driver_missing");

        private void RefreshDriverStatus()
        {
            OnPropertyChanged(nameof(ViGEmStatusText));
            OnPropertyChanged(nameof(HidHideStatusText));
        }

        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* no browser association — nothing sensible to do */ }
        }

        #endregion

        public bool PassthroughEnabled
        {
            get => _engine.PassthroughEnabled;
            set
            {
                _engine.PassthroughEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowPadModeWarning));
                MarkDirty();
                AddLog(value
                    ? "[+] Passthrough ON — physical pad is mirrored into the virtual pad (use the virtual pad in-game)."
                    : "[−] Passthrough OFF — virtual pad emits chords only.");
            }
        }

        private void ToggleRemapper()
        {
            if (IsRemapperActive)
            {
                _engine.Stop();
                StatusMessage = L10n.I.T("status_engine_stopped");
                AddLog("[−] Engine stopped.");
            }
            else
            {
                _engine.SetMappings(MButtons);
                if (_engine.Start(out var error))
                {
                    StatusMessage = PassthroughEnabled
                        ? L10n.I.T("status_engine_online_passthrough")
                        : L10n.I.T("status_engine_online");
                    AddLog("[+] Engine ONLINE. Virtual Xbox 360 pad connected" +
                           (_engine.VirtualSlot >= 0 ? $" (XInput slot {_engine.VirtualSlot + 1})." : "."));
                }
                else
                {
                    StatusMessage = L10n.I.F("status_start_failed", error) + " " + L10n.I.T("hint_install_vigem");
                    AddLog($"[!] {error}");
                    AddLog($"[!] {ViGEmInstallHint}");
                }
            }

            OnPropertyChanged(nameof(IsRemapperActive));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(EngineButtonText));
            OnPropertyChanged(nameof(SlotInfoText)); OnPropertyChanged(nameof(SlotInfoTooltip));
        }

        #endregion

        #region Controller model (top-left selector)

        public sealed class ControllerModelChoice
        {
            public ControllerModel Model { get; init; }
            public string Name { get; init; } = "";
            public override string ToString() => Name;
        }

        public IReadOnlyList<ControllerModelChoice> ControllerModels { get; } = new[]
        {
            new ControllerModelChoice { Model = ControllerModel.WolverineV3Pro8K, Name = "WOLVERINE V3 PRO 8K" },
            new ControllerModelChoice { Model = ControllerModel.WolverineV2,      Name = "WOLVERINE V2 / V2 CHROMA / V2 PRO" },
        };

        private ControllerModelChoice? _selectedControllerModel;
        public ControllerModelChoice SelectedControllerModel
        {
            get => _selectedControllerModel ??= ControllerModels[0];
            set
            {
                if (value == null || ReferenceEquals(_selectedControllerModel, value)) return;
                _selectedControllerModel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPadTriggerMode));
                OnPropertyChanged(nameof(ShowPadModeWarning));

                if (_suppressDirty) return; // profile load: configs already carry their source

                ApplyControllerModelToButtons(assignDefaultSacrifices: true);
                _engine.SetMappings(MButtons);
                MarkDirty();

                if (IsPadTriggerMode)
                {
                    AddLog("[+] Controller set to Wolverine V2 family — each M-button now triggers on a sacrificed pad button.");
                    AddLog("[i] In Razer Controller Setup for Xbox, map each paddle to the button chosen here. Turn Passthrough ON and hide the physical pad from the game with HidHide.");
                    StatusMessage = PassthroughEnabled
                        ? L10n.I.T("status_v2_pick")
                        : L10n.I.T("status_v2_passthrough_off");
                }
                else
                {
                    AddLog("[+] Controller set to Wolverine V3 Pro 8K — M-buttons trigger on keyboard keys from Synapse.");
                    StatusMessage = L10n.I.T("status_v3");
                }
            }
        }

        public bool IsPadTriggerMode => SelectedControllerModel.Model == ControllerModel.WolverineV2;

        /// <summary>Pad-button mode without passthrough leaks the raw press to the game.</summary>
        public bool ShowPadModeWarning => IsPadTriggerMode && !PassthroughEnabled;

        public IReadOnlyList<PadButtonChoice> PadTriggerChoices => PadButtons.Choices;

        public string PadTriggerHint => L10n.I.T("hint_pad_trigger");

        private void SetControllerModelSilent(ControllerModel model)
        {
            var choice = ControllerModels.First(c => c.Model == model);
            _selectedControllerModel = choice;
            OnPropertyChanged(nameof(SelectedControllerModel));
            OnPropertyChanged(nameof(IsPadTriggerMode));
            OnPropertyChanged(nameof(ShowPadModeWarning));
        }

        /// <summary>
        /// Push the selected model down to every M-button's trigger source.
        /// On a fresh switch to pad mode, hand out distinct default sacrifices
        /// (View, Menu, LS, RS, D-Left, D-Right) so nothing collides.
        /// </summary>
        private void ApplyControllerModelToButtons(bool assignDefaultSacrifices)
        {
            var source = IsPadTriggerMode ? TriggerSource.PadButton : TriggerSource.Keyboard;

            bool fresh = assignDefaultSacrifices && IsPadTriggerMode
                         && MButtons.Select(m => m.PadTriggerButton).Distinct().Count() <= 1;

            for (int i = 0; i < MButtons.Count; i++)
            {
                var cfg = MButtons[i];
                cfg.Source = source;
                if (fresh && i < PadButtons.DefaultSacrificeOrder.Length)
                    cfg.PadTriggerButton = PadButtons.DefaultSacrificeOrder[i];
            }
        }

        #endregion

        #region M-button selection & key capture

        private MButtonConfig? _selectedMButton;
        public MButtonConfig? SelectedMButton
        {
            get => _selectedMButton;
            set
            {
                if (_selectedMButton != null) _selectedMButton.IsSelected = false;
                _selectedMButton = value;
                if (_selectedMButton != null) _selectedMButton.IsSelected = true;
                OnPropertyChanged();
            }
        }

        public bool IsCapturing => _engine.IsCapturing;

        private string _capturingButtonName = "";
        public string CapturingButtonName
        {
            get => _capturingButtonName;
            set { _capturingButtonName = value; OnPropertyChanged(); }
        }

        private void SelectMButton(object? parameter)
        {
            var config = parameter as MButtonConfig
                         ?? (parameter is string name ? FindButton(name) : null);
            if (config != null) SelectedMButton = config;
        }

        private void BeginRebind(object? parameter)
        {
            var config = parameter as MButtonConfig ?? SelectedMButton;
            if (config == null) return;

            SelectedMButton = config;
            CapturingButtonName = config.MButtonName;

            if (_engine.BeginCapture(config, out var error))
            {
                OnPropertyChanged(nameof(IsCapturing));
                AddLog($"[Rebind] Press any key to assign to {config.MButtonName} (Esc cancels)…");
            }
            else
            {
                AddLog($"[!] Could not start key capture: {error}");
            }
        }

        private void OnKeyCaptured(MButtonConfig config)
        {
            AddLog($"[Rebind] {config.MButtonName} is now triggered by '{config.KeyDisplayName}'.");
            _engine.SetMappings(MButtons);
        }

        private void OnCaptureEnded()
        {
            OnPropertyChanged(nameof(IsCapturing));
        }

        #endregion

        #region Macro step editor

        /// <summary>Item sources for the step editor's combo boxes.</summary>
        public Array TriggerTypes { get; } = Enum.GetValues(typeof(TriggerType));
        public Array RepeatModes { get; } = Enum.GetValues(typeof(RepeatMode));
        public Array MacroStepTypes { get; } = Enum.GetValues(typeof(MacroStepType));
        public Array VirtualButtons { get; } = Enum.GetValues(typeof(VirtualButtonId));
        public Array StickSides { get; } = Enum.GetValues(typeof(StickSide));

        private bool _isCapturingStick;
        public bool IsCapturingStick
        {
            get => _isCapturingStick;
            set { _isCapturingStick = value; OnPropertyChanged(); }
        }

        private string _stickCaptureTitle = "";
        public string StickCaptureTitle
        {
            get => _stickCaptureTitle;
            set { _stickCaptureTitle = value; OnPropertyChanged(); }
        }

        private string _stickCaptureLive = "";
        public string StickCaptureLive
        {
            get => _stickCaptureLive;
            set { _stickCaptureLive = value; OnPropertyChanged(); }
        }

        private string _stickCaptureBest = "";
        public string StickCaptureBest
        {
            get => _stickCaptureBest;
            set { _stickCaptureBest = value; OnPropertyChanged(); }
        }

        private double _stickCaptureProgress;
        public double StickCaptureProgress
        {
            get => _stickCaptureProgress;
            set { _stickCaptureProgress = value; OnPropertyChanged(); }
        }

        private bool _stickCaptureCancelled;

        /// <summary>
        /// Record a stick position from the physical pad with a full-screen
        /// overlay: live direction readout, progress bar, strongest push wins.
        /// </summary>
        private async void CaptureStickPosition(object? parameter)
        {
            if (parameter is not MacroStep step || IsCapturingStick) return;
            if (!ControllerConnected)
            {
                AddLog("[Capture] No physical controller detected — connect the pad first.");
                StatusMessage = "Connect your controller before recording a stick position.";
                return;
            }

            _stickCaptureCancelled = false;
            StickCaptureTitle = $"RECORDING {step.Stick.ToString().ToUpperInvariant()} STICK";
            StickCaptureLive = "centered";
            StickCaptureBest = "";
            StickCaptureProgress = 0;
            IsCapturingStick = true;

            try
            {
                double bestX = 0, bestY = 0, bestMag = -1;
                const int ticks = 50; // 50 × 50 ms = 2.5 s window
                for (int i = 0; i < ticks; i++)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                    if (_stickCaptureCancelled)
                    {
                        AddLog("[Capture] Cancelled — position unchanged.");
                        return;
                    }

                    double x = step.Stick == StickSide.Left ? LeftStickXNorm : RightStickXNorm;
                    double y = step.Stick == StickSide.Left ? LeftStickYNorm : RightStickYNorm;
                    double mag = Math.Sqrt(x * x + y * y);

                    StickCaptureLive = FormatStickDirection(x, y);
                    if (mag > bestMag)
                    {
                        bestMag = mag; bestX = x; bestY = y;
                        if (bestMag >= 0.05)
                            StickCaptureBest = $"Best so far: {FormatStickDirection(bestX, bestY)}";
                    }
                    StickCaptureProgress = (i + 1) * 100.0 / ticks;
                }

                step.StickX = Math.Round(bestX, 3);
                step.StickY = Math.Round(bestY, 3);
                if (bestMag < 0.01)
                {
                    AddLog("[Capture] No stick movement detected — position left centered.");
                    StatusMessage = "No stick movement detected during capture. Try again and push the stick.";
                }
                else
                {
                    AddLog($"[Capture] Saved {step.Stick} stick position: {step.StickSummary}");
                    StatusMessage = $"Stick position saved: {step.StickSummary}";
                }
            }
            finally
            {
                IsCapturingStick = false;
            }
        }

        #region Overlay (see-through live controller)

        /// <summary>Raised when the user asks to park the overlay back at its default spot.</summary>
        public event Action? OverlayResetRequested;

        public bool OverlayEnabled
        {
            get => _settings.OverlayVisible;
            set
            {
                if (_settings.OverlayVisible == value) return;
                _settings.OverlayVisible = value;
                SaveSettings();
                OnPropertyChanged();
                AddLog(value ? "[+] Overlay shown — drag it into place, right-click to lock." : "[−] Overlay hidden.");
            }
        }

        public bool OverlayLocked
        {
            get => _settings.OverlayLocked;
            set
            {
                if (_settings.OverlayLocked == value) return;
                _settings.OverlayLocked = value;
                SaveSettings();
                OnPropertyChanged();
                AddLog(value ? "[+] Overlay locked (click-through). Unlock from the tray or Settings." : "[−] Overlay unlocked — drag to move, right-click to lock.");
            }
        }

        public double OverlayScale
        {
            get => _settings.OverlayScale;
            set { _settings.OverlayScale = Math.Clamp(value, 0.5, 1.6); SaveSettings(); OnPropertyChanged(); }
        }

        public double OverlayOpacity
        {
            get => _settings.OverlayOpacity;
            set { _settings.OverlayOpacity = Math.Clamp(value, 0.2, 1.0); SaveSettings(); OnPropertyChanged(); }
        }

        public double OverlayX => _settings.OverlayX;
        public double OverlayY => _settings.OverlayY;

        public void SaveOverlayPosition(double x, double y)
        {
            _settings.OverlayX = x;
            _settings.OverlayY = y;
            SaveSettings();
        }

        public void ResetOverlayPosition() => OverlayResetRequested?.Invoke();

        #endregion

        #region First-run guide

        public const int GuidePageCount = 7;

        /// <summary>True until the guide has been shown once (installer/first launch).</summary>
        public bool TutorialSeen => _settings.TutorialSeen;

        private bool _isGuideOpen;
        public bool IsGuideOpen
        {
            get => _isGuideOpen;
            set { _isGuideOpen = value; OnPropertyChanged(); }
        }

        private int _guidePageIndex;
        public int GuidePageIndex
        {
            get => _guidePageIndex;
            set
            {
                _guidePageIndex = Math.Clamp(value, 0, GuidePageCount - 1);
                OnPropertyChanged();
                NotifyGuidePage();
            }
        }

        public string GuidePageTitle => L10n.I.T($"guide_p{GuidePageIndex + 1}_title");
        public string GuidePageBody => L10n.I.T($"guide_p{GuidePageIndex + 1}_body");
        public string GuidePageCounter => $"{GuidePageIndex + 1} / {GuidePageCount}";
        public bool GuideIsFirstPage => GuidePageIndex == 0;
        public bool GuideIsLastPage => GuidePageIndex == GuidePageCount - 1;
        public bool GuideIsNotLastPage => !GuideIsLastPage;

        private void NotifyGuidePage()
        {
            OnPropertyChanged(nameof(GuidePageTitle));
            OnPropertyChanged(nameof(GuidePageBody));
            OnPropertyChanged(nameof(GuidePageCounter));
            OnPropertyChanged(nameof(GuideIsFirstPage));
            OnPropertyChanged(nameof(GuideIsLastPage));
            OnPropertyChanged(nameof(GuideIsNotLastPage));
        }

        public void ShowGuide()
        {
            GuidePageIndex = 0;
            IsGuideOpen = true;
        }

        private void CloseGuide()
        {
            IsGuideOpen = false;
            if (!_settings.TutorialSeen)
            {
                _settings.TutorialSeen = true;
                SaveSettings();
            }
        }

        #endregion

        #region Macro recorder

        private readonly MacroRecorder _recorder = new();
        private MButtonConfig? _recordTarget;
        private bool _recordCountdownCancelled;

        private bool _isRecordingMacro;
        /// <summary>Overlay visible (countdown or recording).</summary>
        public bool IsRecordingMacro
        {
            get => _isRecordingMacro;
            set { _isRecordingMacro = value; OnPropertyChanged(); }
        }

        private bool _isRecordingLive;
        /// <summary>True once the countdown is over and samples are being captured.</summary>
        public bool IsRecordingLive
        {
            get => _isRecordingLive;
            set { _isRecordingLive = value; OnPropertyChanged(); }
        }

        private string _recordTitle = "";
        public string RecordTitle { get => _recordTitle; set { _recordTitle = value; OnPropertyChanged(); } }

        private string _recordBig = "";
        /// <summary>Countdown digit, then the elapsed time.</summary>
        public string RecordBig { get => _recordBig; set { _recordBig = value; OnPropertyChanged(); } }

        private string _recordDetail = "";
        public string RecordDetail { get => _recordDetail; set { _recordDetail = value; OnPropertyChanged(); } }

        // Recorder parameters (app-wide, persisted in settings).
        public int RecordTapThresholdMs
        {
            get => _settings.RecordTapThresholdMs;
            set { _settings.RecordTapThresholdMs = Math.Clamp(value, 50, 400); SaveSettings(); OnPropertyChanged(); }
        }
        public int RecordStickThresholdPercent
        {
            get => _settings.RecordStickThresholdPercent;
            set { _settings.RecordStickThresholdPercent = Math.Clamp(value, 20, 80); SaveSettings(); OnPropertyChanged(); }
        }
        public bool RecordKeepWaits
        {
            get => _settings.RecordKeepWaits;
            set { _settings.RecordKeepWaits = value; SaveSettings(); OnPropertyChanged(); }
        }
        public bool RecordHoldAtEnd
        {
            get => _settings.RecordHoldAtEnd;
            set { _settings.RecordHoldAtEnd = value; SaveSettings(); OnPropertyChanged(); }
        }

        private async Task RecordMacroAsync()
        {
            if (IsRecordingMacro || SelectedMButton == null) return;
            if (!ControllerConnected)
            {
                StatusMessage = L10n.I.T("record_no_pad");
                AddLog("[Record] No physical controller detected — connect the pad first.");
                return;
            }

            _recordTarget = SelectedMButton;
            _recordCountdownCancelled = false;
            RecordTitle = L10n.I.F("record_title", _recordTarget.MButtonName);
            RecordDetail = L10n.I.T("record_get_ready");
            IsRecordingLive = false;
            IsRecordingMacro = true;

            for (int n = 3; n >= 1; n--)
            {
                RecordBig = n.ToString();
                await Task.Delay(1000);
                if (_recordCountdownCancelled) return;
            }

            _recorder.Start(Environment.TickCount64, new RecorderOptions(
                _settings.RecordTapThresholdMs,
                _settings.RecordStickThresholdPercent / 100.0,
                _settings.RecordKeepWaits,
                _settings.RecordHoldAtEnd));
            RecordBig = "0.0 s";
            RecordDetail = L10n.I.T("record_now_play");
            IsRecordingLive = true;
            AddLog($"[Record] Recording {_recordTarget.MButtonName} from the pad…");
        }

        /// <summary>Called from the pad poll loop.</summary>
        private void RecorderTick(in XINPUT_GAMEPAD pad)
        {
            if (!_recorder.IsRecording) return;
            long now = Environment.TickCount64;
            _recorder.Sample(pad, now);
            RecordBig = $"{_recorder.ElapsedMs(now) / 1000.0:F1} s";
            RecordDetail = L10n.I.F("record_inputs", _recorder.EventCount);
        }

        private void FinishRecording(bool cancel)
        {
            if (!IsRecordingMacro) return;

            if (!_recorder.IsRecording)
            {
                // Still in the countdown.
                _recordCountdownCancelled = true;
                IsRecordingMacro = false;
                return;
            }

            var steps = _recorder.Stop();
            IsRecordingLive = false;
            IsRecordingMacro = false;

            if (cancel)
            {
                AddLog("[Record] Cancelled — steps unchanged.");
                return;
            }
            if (steps.Count == 0 || _recordTarget == null)
            {
                StatusMessage = L10n.I.T("record_nothing");
                AddLog("[Record] Nothing was pressed — steps unchanged.");
                return;
            }

            _recordTarget.Steps.Clear();
            foreach (var s in steps) _recordTarget.Steps.Add(s);
            if (_recordTarget.Mode != ActionMode.Sequence) _recordTarget.Mode = ActionMode.Sequence;

            StatusMessage = L10n.I.F("record_summary", steps.Count, _recordTarget.MButtonName);
            AddLog($"[Record] {_recordTarget.MButtonName}: {steps.Count} steps — {MacroRecorder.Describe(steps)}");
        }

        #endregion

        private static string FormatStickDirection(double x, double y)
        {
            double mag = Math.Sqrt(x * x + y * y);
            if (mag < 0.02) return "centered";
            double angle = Math.Atan2(y, x) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;
            return $"{angle:F0}°  ·  {Math.Min(mag, 1) * 100:F0}%";
        }

        private void AddStep(object? parameter)
        {
            if (SelectedMButton == null || parameter is not string typeName) return;
            if (!Enum.TryParse<MacroStepType>(typeName, out var type)) return;

            SelectedMButton.Steps.Add(new MacroStep
            {
                Type = type,
                Button = VirtualButtonId.A,
                DurationMs = type switch
                {
                    MacroStepType.Wait => 60,
                    MacroStepType.Tap => 40,
                    _ => 0
                }
            });
        }

        private void MoveStep(object? parameter, int delta)
        {
            if (parameter is not MacroStep step || SelectedMButton == null) return;
            var steps = SelectedMButton.Steps;
            int index = steps.IndexOf(step);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= steps.Count) return;
            steps.Move(index, target);
        }

        #endregion

        #region Profiles

        private string _profileNameInput = "Throne and Liberty";
        public string ProfileNameInput
        {
            get => _profileNameInput;
            set { _profileNameInput = value ?? ""; OnPropertyChanged(); }
        }

        // Selection in the profiles dropdown. Decoupled from ProfileNameInput:
        // clearing/refilling ProfileNames must never wipe the typed name, and
        // a user pick auto-loads that profile.
        private bool _syncingProfileSelection;
        private string? _loadedProfileName;
        private string? _selectedProfile;
        public string? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile == value) return;
                _selectedProfile = value;
                OnPropertyChanged();
                if (!_syncingProfileSelection && value != null)
                {
                    LoadProfileByName(value);
                }
            }
        }

        private void SetSelectedProfileSilent(string? name)
        {
            // Windows filenames are case-insensitive: the on-disk profile may
            // differ in casing from the typed name. Select the canonical list
            // entry so the ComboBox always finds its match.
            string? canonical = name == null
                ? null
                : ProfileNames.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

            _syncingProfileSelection = true;
            try { SelectedProfile = canonical; }
            finally { _syncingProfileSelection = false; }
        }

        // Transient "✓ saved" confirmation next to the profile buttons.
        private bool _showSavedFlash;
        public bool ShowSavedFlash
        {
            get => _showSavedFlash;
            set { if (_showSavedFlash != value) { _showSavedFlash = value; OnPropertyChanged(); } }
        }

        private int _savedFlashToken;
        private async void FlashSaved()
        {
            int token = ++_savedFlashToken;
            ShowSavedFlash = true;
            await System.Threading.Tasks.Task.Delay(2500);
            if (token == _savedFlashToken) ShowSavedFlash = false;
        }

        // Unsaved-changes tracking: mapping edits flip the flag; save/load/new
        // clear it. Live state (IsPressed/IsSelected) never counts.
        private bool _suppressDirty;
        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { if (_hasUnsavedChanges != value) { _hasUnsavedChanges = value; OnPropertyChanged(); } }
        }

        private void MarkDirty()
        {
            if (_suppressDirty) return;
            HasUnsavedChanges = true;
            ShowSavedFlash = false; // never show "✓ saved" and "● unsaved" together
        }

        private void RefreshProfileNames()
        {
            // Clearing the ItemsSource nulls the ComboBox selection through
            // the binding — suppress so that never triggers a load, and
            // restore the selection afterwards.
            _syncingProfileSelection = true;
            try
            {
                string? current = SelectedProfile;
                ProfileNames.Clear();
                foreach (var name in _profiles.ListProfiles()) ProfileNames.Add(name);
                SelectedProfile = current == null
                    ? null
                    : ProfileNames.FirstOrDefault(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _syncingProfileSelection = false;
            }
        }

        private void SaveProfile()
        {
            try
            {
                var profile = new RemapperProfile
                {
                    ProfileName = ProfileNameInput,
                    ControllerModel = SelectedControllerModel.Model,
                    MButtons = MButtons.ToList(),
                    PassthroughEnabled = PassthroughEnabled,
                    InnerDeadzone = InnerDeadzone,
                    OuterDeadzone = OuterDeadzone
                };
                _profiles.Save(profile);
                ProfileNameInput = profile.ProfileName;
                _settings.LastProfileName = profile.ProfileName;
                _profiles.SaveSettings(_settings);
                RefreshProfileNames();
                SetSelectedProfileSilent(profile.ProfileName);
                _loadedProfileName = profile.ProfileName;
                HasUnsavedChanges = false;
                FlashSaved();
                StatusMessage = L10n.I.F("status_profile_saved", profile.ProfileName);
                AddLog($"[+] Saved profile '{profile.ProfileName}' to {_profiles.ProfilesDirectory}.");
            }
            catch (Exception ex)
            {
                StatusMessage = L10n.I.F("status_save_failed", ex.Message);
                AddLog($"[!] Save failed: {ex.Message}");
            }
        }

        private void LoadProfile() => LoadProfileByName(ProfileNameInput);

        /// <summary>Name of the profile currently loaded from disk (null for an unsaved new one).</summary>
        public string? LoadedProfileName => _loadedProfileName;

        /// <summary>Load a saved profile by name (tray menu entry point).</summary>
        public void SwitchProfile(string name) => LoadProfileByName(name);

        private void LoadProfileByName(string name)
        {
            try
            {
                if (HasUnsavedChanges)
                {
                    var choice = MessageBox.Show(
                        L10n.I.F("prompt_load_discard", name),
                        L10n.I.T("prompt_unsaved_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes)
                    {
                        SetSelectedProfileSilent(_loadedProfileName);
                        return;
                    }
                }

                var profile = _profiles.Load(name);
                if (profile == null)
                {
                    StatusMessage = L10n.I.F("status_profile_not_found", name);
                    AddLog($"[!] Profile '{name}' not found in {_profiles.ProfilesDirectory}.");
                    SetSelectedProfileSilent(_loadedProfileName);
                    return;
                }

                ApplyProfile(profile);
                SetSelectedProfileSilent(profile.ProfileName);
                _loadedProfileName = profile.ProfileName;
                _settings.LastProfileName = profile.ProfileName;
                _profiles.SaveSettings(_settings);
                StatusMessage = L10n.I.F("status_profile_loaded", profile.ProfileName);
                AddLog($"[+] Loaded profile '{profile.ProfileName}'.");
            }
            catch (Exception ex)
            {
                StatusMessage = L10n.I.F("status_load_failed", ex.Message);
                AddLog($"[!] Load failed: {ex.Message}");
            }
        }

        private void DeleteProfile()
        {
            var choice = MessageBox.Show(
                L10n.I.F("prompt_delete_profile", ProfileNameInput),
                L10n.I.T("prompt_delete_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes) return;

            if (_profiles.Delete(ProfileNameInput))
            {
                RefreshProfileNames();
                if (_loadedProfileName == ProfileNameInput) _loadedProfileName = null;
                SetSelectedProfileSilent(_loadedProfileName);
                StatusMessage = L10n.I.F("status_profile_deleted", ProfileNameInput);
                AddLog($"[−] Deleted profile '{ProfileNameInput}'.");
            }
            else
            {
                StatusMessage = L10n.I.F("status_profile_not_found", ProfileNameInput);
            }
        }

        /// <summary>
        /// Save a copy of what the editor currently shows as "Name (2)" (Explorer
        /// style) and switch to it. Unsaved edits travel with the copy; the
        /// source file on disk is left exactly as last saved.
        /// </summary>
        private void DuplicateProfile()
        {
            try
            {
                string source = string.IsNullOrWhiteSpace(ProfileNameInput) ? "New Profile" : ProfileNameInput.Trim();
                bool carriedEdits = HasUnsavedChanges;

                var copy = _profiles.Clone(new RemapperProfile
                {
                    ProfileName = source,
                    ControllerModel = SelectedControllerModel.Model,
                    MButtons = MButtons.ToList(),
                    PassthroughEnabled = PassthroughEnabled,
                    InnerDeadzone = InnerDeadzone,
                    OuterDeadzone = OuterDeadzone
                });
                copy.ProfileName = _profiles.NextAvailableName(source);
                _profiles.Save(copy);

                ApplyProfile(copy);
                _settings.LastProfileName = copy.ProfileName;
                _profiles.SaveSettings(_settings);
                RefreshProfileNames();
                SetSelectedProfileSilent(copy.ProfileName);
                _loadedProfileName = copy.ProfileName;
                FlashSaved();

                StatusMessage = carriedEdits
                    ? L10n.I.F("status_duplicated_with_edits", copy.ProfileName, source)
                    : L10n.I.F("status_duplicated", copy.ProfileName);
                AddLog($"[+] Duplicated '{source}' → '{copy.ProfileName}'.");
            }
            catch (Exception ex)
            {
                StatusMessage = L10n.I.F("status_duplicate_failed", ex.Message);
                AddLog($"[!] Duplicate failed: {ex.Message}");
            }
        }

        private void NewProfile()
        {
            if (HasUnsavedChanges)
            {
                var choice = MessageBox.Show(
                    L10n.I.T("prompt_new_discard"),
                    L10n.I.T("prompt_unsaved_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.Yes) return;
            }

            LoadDefaults();
            ProfileNameInput = L10n.I.T("new_profile_name");
            _loadedProfileName = null;
            SetSelectedProfileSilent(null);
            StatusMessage = L10n.I.T("status_new_profile");
            AddLog("[+] Reset to default mapping.");
        }

        private void ApplyProfile(RemapperProfile profile)
        {
            _suppressDirty = true;
            try
            {
                MButtons.Clear();
                foreach (var m in profile.MButtons) MButtons.Add(m);

                // Model first, then make every button's source agree with it
                // (older profiles have no source field and default to keyboard).
                SetControllerModelSilent(profile.ControllerModel);
                ApplyControllerModelToButtons(assignDefaultSacrifices: false);

                ProfileNameInput = profile.ProfileName;
                PassthroughEnabled = profile.PassthroughEnabled;
                InnerDeadzone = profile.InnerDeadzone;
                OuterDeadzone = profile.OuterDeadzone;

                SelectedMButton = MButtons.FirstOrDefault();
                _engine.SetMappings(MButtons);
            }
            finally
            {
                _suppressDirty = false;
                HasUnsavedChanges = false;
            }
        }

        private void LoadDefaults()
        {
            _suppressDirty = true;
            try
            {
            // Default trigger keys match the recommended Synapse setup: keys
            // that exist in Synapse's F1–F12-limited remap list (it offers no
            // F13+) and that nobody presses while gaming.
            MButtons.Clear();
            MButtons.Add(new MButtonConfig { MButtonName = "M1", VkCode = 0x91, KeyDisplayName = "ScrollLock", LB = true, X = true });
            MButtons.Add(new MButtonConfig { MButtonName = "M2", VkCode = 0x13, KeyDisplayName = "Pause", RB = true, Y = true });
            MButtons.Add(new MButtonConfig { MButtonName = "M3", VkCode = 0x2D, KeyDisplayName = "Insert", A = true, LB = true });
            MButtons.Add(new MButtonConfig { MButtonName = "M4", VkCode = 0x24, KeyDisplayName = "Home", X = true, RB = true });
            MButtons.Add(new MButtonConfig { MButtonName = "M5", VkCode = 0x21, KeyDisplayName = "PageUp", LT = true, X = true });
            MButtons.Add(new MButtonConfig { MButtonName = "M6", VkCode = 0x22, KeyDisplayName = "PageDown", LB = true, RB = true, Y = true });

            SetControllerModelSilent(ControllerModel.WolverineV3Pro8K);
            SelectedMButton = MButtons.FirstOrDefault();
            _engine.SetMappings(MButtons);
            }
            finally
            {
                _suppressDirty = false;
                HasUnsavedChanges = false;
            }
        }

        private void OnMButtonsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var cfg in _subscribedConfigs) cfg.PropertyChanged -= OnConfigPropertyChanged;
            _subscribedConfigs.Clear();
            foreach (var cfg in MButtons)
            {
                cfg.PropertyChanged += OnConfigPropertyChanged;
                _subscribedConfigs.Add(cfg);
            }

            OnPropertyChanged(nameof(M1));
            OnPropertyChanged(nameof(M2));
            OnPropertyChanged(nameof(M3));
            OnPropertyChanged(nameof(M4));
            OnPropertyChanged(nameof(M5));
            OnPropertyChanged(nameof(M6));
            MarkDirty();
        }

        private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Live state changes are not edits.
            if (e.PropertyName is nameof(MButtonConfig.IsPressed) or nameof(MButtonConfig.IsSelected))
                return;

            MarkDirty();

            // A changed trigger (key or sacrificed pad button) invalidates the lookup tables.
            if (e.PropertyName is nameof(MButtonConfig.VkCode)
                or nameof(MButtonConfig.PadTriggerButton)
                or nameof(MButtonConfig.Source))
            {
                _engine.SetMappings(MButtons);
            }
        }

        #endregion

        #region Gamepad monitor (tester + analyzer)

        private bool _controllerConnected;
        public bool ControllerConnected
        {
            get => _controllerConnected;
            set { if (_controllerConnected != value) { _controllerConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SlotInfoText)); OnPropertyChanged(nameof(SlotInfoTooltip)); } }
        }

        public string[] MonitorSlotChoices { get; } = { "Auto", "Slot 1", "Slot 2", "Slot 3", "Slot 4" };

        private string _monitorSlotChoice = "Auto";
        private int _forcedSlot = -1;
        public string MonitorSlotChoice
        {
            get => _monitorSlotChoice;
            set
            {
                _monitorSlotChoice = value;
                _forcedSlot = value.StartsWith("Slot") ? int.Parse(value.Substring(5)) - 1 : -1;
                OnPropertyChanged();
                AddLog(_forcedSlot >= 0
                    ? $"[Pad] Monitoring forced to XInput slot {_forcedSlot + 1}."
                    : "[Pad] Monitoring set to Auto — following the slot with live input activity.");
            }
        }

        public string SlotInfoText
        {
            get
            {
                if (!ControllerConnected) return "⚠ " + L10n.I.T("slot_no_controller");
                return _engine.IsRunning && _engine.VirtualSlot >= 0
                    ? L10n.I.T("slot_controller") + " ✓ · " + L10n.I.T("slot_virtual_pad") + " ✓"
                    : L10n.I.T("slot_controller") + " ✓";
            }
        }

        public string SlotInfoTooltip
        {
            get
            {
                if (!ControllerConnected)
                    return L10n.I.T("slot_tip_none");

                string tip = L10n.I.F("slot_tip_detected", _monitorSlot + 1);
                tip += "\n" + (_engine.IsRunning && _engine.VirtualSlot >= 0
                    ? L10n.I.F("slot_tip_virtual", _engine.VirtualSlot + 1)
                    : L10n.I.T("slot_tip_start"));
                tip += "\n\n" + L10n.I.T("slot_tip_auto");
                return tip;
            }
        }

        private string _batteryText = "";
        public string BatteryText
        {
            get => _batteryText;
            set { if (_batteryText != value) { _batteryText = value; OnPropertyChanged(); } }
        }

        private bool _isAPressed, _isBPressed, _isXPressed, _isYPressed;
        private bool _isLBPressed, _isRBPressed, _isLTPressed, _isRTPressed;
        private bool _isDPadUpPressed, _isDPadDownPressed, _isDPadLeftPressed, _isDPadRightPressed;
        private bool _isLSPressed, _isRSPressed, _isViewPressed, _isMenuPressed;

        public bool IsAPressed { get => _isAPressed; set { if (_isAPressed != value) { _isAPressed = value; OnPropertyChanged(); } } }
        public bool IsBPressed { get => _isBPressed; set { if (_isBPressed != value) { _isBPressed = value; OnPropertyChanged(); } } }
        public bool IsXPressed { get => _isXPressed; set { if (_isXPressed != value) { _isXPressed = value; OnPropertyChanged(); } } }
        public bool IsYPressed { get => _isYPressed; set { if (_isYPressed != value) { _isYPressed = value; OnPropertyChanged(); } } }
        public bool IsLBPressed { get => _isLBPressed; set { if (_isLBPressed != value) { _isLBPressed = value; OnPropertyChanged(); } } }
        public bool IsRBPressed { get => _isRBPressed; set { if (_isRBPressed != value) { _isRBPressed = value; OnPropertyChanged(); } } }
        public bool IsLTPressed { get => _isLTPressed; set { if (_isLTPressed != value) { _isLTPressed = value; OnPropertyChanged(); } } }
        public bool IsRTPressed { get => _isRTPressed; set { if (_isRTPressed != value) { _isRTPressed = value; OnPropertyChanged(); } } }
        public bool IsDPadUpPressed { get => _isDPadUpPressed; set { if (_isDPadUpPressed != value) { _isDPadUpPressed = value; OnPropertyChanged(); } } }
        public bool IsDPadDownPressed { get => _isDPadDownPressed; set { if (_isDPadDownPressed != value) { _isDPadDownPressed = value; OnPropertyChanged(); } } }
        public bool IsDPadLeftPressed { get => _isDPadLeftPressed; set { if (_isDPadLeftPressed != value) { _isDPadLeftPressed = value; OnPropertyChanged(); } } }
        public bool IsDPadRightPressed { get => _isDPadRightPressed; set { if (_isDPadRightPressed != value) { _isDPadRightPressed = value; OnPropertyChanged(); } } }
        public bool IsLSPressed { get => _isLSPressed; set { if (_isLSPressed != value) { _isLSPressed = value; OnPropertyChanged(); } } }
        public bool IsRSPressed { get => _isRSPressed; set { if (_isRSPressed != value) { _isRSPressed = value; OnPropertyChanged(); } } }
        public bool IsViewPressed { get => _isViewPressed; set { if (_isViewPressed != value) { _isViewPressed = value; OnPropertyChanged(); } } }
        public bool IsMenuPressed { get => _isMenuPressed; set { if (_isMenuPressed != value) { _isMenuPressed = value; OnPropertyChanged(); } } }

        private double _leftTriggerValue, _rightTriggerValue;
        public double LeftTriggerValue { get => _leftTriggerValue; set { if (_leftTriggerValue != value) { _leftTriggerValue = value; OnPropertyChanged(); } } }
        public double RightTriggerValue { get => _rightTriggerValue; set { if (_rightTriggerValue != value) { _rightTriggerValue = value; OnPropertyChanged(); } } }

        private double _leftStickXNorm, _leftStickYNorm, _leftStickMagnitude, _leftStickDriftPercent;
        private double _leftStickCanvasX = 93, _leftStickCanvasY = 93;
        public double LeftStickXNorm { get => _leftStickXNorm; set { _leftStickXNorm = value; OnPropertyChanged(); } }
        public double LeftStickYNorm { get => _leftStickYNorm; set { _leftStickYNorm = value; OnPropertyChanged(); } }
        public double LeftStickMagnitude { get => _leftStickMagnitude; set { _leftStickMagnitude = value; OnPropertyChanged(); } }
        public double LeftStickDriftPercent { get => _leftStickDriftPercent; set { _leftStickDriftPercent = value; OnPropertyChanged(); } }
        public double LeftStickCanvasX { get => _leftStickCanvasX; set { _leftStickCanvasX = value; OnPropertyChanged(); } }
        public double LeftStickCanvasY { get => _leftStickCanvasY; set { _leftStickCanvasY = value; OnPropertyChanged(); } }

        private double _rightStickXNorm, _rightStickYNorm, _rightStickMagnitude, _rightStickDriftPercent;
        private double _rightStickCanvasX = 93, _rightStickCanvasY = 93;
        public double RightStickXNorm { get => _rightStickXNorm; set { _rightStickXNorm = value; OnPropertyChanged(); } }
        public double RightStickYNorm { get => _rightStickYNorm; set { _rightStickYNorm = value; OnPropertyChanged(); } }
        public double RightStickMagnitude { get => _rightStickMagnitude; set { _rightStickMagnitude = value; OnPropertyChanged(); } }
        public double RightStickDriftPercent { get => _rightStickDriftPercent; set { _rightStickDriftPercent = value; OnPropertyChanged(); } }
        public double RightStickCanvasX { get => _rightStickCanvasX; set { _rightStickCanvasX = value; OnPropertyChanged(); } }
        public double RightStickCanvasY { get => _rightStickCanvasY; set { _rightStickCanvasY = value; OnPropertyChanged(); } }

        private double _innerDeadzone = 10.0;
        public double InnerDeadzone
        {
            get => _innerDeadzone;
            set
            {
                _innerDeadzone = Math.Clamp(value, 0, 40);
                _engine.InnerDeadzonePercent = _innerDeadzone;
                OnPropertyChanged();
                MarkDirty();
            }
        }

        private double _outerDeadzone = 95.0;
        public double OuterDeadzone
        {
            get => _outerDeadzone;
            set
            {
                _outerDeadzone = Math.Clamp(value, 60, 100);
                _engine.OuterDeadzonePercent = _outerDeadzone;
                OnPropertyChanged();
                MarkDirty();
            }
        }

        private readonly uint[] _lastPackets = new uint[4];

        private void PollGamepad(object? sender, EventArgs e)
        {
            int virtualSlot = _engine.IsRunning ? _engine.VirtualSlot : -1;
            int previousSlot = _monitorSlot;

            XINPUT_STATE state = default;
            bool got;

            if (_forcedSlot >= 0)
            {
                _monitorSlot = _forcedSlot;
                got = _forcedSlot != virtualSlot && _xinput.GetState(_forcedSlot, out state);
            }
            else
            {
                got = AutoSelectSlot(virtualSlot, ref state);
            }

            // Keep passthrough mirroring the same pad the tester shows.
            _engine.PhysicalSlot = got ? _monitorSlot : -1;
            if (_monitorSlot != previousSlot) OnPropertyChanged(nameof(SlotInfoText)); OnPropertyChanged(nameof(SlotInfoTooltip));

            if (!got)
            {
                if (ControllerConnected)
                {
                    ControllerConnected = false;
                    BatteryText = "";
                    ResetMonitorState();
                }
                return;
            }

            ControllerConnected = true;
            var pad = state.Gamepad;
            RecorderTick(pad);
            ushort btn = pad.wButtons;

            IsAPressed = (btn & XInputService.XINPUT_GAMEPAD_A) != 0;
            IsBPressed = (btn & XInputService.XINPUT_GAMEPAD_B) != 0;
            IsXPressed = (btn & XInputService.XINPUT_GAMEPAD_X) != 0;
            IsYPressed = (btn & XInputService.XINPUT_GAMEPAD_Y) != 0;
            IsLBPressed = (btn & XInputService.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
            IsRBPressed = (btn & XInputService.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
            IsDPadUpPressed = (btn & XInputService.XINPUT_GAMEPAD_DPAD_UP) != 0;
            IsDPadDownPressed = (btn & XInputService.XINPUT_GAMEPAD_DPAD_DOWN) != 0;
            IsDPadLeftPressed = (btn & XInputService.XINPUT_GAMEPAD_DPAD_LEFT) != 0;
            IsDPadRightPressed = (btn & XInputService.XINPUT_GAMEPAD_DPAD_RIGHT) != 0;
            IsLSPressed = (btn & XInputService.XINPUT_GAMEPAD_LEFT_THUMB) != 0;
            IsRSPressed = (btn & XInputService.XINPUT_GAMEPAD_RIGHT_THUMB) != 0;
            IsViewPressed = (btn & XInputService.XINPUT_GAMEPAD_BACK) != 0;
            IsMenuPressed = (btn & XInputService.XINPUT_GAMEPAD_START) != 0;

            LeftTriggerValue = pad.bLeftTrigger;
            RightTriggerValue = pad.bRightTrigger;
            IsLTPressed = pad.bLeftTrigger > 30;
            IsRTPressed = pad.bRightTrigger > 30;

            UpdateStick(pad.sThumbLX, pad.sThumbLY, isLeft: true);
            UpdateStick(pad.sThumbRX, pad.sThumbRY, isLeft: false);

            // Battery is a slow query — refresh every ~2 s, not every frame.
            if (--_batteryCooldown <= 0)
            {
                _batteryCooldown = 125;
                BatteryText = _xinput.GetBatteryDescription(_monitorSlot) ?? "";
            }
        }

        /// <summary>
        /// Picks the XInput slot to monitor by *activity*, not enumeration
        /// order. Dongles and virtual pads can occupy low slots while sitting
        /// completely idle (packet counter frozen); the real controller is the
        /// one whose packet number keeps advancing. Sticky: keeps the current
        /// slot while it's alive, and only jumps to another slot when ours is
        /// silent and that one is actually talking.
        /// </summary>
        private bool AutoSelectSlot(int virtualSlot, ref XINPUT_STATE state)
        {
            bool currentConnected = false, currentActive = false;
            XINPUT_STATE currentState = default;
            int activeSlot = -1;
            XINPUT_STATE activeState = default;
            int firstConnected = -1;
            XINPUT_STATE firstState = default;

            for (int i = 0; i < 4; i++)
            {
                if (i == virtualSlot) continue;
                if (!_xinput.GetState(i, out var s)) continue;

                bool changed = s.dwPacketNumber != _lastPackets[i];
                _lastPackets[i] = s.dwPacketNumber;

                if (firstConnected < 0) { firstConnected = i; firstState = s; }

                if (i == _monitorSlot)
                {
                    currentConnected = true;
                    currentActive = changed;
                    currentState = s;
                }
                else if (changed && activeSlot < 0)
                {
                    activeSlot = i;
                    activeState = s;
                }
            }

            // Stay on the current slot while it's connected and either active
            // itself or unchallenged by activity elsewhere.
            if (currentConnected && (currentActive || activeSlot < 0))
            {
                state = currentState;
                return true;
            }

            if (activeSlot >= 0)
            {
                _monitorSlot = activeSlot;
                state = activeState;
                AddLog($"[Pad] Input activity detected on XInput slot {activeSlot + 1} — now monitoring it.");
                return true;
            }

            if (currentConnected)
            {
                state = currentState;
                return true;
            }

            _monitorSlot = firstConnected;
            state = firstState;
            return firstConnected >= 0;
        }

        private void UpdateStick(short rawX, short rawY, bool isLeft)
        {
            double x = rawX / 32768.0;
            double y = rawY / 32768.0;

            // Sticks report a SQUARE range: a full diagonal is (1,1) = √2 ≈ 141%
            // magnitude. Clamp the plot to the circle rim (keeping direction)
            // and cap the displayed deflection at 100%.
            double magnitude = Math.Sqrt(x * x + y * y);
            double plotX = x, plotY = y;
            if (magnitude > 1)
            {
                plotX = x / magnitude;
                plotY = y / magnitude;
            }
            double magnitudePct = Math.Round(Math.Min(magnitude, 1.0) * 100, 1);

            // Plot: 200×200 canvas, radius 93, 14 px dot centered on the value.
            double canvasX = 100 + plotX * 93 - 7;
            double canvasY = 100 - plotY * 93 - 7;

            if (isLeft)
            {
                LeftStickXNorm = Math.Round(x, 3);
                LeftStickYNorm = Math.Round(y, 3);
                LeftStickMagnitude = magnitudePct;
                LeftStickCanvasX = canvasX;
                LeftStickCanvasY = canvasY;
                if (magnitudePct < InnerDeadzone)
                    LeftStickDriftPercent = Math.Round(LeftStickDriftPercent * 0.95 + magnitudePct * 0.05, 2);
            }
            else
            {
                RightStickXNorm = Math.Round(x, 3);
                RightStickYNorm = Math.Round(y, 3);
                RightStickMagnitude = magnitudePct;
                RightStickCanvasX = canvasX;
                RightStickCanvasY = canvasY;
                if (magnitudePct < InnerDeadzone)
                    RightStickDriftPercent = Math.Round(RightStickDriftPercent * 0.95 + magnitudePct * 0.05, 2);
            }
        }

        private void ResetMonitorState()
        {
            IsAPressed = IsBPressed = IsXPressed = IsYPressed = false;
            IsLBPressed = IsRBPressed = IsLTPressed = IsRTPressed = false;
            IsDPadUpPressed = IsDPadDownPressed = IsDPadLeftPressed = IsDPadRightPressed = false;
            IsLSPressed = IsRSPressed = IsViewPressed = IsMenuPressed = false;
            LeftTriggerValue = RightTriggerValue = 0;
            UpdateStick(0, 0, isLeft: true);
            UpdateStick(0, 0, isLeft: false);
        }

        #endregion

        #region Commands & plumbing

        public ICommand ToggleRemapperCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand LoadProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand DuplicateProfileCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand OpenUpdateCommand { get; }
        public ICommand OpenGitHubCommand { get; }
        public ICommand OpenViGEmPageCommand { get; }
        public ICommand OpenHidHidePageCommand { get; }
        public ICommand RefreshDriversCommand { get; }
        public ICommand SelectMButtonCommand { get; }
        public ICommand RebindCommand { get; }
        public ICommand CancelCaptureCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand AddStepCommand { get; }
        public ICommand CaptureStickCommand { get; }
        public ICommand CancelStickCaptureCommand { get; }
        public ICommand RemoveStepCommand { get; }
        public ICommand ResetOverlayPositionCommand { get; }
        public ICommand ClearStepsCommand { get; }
        public ICommand ShowGuideCommand { get; }
        public ICommand CloseGuideCommand { get; }
        public ICommand GuideNextCommand { get; }
        public ICommand GuideBackCommand { get; }
        public ICommand RecordMacroCommand { get; }
        public ICommand StopRecordingCommand { get; }
        public ICommand CancelRecordingCommand { get; }
        public ICommand MoveStepUpCommand { get; }
        public ICommand MoveStepDownCommand { get; }

        private void AddLog(string msg)
        {
            ActivityLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (ActivityLogs.Count > 200) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
        }

        /// <summary>Called from Window.Closing — releases hook, virtual pad, timers.</summary>
        public void Shutdown()
        {
            _pollTimer.Stop();
            _profiles.SaveSettings(_settings);
            _engine.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }
}
