using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using WolverineRemapper.Services;
using WolverineRemapper.ViewModels;

namespace WolverineRemapper
{
    public partial class MainWindow : Window
    {
        private TrayService? _tray;
        private bool _exitRequested;
        private bool _restartRequested;
        private bool _hiddenHintShown;

        public MainWindow()
        {
            InitializeComponent();
            Closing += OnWindowClosing;
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            _tray = new TrayService(vm, ShowFromTray, RequestRestart, RequestExit);

            // `--autostart` (or the setting) launches straight into remapping;
            // `--minimized` (what the Windows Run key passes) stays in the tray.
            var args = Environment.GetCommandLineArgs();
            bool autostart = vm.AutoStartEngine || args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
            bool minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            if (autostart && !vm.IsRemapperActive)
            {
                vm.ToggleRemapperCommand.Execute(null);
            }
            if (minimized)
            {
                _hiddenHintShown = true; // the user asked for this; no balloon
                Dispatcher.BeginInvoke(new Action(Hide), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>Bring the window back from the notification area.</summary>
        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>Really exit (tray Quit): runs the unsaved-changes prompt, then shuts down.</summary>
        public void RequestExit()
        {
            _exitRequested = true;
            Close();
            _exitRequested = false; // reached only if the close was cancelled
        }

        /// <summary>Exit and launch a fresh instance (engine restarted if it was running).</summary>
        public void RequestRestart()
        {
            _restartRequested = true;
            RequestExit();
            _restartRequested = false;
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            // The close button hides to the tray; the engine keeps running.
            // Hide() issued inside a cancelled Closing is ignored by WPF, so
            // defer it until the close sequence has unwound.
            if (!_exitRequested && vm.CloseToTray)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Hide();
                    if (!_hiddenHintShown)
                    {
                        _hiddenHintShown = true;
                        _tray?.ShowHiddenHint();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            if (vm.HasUnsavedChanges)
            {
                ShowFromTray(); // a prompt behind a hidden window is invisible
                var choice = MessageBox.Show(
                    L10n.I.F("prompt_save_before_close", vm.ProfileNameInput),
                    L10n.I.T("prompt_unsaved_title"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (choice == MessageBoxResult.Yes)
                {
                    vm.SaveProfileCommand.Execute(null);
                }
            }

            bool relaunch = _restartRequested;
            bool engineWasRunning = vm.IsRemapperActive;

            // Release the keyboard hook and disconnect the virtual pad —
            // otherwise a stale hook can degrade system-wide input latency.
            vm.Shutdown();
            _tray?.Dispose();
            _tray = null;

            if (relaunch)
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(exe, engineWasRunning ? "--autostart" : "")
                        {
                            UseShellExecute = true,
                            WorkingDirectory = Environment.CurrentDirectory
                        });
                    }
                    catch { /* nothing sensible to do — the user can relaunch by hand */ }
                }
            }

            Application.Current.Shutdown();
        }
    }
}
