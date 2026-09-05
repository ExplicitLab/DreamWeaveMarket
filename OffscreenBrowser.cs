using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DreamweaveMarket
{
    /// <summary>
    /// A WebView2 that renders where nobody can see it, so its frames can be drawn inside the
    /// game instead.
    ///
    /// Why a real window positioned off the desktop rather than a hidden one: WebView2 has no
    /// offscreen rendering mode. A window that is hidden, minimised or fully occluded gets its
    /// renderer throttled or stopped by Chromium, and the capture comes back blank or stale. A
    /// window that exists at normal size, is shown, and simply sits beyond the edge of the
    /// virtual desktop keeps painting - with the occlusion detection switched off in the browser
    /// arguments below, which is what stops Chromium noticing it is not visible.
    ///
    /// Everything WebView2 touches happens on this class's own STA thread. Frames are handed to
    /// the game thread through a single swapped reference; input goes the other way as Chrome
    /// DevTools Protocol calls, which inject at the browser level and so do not care that the
    /// window has no real mouse over it.
    /// </summary>
    public sealed class OffscreenBrowser : IDisposable
    {
        private readonly string url;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Thread uiThread;
        private readonly object frameLock = new object();

        private Form form;
        private WebView2 web;
        private volatile bool initialised;
        private volatile bool capturing;
        private volatile bool disposed;

        private Bitmap pendingFrame;        // written on the UI thread, taken by the game thread
        private int width;
        private int height;

        public volatile string LastError;
        public volatile string PageTitle = "";

        private double zoom = 1.0;

        public OffscreenBrowser(string url, int width, int height)
        {
            this.url = url;
            this.width = Math.Max(64, width);
            this.height = Math.Max(64, height);

            uiThread = new Thread(UiThreadMain)
            {
                Name = "DreamweaveMarket offscreen",
                IsBackground = true
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
        }

        public bool Ready { get { return initialised; } }

        // ------------------------------------------------------------------
        // UI thread
        // ------------------------------------------------------------------

        private void UiThreadMain()
        {
            try
            {
                form = new OffscreenForm();
                form.ClientSize = new Size(width, height);

                // Off to the right of every monitor, but level with the TOP of the desktop, not
                // past the bottom of it.
                //
                // The horizontal offset is what hides the window. The vertical position looks
                // arbitrary and is not: Chromium places a <select> popup using the element's
                // position in SCREEN coordinates, and opens the list upwards when there is no room
                // for it below on the nearest monitor. Parked past the bottom edge, as this was,
                // there never is - so every dropdown in the page opened upwards, unlike the same
                // page in a real browser. Level with the top there is a monitor's worth of room
                // below and they open the right way.
                Rectangle vs = SystemInformation.VirtualScreen;
                form.Location = new Point(vs.Right + 64, vs.Top + 8);

                web = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(web);

                IntPtr unused = form.Handle;
                form.Show();

                StartWebViewAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Util.LogError(ex);
            }
            finally { ready.Set(); }

            if (form != null)
                Application.Run();
        }

        private async void StartWebViewAsync()
        {
            try
            {
                // Shared with the pop-out window, and it has to be created identically in both -
                // see WebViewEnvironment. The occlusion flags in there are what keep this window
                // painting while it sits off the edge of the desktop.
                CoreWebView2Environment env = await WebViewEnvironment.CreateAsync();

                await web.EnsureCoreWebView2Async(env);

                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;

                web.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    try { PageTitle = web.CoreWebView2.DocumentTitle; } catch { }
                };

                web.ZoomFactor = zoom;
                web.CoreWebView2.Navigate(url);

                initialised = true;
                Util.Log("offscreen browser ready at " + width + "x" + height);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Util.LogError(ex);
            }
        }

        /// <summary>A borderless window that never steals focus when shown.</summary>
        private sealed class OffscreenForm : Form
        {
            public OffscreenForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
            }

            protected override bool ShowWithoutActivation { get { return true; } }
        }

        private void OnUi(Action a)
        {
            try
            {
                ready.WaitOne(5000);

                if (disposed || form == null || form.IsDisposed || !form.IsHandleCreated)
                    return;

                form.BeginInvoke(a);
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        // ------------------------------------------------------------------
        // Frames
        // ------------------------------------------------------------------

        /// <summary>
        /// Asks for a new frame. Returns straight away - the capture runs on the browser's thread
        /// and the result turns up at TakeFrame some milliseconds later. Only one capture is ever
        /// in flight, so calling this every game frame is harmless.
        /// </summary>
        public void RequestFrame()
        {
            if (!initialised || capturing || disposed)
                return;

            capturing = true;
            OnUi(async () =>
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        await web.CoreWebView2.CapturePreviewAsync(
                            CoreWebView2CapturePreviewImageFormat.Png, ms);

                        ms.Position = 0;

                        // Copied out of the stream deliberately: a Bitmap built straight on a
                        // MemoryStream keeps a reference to it, and the stream is about to go.
                        using (Image img = Image.FromStream(ms))
                        {
                            Bitmap copy = new Bitmap(img);

                            lock (frameLock)
                            {
                                if (pendingFrame != null)
                                    pendingFrame.Dispose();

                                pendingFrame = copy;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Util.LogError(ex);
                }
                finally { capturing = false; }
            });
        }

        /// <summary>
        /// Hands over the newest frame, or null if there is not a fresh one. The caller owns the
        /// bitmap it gets back and must dispose it.
        /// </summary>
        public Bitmap TakeFrame()
        {
            lock (frameLock)
            {
                Bitmap b = pendingFrame;
                pendingFrame = null;
                return b;
            }
        }

        /// <summary>
        /// Sets the page zoom, as WebView2 understands it - the same thing Ctrl+plus does in a
        /// browser. Note that this changes the size of a CSS pixel, so anything sending input in
        /// page coordinates has to divide by it: see MarketBrowser.PagePoint.
        /// </summary>
        public void SetZoom(double factor)
        {
            zoom = factor;

            OnUi(() =>
            {
                try { web.ZoomFactor = zoom; }
                catch (Exception ex) { Util.LogError(ex); }
            });
        }

        public void Resize(int w, int h)
        {
            w = Math.Max(64, w);
            h = Math.Max(64, h);

            if (w == width && h == height)
                return;

            width = w;
            height = h;

            OnUi(() =>
            {
                try { form.ClientSize = new Size(width, height); }
                catch (Exception ex) { Util.LogError(ex); }
            });
        }

        public void Navigate(string address)
        {
            OnUi(() =>
            {
                try { web.CoreWebView2.Navigate(address); }
                catch (Exception ex) { Util.LogError(ex); }
            });
        }

        public void Reload()
        {
            OnUi(() =>
            {
                try { web.CoreWebView2.Reload(); }
                catch (Exception ex) { Util.LogError(ex); }
            });
        }

        public void GoBack()
        {
            OnUi(() =>
            {
                try { if (web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack(); }
                catch (Exception ex) { Util.LogError(ex); }
            });
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        // Chrome DevTools Protocol rather than Win32 messages: CDP injects at the browser's own
        // input layer, so it does not care that the window is off-screen and has no real cursor
        // anywhere near it. Win32 messages to an unfocused off-screen window are far less
        // predictable.

        /// <summary>
        /// Moves the pointer. leftDown matters more than it looks: a mouseMoved that reports no
        /// button held is a hover, and Chromium will not drag a scrollbar, a slider or a selection
        /// with one. It is the difference between a scrollbar you can click and a scrollbar you
        /// can drag.
        /// </summary>
        public void MouseMove(int x, int y, bool leftDown)
        {
            Cdp("Input.dispatchMouseEvent",
                "{\"type\":\"mouseMoved\",\"x\":" + I(x) + ",\"y\":" + I(y)
                + ",\"button\":\"" + (leftDown ? "left" : "none") + "\""
                + ",\"buttons\":" + (leftDown ? "1" : "0") + "}");
        }

        public void MouseDown(int x, int y, bool rightButton)
        {
            string b = rightButton ? "right" : "left";
            Cdp("Input.dispatchMouseEvent",
                "{\"type\":\"mousePressed\",\"x\":" + I(x) + ",\"y\":" + I(y)
                + ",\"button\":\"" + b + "\",\"buttons\":" + (rightButton ? "2" : "1")
                + ",\"clickCount\":1}");
        }

        public void MouseUp(int x, int y, bool rightButton)
        {
            string b = rightButton ? "right" : "left";
            Cdp("Input.dispatchMouseEvent",
                "{\"type\":\"mouseReleased\",\"x\":" + I(x) + ",\"y\":" + I(y)
                + ",\"button\":\"" + b + "\",\"buttons\":0,\"clickCount\":1}");
        }

        /// <summary>
        /// Sends a typed character. CDP's "char" event is the one that actually inserts text -
        /// keyDown alone moves focus and fires handlers but types nothing.
        /// </summary>
        public void SendChar(char c)
        {
            Cdp("Input.dispatchKeyEvent",
                "{\"type\":\"char\",\"text\":\"" + JsonEscape(c.ToString()) + "\"}");
        }

        /// <summary>
        /// Sends a non-printing key - backspace, enter, the arrows - by Windows virtual key code.
        /// rawKeyDown rather than keyDown, because keyDown asks Chromium to synthesise the text
        /// itself and would double up with the char events above.
        /// </summary>
        public void SendKey(bool down, int virtualKey, string keyName)
        {
            string type = down ? "rawKeyDown" : "keyUp";

            Cdp("Input.dispatchKeyEvent",
                "{\"type\":\"" + type + "\""
                + ",\"windowsVirtualKeyCode\":" + I(virtualKey)
                + ",\"nativeVirtualKeyCode\":" + I(virtualKey)
                + (keyName == null ? "" : ",\"key\":\"" + JsonEscape(keyName) + "\"")
                + "}");
        }

        private static string JsonEscape(string text)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length + 8);

            foreach (char c in text)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7e)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        public void MouseWheel(int x, int y, int delta)
        {
            Cdp("Input.dispatchMouseEvent",
                "{\"type\":\"mouseWheel\",\"x\":" + I(x) + ",\"y\":" + I(y)
                + ",\"deltaX\":0,\"deltaY\":" + I(delta) + "}");
        }

        private static string I(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private void Cdp(string method, string json)
        {
            if (!initialised || disposed)
                return;

            OnUi(async () =>
            {
                try { await web.CoreWebView2.CallDevToolsProtocolMethodAsync(method, json); }
                catch (Exception ex) { Util.LogError(ex); }
            });
        }

        // ------------------------------------------------------------------

        public void Dispose()
        {
            disposed = true;

            OnUi(() =>
            {
                try { form.Close(); form.Dispose(); } catch { }
                Application.ExitThread();
            });

            lock (frameLock)
            {
                if (pendingFrame != null)
                {
                    pendingFrame.Dispose();
                    pendingFrame = null;
                }
            }
        }
    }
}
