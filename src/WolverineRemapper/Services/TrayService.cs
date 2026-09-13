using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using WolverineRemapper.ViewModels;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// Notification-area icon with a quick-access menu: open, start/stop the
    /// engine, switch profile, restart, quit. Built on the WinForms NotifyIcon
    /// (WPF has none). Labels come from L10n and are rebuilt on language change.
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        private readonly MainViewModel _vm;
        private readonly Action _show;
        private readonly Action _restart;
        private readonly Action _quit;

        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu = new();
        private readonly ToolStripMenuItem _open = new();
        private readonly ToolStripMenuItem _engine = new();
        private readonly ToolStripMenuItem _profiles = new();
        private readonly ToolStripMenuItem _startup = new() { CheckOnClick = true };
        private readonly ToolStripMenuItem _overlay = new();
        private readonly ToolStripMenuItem _overlayShow = new() { CheckOnClick = true };
        private readonly ToolStripMenuItem _overlayLock = new() { CheckOnClick = true };
        private readonly ToolStripMenuItem _overlayReset = new();
        private readonly ToolStripMenuItem _restartItem = new();
        private readonly ToolStripMenuItem _quitItem = new();

        public TrayService(MainViewModel vm, Action show, Action restart, Action quit)
        {
            _vm = vm;
            _show = show;
            _restart = restart;
            _quit = quit;

            _open.Font = new Font(_open.Font, System.Drawing.FontStyle.Bold);
            _open.Click += (_, _) => _show();
            _engine.Click += (_, _) => _vm.ToggleRemapperCommand.Execute(null);
            _restartItem.Click += (_, _) => _restart();
            _quitItem.Click += (_, _) => _quit();
            _startup.CheckedChanged += (_, _) => { if (_vm.StartWithWindows != _startup.Checked) _vm.StartWithWindows = _startup.Checked; };
            _overlayShow.CheckedChanged += (_, _) => { if (_vm.OverlayEnabled != _overlayShow.Checked) _vm.OverlayEnabled = _overlayShow.Checked; };
            _overlayLock.CheckedChanged += (_, _) => { if (_vm.OverlayLocked != _overlayLock.Checked) _vm.OverlayLocked = _overlayLock.Checked; };
            _overlayReset.Click += (_, _) => _vm.ResetOverlayPosition();
            _overlay.DropDownItems.AddRange(new ToolStripItem[] { _overlayShow, _overlayLock, _overlayReset });

            _menu.Items.AddRange(new ToolStripItem[]
            {
                _open, new ToolStripSeparator(),
                _engine, _profiles, _overlay, new ToolStripSeparator(),
                _startup, new ToolStripSeparator(),
                _restartItem, _quitItem
            });
            _menu.Opening += (_, _) =>
            {
                RebuildProfiles();
                _startup.Checked = _vm.StartWithWindows;
                _overlayShow.Checked = _vm.OverlayEnabled;
                _overlayLock.Checked = _vm.OverlayLocked;
                _overlayLock.Enabled = _vm.OverlayEnabled;
                _overlayReset.Enabled = _vm.OverlayEnabled;
            };

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _show(); };

            _vm.PropertyChanged += OnVmPropertyChanged;
            L10n.I.LanguageChanged += Relabel;
            Relabel();
        }

        /// <summary>Balloon shown the first time the window is closed to the tray.</summary>
        public void ShowHiddenHint()
        {
            _icon.ShowBalloonTip(3000, L10n.I.T("tray_hidden_title"), L10n.I.T("tray_hidden_text"), ToolTipIcon.Info);
        }

        /// <summary>Balloon for a newer release; clicking it brings the window (and its update banner) up.</summary>
        public void ShowUpdateBalloon(Version latest)
        {
            _icon.BalloonTipClicked -= OnUpdateBalloonClicked;
            _icon.BalloonTipClicked += OnUpdateBalloonClicked;
            _icon.ShowBalloonTip(8000, L10n.I.T("tray_update_title"), L10n.I.F("tray_update_text", "v" + latest), ToolTipIcon.Info);
        }

        private void OnUpdateBalloonClicked(object? sender, EventArgs e)
        {
            _icon.BalloonTipClicked -= OnUpdateBalloonClicked;
            _show();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsRemapperActive)) Relabel();
        }

        private void Relabel()
        {
            bool running = _vm.IsRemapperActive;
            _open.Text = L10n.I.T("tray_open");
            _engine.Text = running ? L10n.I.T("tray_stop_engine") : L10n.I.T("tray_start_engine");
            _profiles.Text = L10n.I.T("tray_profiles");
            _startup.Text = L10n.I.T("tray_start_with_windows");
            _overlay.Text = L10n.I.T("tray_overlay");
            _overlayShow.Text = L10n.I.T("tray_overlay_show");
            _overlayLock.Text = L10n.I.T("tray_overlay_lock");
            _overlayReset.Text = L10n.I.T("tray_overlay_reset");
            _restartItem.Text = L10n.I.T("tray_restart");
            _quitItem.Text = L10n.I.T("tray_quit");

            string state = running ? L10n.I.T("engine_online") : L10n.I.T("engine_stopped");
            string tip = $"Wolverine Remapper — {state}";
            _icon.Text = tip.Length > 63 ? tip[..63] : tip; // NotifyIcon caps the tooltip at 63 chars
        }

        private void RebuildProfiles()
        {
            _profiles.DropDownItems.Clear();
            var names = _vm.ProfileNames.ToList();
            if (names.Count == 0)
            {
                _profiles.DropDownItems.Add(new ToolStripMenuItem(L10n.I.T("tray_no_profiles")) { Enabled = false });
                return;
            }
            foreach (var name in names)
            {
                var item = new ToolStripMenuItem(name)
                {
                    Checked = string.Equals(name, _vm.LoadedProfileName, StringComparison.OrdinalIgnoreCase)
                };
                string captured = name;
                item.Click += (_, _) => _vm.SwitchProfile(captured);
                _profiles.DropDownItems.Add(item);
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
                if (res != null) return new Icon(res.Stream);
            }
            catch { /* fall through */ }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            L10n.I.LanguageChanged -= Relabel;
            _icon.Visible = false; // otherwise a ghost icon lingers until the tray is hovered
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
