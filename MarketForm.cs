using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DreamweaveMarket
{
    public sealed class MarketForm : Form
    {
        private readonly string _url;
        private readonly WebView2 _web;
        private bool _initialized;

        public MarketForm(string url)
        {
            _url = url;

            Text = "Dreamweave Market " + Util.Version;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1000, 700);
            TopMost = true;                 // stays above the (windowed) game client
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;

            _web = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);

            // Closing the window just hides it, so the page keeps its state.
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        public void ToggleVisible()
        {
            if (Visible) { Hide(); return; }

            if (!_initialized) PositionOverGame();
            Show();
            Activate();
            _ = EnsureInitializedAsync();
        }

        public void SetZoom(double factor)
        {
            try { _web.ZoomFactor = factor; }
            catch (Exception ex) { Util.LogError(ex); }
        }

        public void ReloadPage()
        {
            if (_web.CoreWebView2 != null) _web.CoreWebView2.Navigate(_url);
        }

        private async System.Threading.Tasks.Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                // Shared with the in-game renderer, and described identically - see
                // WebViewEnvironment. Two environments over one user data folder with different
                // options is 0x8007139F and a dead browser.
                var env = await WebViewEnvironment.CreateAsync();
                await _web.EnsureCoreWebView2Async(env);

                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Keep the user on the market site; hand anything else to the system browser.
                _web.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
                };

                _web.ZoomFactor = Settings.ZoomPercent / 100.0;
                _web.CoreWebView2.Navigate(_url);
            }
            catch (Exception ex)
            {
                _initialized = false;
                MessageBox.Show(this,
                    "Could not start the embedded browser.\n\n" +
                    "Make sure the Microsoft Edge WebView2 Runtime is installed:\n" +
                    "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" + ex.Message,
                    "Dreamweave Market", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Util.LogError(ex);
            }
        }

        /// <summary>Centers the window over the game client window on first open.</summary>
        private void PositionOverGame()
        {
            try
            {
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r))
                {
                    int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                    Location = new Point(cx - Width / 2, cy - Height / 2);
                    return;
                }
            }
            catch { }
            StartPosition = FormStartPosition.CenterScreen;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
