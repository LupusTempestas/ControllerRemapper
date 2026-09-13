using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WolverineRemapper.ViewModels;

namespace WolverineRemapper
{
    /// <summary>
    /// Transparent, always-on-top live controller. While unlocked it can be
    /// dragged; a right-click locks it, which also makes it click-through so
    /// the game underneath receives the mouse. Never takes focus.
    /// Note: works over windowed and borderless games; exclusive-fullscreen
    /// games draw over every window, overlays included.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly MainViewModel _vm;
        private IntPtr _hwnd;
        private static readonly Brush UnlockedBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));

        public OverlayWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            SourceInitialized += OnSourceInitialized;
            LocationChanged += OnLocationChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            ApplyLock();
            PlaceFromSettings();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.OverlayLocked)) ApplyLock();
        }

        /// <summary>Locked ⇒ click-through and no frame; unlocked ⇒ draggable with a cyan frame.</summary>
        private void ApplyLock()
        {
            bool locked = _vm.OverlayLocked;
            if (_hwnd != IntPtr.Zero)
            {
                int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
                ex = locked ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            }
            Frame.BorderBrush = locked ? Brushes.Transparent : UnlockedBrush;
            Frame.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
            Hint.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (_vm.OverlayLocked) return;
            try { DragMove(); } catch { /* released outside — ignore */ }
        }

        private void OnLockRequested(object sender, MouseButtonEventArgs e)
        {
            if (!_vm.OverlayLocked) _vm.OverlayLocked = true;
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            if (!IsLoaded || _vm.OverlayLocked) return;
            _vm.SaveOverlayPosition(Left, Top);
        }

        /// <summary>Restore the saved spot, or park it at the top-right of the work area.</summary>
        public void PlaceFromSettings()
        {
            var area = SystemParameters.WorkArea;
            double x = _vm.OverlayX, y = _vm.OverlayY;
            bool onScreen = x >= area.Left - 50 && y >= area.Top - 50 && x < area.Right - 50 && y < area.Bottom - 50;
            if (double.IsNaN(x) || double.IsNaN(y) || !onScreen)
            {
                x = area.Right - 320;
                y = area.Top + 24;
            }
            Left = x;
            Top = y;
        }

        public void ResetPosition()
        {
            _vm.SaveOverlayPosition(double.NaN, double.NaN);
            PlaceFromSettings();
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            base.OnClosed(e);
        }
    }
}
