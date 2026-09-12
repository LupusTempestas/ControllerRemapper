using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using WolverineRemapper.ViewModels;

namespace WolverineRemapper
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closing += OnWindowClosing;
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // `--autostart` launches straight into remapping (for shortcuts
            // that should be game-ready without an extra click).
            bool autostart = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
            if (autostart && DataContext is MainViewModel vm && !vm.IsRemapperActive)
            {
                vm.ToggleRemapperCommand.Execute(null);
            }
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (vm.HasUnsavedChanges)
            {
                var choice = MessageBox.Show(
                    $"Save profile '{vm.ProfileNameInput}' before closing?",
                    "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
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

            // Release the keyboard hook and disconnect the virtual pad —
            // otherwise a stale hook can degrade system-wide input latency.
            vm.Shutdown();
        }
    }
}
