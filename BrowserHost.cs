using System;
using System.Threading;
using System.Windows.Forms;

namespace DreamweaveMarket
{
    /// <summary>
    /// Owns a dedicated STA UI thread with its own message loop so the browser
    /// window never depends on the game's message pump. All calls from the
    /// plugin (Toggle/Reload/Dispose) are marshalled onto that thread.
    /// </summary>
    public sealed class BrowserHost : IDisposable
    {
        private readonly string _url;
        private readonly Thread _uiThread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private MarketForm _form;

        public BrowserHost(string url)
        {
            _url = url;
            _uiThread = new Thread(UiThreadMain)
            {
                Name = "DreamweaveMarket UI",
                IsBackground = true
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
        }

        private void UiThreadMain()
        {
            try
            {
                _form = new MarketForm(_url);

                // Touching Handle forces the window handle into existence. Form.CreateControl()
                // does not - it returns early for a top-level window that has never been shown -
                // and without a handle the BeginInvoke below throws.
                IntPtr unused = _form.Handle;
            }
            catch (Exception ex) { Util.LogError(ex); }
            finally { _ready.Set(); }

            if (_form == null)
                return;

            Application.Run();   // runs until Application.ExitThread() in Dispose
        }

        /// <summary>
        /// Runs the action on the browser's own UI thread. Called from Decal's thread, so
        /// everything here has to survive the window not existing (yet, or any more).
        /// </summary>
        private void OnUi(Action a)
        {
            try
            {
                _ready.WaitOne(5000);

                if (_form == null || _form.IsDisposed || !_form.IsHandleCreated)
                    return;

                _form.BeginInvoke(a);
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>One-line summary of the browser thread, for /market diag.</summary>
        public string State
        {
            get
            {
                try
                {
                    if (!_ready.WaitOne(0)) return "still starting";
                    if (_form == null) return "window failed to construct - see the log";
                    if (_form.IsDisposed) return "window disposed";
                    if (!_form.IsHandleCreated) return "window has no handle";
                    return _form.Visible ? "open" : "hidden";
                }
                catch (Exception ex)
                {
                    Util.LogError(ex);
                    return "unknown";
                }
            }
        }

        public void Toggle() => OnUi(() => _form.ToggleVisible());
        public void Reload() => OnUi(() => _form.ReloadPage());
        public void SetTopMost(bool on) => OnUi(() => _form.TopMost = on);
        public void SetZoom(double factor) => OnUi(() => _form.SetZoom(factor));

        public void Dispose()
        {
            OnUi(() =>
            {
                try { _form.Close(); _form.Dispose(); } catch { }
                Application.ExitThread();
            });
        }
    }
}
