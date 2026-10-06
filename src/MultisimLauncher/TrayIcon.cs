// ============================================================================
//  TrayIcon.cs - the notification-area presence.
//
//  Owns a NotifyIcon plus its context menu, and nothing else: it reports clicks
//  and menu choices back through events so MainForm stays the only place that
//  decides what the application does.
//
//  Kept separate from MainForm because NotifyIcon has to be disposed explicitly.
//  Icons left in the notification area after the process exits are a common
//  annoyance, and confining the lifetime to one small class makes it obvious
//  where that has to happen.
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace MultisimLauncher
{
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private bool _disposed;

        internal event EventHandler ShowRequested;
        internal event EventHandler ExitRequested;

        internal TrayIcon(string tooltip, Icon icon)
        {
            _menu = new ContextMenuStrip();
            _menu.Items.Add(Strings.TrayShow, null, delegate { RaiseShow(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Strings.TrayExit, null, delegate { RaiseExit(); });

            _icon = new NotifyIcon();
            _icon.Text = Tooltip(tooltip);
            _icon.Icon = icon;
            _icon.ContextMenuStrip = _menu;
            _icon.Visible = true;
            _icon.DoubleClick += delegate { RaiseShow(); };
        }

        /// <summary>
        /// NotifyIcon.Text is capped at 63 characters and throws beyond that, so
        /// callers must not be able to crash the tray by passing a long string.
        /// </summary>
        private static string Tooltip(string s)
        {
            if (string.IsNullOrEmpty(s)) return Program.AppName;
            return s.Length <= 62 ? s : s.Substring(0, 62);
        }

        internal void SetTooltip(string s)
        {
            if (_disposed) return;
            try { _icon.Text = Tooltip(s); } catch { }
        }

        private void RaiseShow()
        {
            EventHandler h = ShowRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void RaiseExit()
        {
            EventHandler h = ExitRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>Brief balloon, used to explain where the window went.</summary>
        internal void Balloon(string title, string text)
        {
            if (_disposed) return;
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.ShowBalloonTip(4000);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Hide before disposing, otherwise the icon can linger until the
            // mouse is moved over the notification area.
            try { _icon.Visible = false; } catch { }
            try { _icon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
        }
    }
}
