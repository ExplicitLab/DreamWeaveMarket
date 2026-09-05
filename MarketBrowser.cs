using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;   // CompositingMode
using System.Drawing.Imaging;    // PixelFormat

#if VVS_REFERENCED
using VirindiViewService;
using VirindiViewService.Controls;
#endif

namespace DreamweaveMarket
{
    /// <summary>
    /// The in-game view of the market: a picture box inside the plugin's Virindi window, fed
    /// frames captured from an off-screen WebView2, with mouse input sent back the other way.
    ///
    /// The obvious way to do this was VVS's own HudBrowser control, and the first version did.
    /// It does not work: HudBrowser.IsAvailable returns true, the control attaches and draws,
    /// and then renders a blank white page for every address - including plain HTTP ones, so it
    /// is not merely the old engine failing on modern TLS. Available only means VVS could load
    /// WebControlWrapper.dll, and nothing in the API reports the engine behind it being dead.
    ///
    /// So the rendering is done by the same Chromium that already works in the pop-out window,
    /// and only the pixels come into the game. That costs a screenshot per refresh and gives up
    /// keyboard input, but it renders the actual site rather than nothing at all.
    /// </summary>
    public sealed class MarketBrowser
    {
        /// <summary>Refresh interval. Four frames a second: enough for scrolling to read as
        /// motion, cheap enough that the capture never becomes the reason the client stutters.</summary>
        private const int RefreshMs = 250;

        /// <summary>A quicker burst after a click or scroll, so interaction feels immediate.</summary>
        private const int ActiveRefreshMs = 80;
        private const int ActiveWindowMs = 1200;

        private readonly string url;

        private OffscreenBrowser offscreen;
        private int lastRefresh;
        private int activeUntil;

        private bool everPainted;
        private string shownMessage;

        /// <summary>Whether the left button is currently held on the page - see MouseMove.</summary>
        private bool leftDown;
        private int lastX;
        private int lastY;

        /// <summary>Page zoom, mirrored here because every input coordinate has to be divided
        /// by it - see PagePoint.</summary>
        private double zoom = 1.0;

#if VVS_REFERENCED
        private HudPictureBox picture;
        private HudFixedLayout page;
        private HudView hud;

        private ACImage currentImage;

        // Images already replaced, freed a few frames later - see Retire.
        private readonly List<Retiree> retired = new List<Retiree>();
#endif

        public MarketBrowser(string url)
        {
            this.url = url;
        }

        public bool Attached
        {
            get
            {
#if VVS_REFERENCED
                return picture != null;
#else
                return false;
#endif
            }
        }

        public string Status
        {
            get
            {
                if (offscreen == null) return "not started";
                if (offscreen.LastError != null) return "error: " + offscreen.LastError;
                if (!offscreen.Ready) return "starting";

                return "rendering" + (offscreen.PageTitle.Length > 0
                                      ? " - " + offscreen.PageTitle : "");
            }
        }

        // ------------------------------------------------------------------

        /// <summary>Puts the picture box on the named layout and starts the browser behind it.</summary>
        public bool Attach(object hudView, string pageControlName)
        {
#if VVS_REFERENCED
            try
            {
                hud = hudView as HudView;
                if (hud == null)
                {
                    Util.Log("in-game: no VVS view to attach to");
                    return false;
                }

                page = hud[pageControlName] as HudFixedLayout;
                if (page == null)
                {
                    Util.Log("in-game: control '" + pageControlName + "' is not a layout");
                    return false;
                }

                Rectangle r = PageRect();

                picture = new HudPictureBox();
                page.AddControl(picture, r);

                picture.MouseEvent += Picture_MouseEvent;
                picture.KeyEvent += Picture_KeyEvent;
                hud.Resize += Hud_Resize;

                offscreen = new OffscreenBrowser(url, r.Width, r.Height);

                zoom = Settings.ZoomPercent / 100.0;
                offscreen.SetZoom(zoom);

                Globals.Core.RenderFrame += Core_RenderFrame;

                Util.Log("in-game surface attached at " + r);
                return true;
            }
            catch (Exception ex)
            {
                Util.LogError(ex);
                Detach();
                return false;
            }
#else
            return false;
#endif
        }

        public void Detach()
        {
#if VVS_REFERENCED
            try
            {
                try { Globals.Core.RenderFrame -= Core_RenderFrame; } catch { }

                if (hud != null) hud.Resize -= Hud_Resize;
                if (picture != null)
                {
                    picture.MouseEvent -= Picture_MouseEvent;
                    picture.KeyEvent -= Picture_KeyEvent;
                }

                if (offscreen != null)
                {
                    offscreen.Dispose();
                    offscreen = null;
                }

                if (picture != null)
                {
                    picture.Dispose();
                    picture = null;
                }

                // The picture box is gone by now, so nothing can be mid-draw on these.
                if (currentImage != null) { currentImage.Dispose(); currentImage = null; }

                foreach (Retiree r in retired)
                {
                    try { r.Image.Dispose(); } catch (Exception ex) { Util.LogError(ex); }
                }

                retired.Clear();
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        public void Reload()
        {
            if (offscreen != null) offscreen.Reload();
            MarkActive();
        }

        /// <summary>Applies a new zoom, and remembers it for the coordinate mapping.</summary>
        public void SetZoom(double factor)
        {
            zoom = factor <= 0 ? 1.0 : factor;

            if (offscreen != null)
                offscreen.SetZoom(zoom);

            MarkActive();
        }

        /// <summary>
        /// Turns a point on the control into a point on the page.
        ///
        /// The capture is the size of the control, so at 100% the two are the same. Zoom changes
        /// the size of a CSS pixel, though, and the coordinates the browser wants are CSS pixels -
        /// so at 80% zoom the page is 1/0.8 as wide as the control in the units the browser
        /// counts in. Without this, everything still looks right and every click lands somewhere
        /// else, further out the further from the top-left corner you go.
        /// </summary>
        private int PagePoint(int v)
        {
            return (int)Math.Round(v / zoom);
        }

        public void Navigate(string address)
        {
            if (offscreen != null) offscreen.Navigate(address);
            MarkActive();
        }

#if VVS_REFERENCED
        // ------------------------------------------------------------------
        // Frame pump
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs on the game's own render thread, so it must stay cheap and must never block. All
        /// it does is ask for a capture on a timer and swap in whichever frame has arrived; the
        /// capture itself happens on the browser's thread.
        /// </summary>
        private void Core_RenderFrame(object sender, EventArgs e)
        {
            try
            {
                if (picture == null || offscreen == null)
                    return;

                // Nothing to do while the window is closed or the Market tab is not on top.
                if (hud == null || !hud.Visible || !picture.ViewVisible)
                    return;

                int now = Environment.TickCount;
                int interval = now < activeUntil ? ActiveRefreshMs : RefreshMs;

                if (unchecked(now - lastRefresh) >= interval)
                {
                    lastRefresh = now;
                    offscreen.RequestFrame();
                }

                DrainRetired();
                CheckForLostButton();

                Bitmap fresh = offscreen.TakeFrame();

                if (fresh != null)
                {
                    PaintFrame(fresh);
                    return;
                }

                // Nothing has ever rendered: say why, rather than showing a blank rectangle.
                if (!everPainted)
                {
                    string message = offscreen.LastError != null
                        ? "The browser could not start.\n\n" + offscreen.LastError
                          + "\n\nUse the Pop Out Window instead: /market mode window"
                        : "Loading " + MarketBrowser.Host(url) + "...";

                    if (message != shownMessage)
                    {
                        shownMessage = message;
                        PaintMessage(message);
                    }
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>
        /// Puts a captured frame on the picture box.
        ///
        /// A new ACImage per frame, which is what 1.2.2 did and what actually renders. 1.3.2 tried
        /// to be cleverer - one bitmap and one texture, repainted in place and refreshed with
        /// ClearCachedBMPTexture/GenerateCachedBMPTexture - to avoid churning DirectX textures.
        /// It does not work: VVS kept drawing the texture it already had, so the tab showed
        /// whatever colour the surface had been cleared to and never changed. White in 1.3.2,
        /// dark in 1.4.1, and in both cases it looked like a browser that had failed rather than a
        /// picture that was never updated.
        ///
        /// The one thing worth keeping from that attempt is the deferred disposal below. The
        /// original code disposed the previous ACImage the instant it was replaced, freeing a
        /// DirectX texture that VVS might still be drawing from in the current frame.
        /// </summary>
        private void PaintFrame(Bitmap frame)
        {
            try
            {
                ACImage image = new ACImage(frame);

                // ACImage copies what it is given, so the capture can go straight away.
                frame.Dispose();

                Show(image);
                everPainted = true;
            }
            catch (Exception ex)
            {
                Util.LogError(ex);

                try { frame.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Writes a line of text where the page will be, for the stretch before the first frame.
        ///
        /// Without this the tab is a blank rectangle whether the browser is still starting, has
        /// failed, or is not running - three quite different situations that looked identical for
        /// several versions and cost a lot of guessing.
        /// </summary>
        private void PaintMessage(string text)
        {
            try
            {
                Rectangle r = PageRect();

                using (Bitmap bmp = new Bitmap(Math.Max(40, r.Width), Math.Max(30, r.Height),
                                               PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(BackColour);

                        using (Font font = new Font("Tahoma", 9f))
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 220, 214, 236)))
                            g.DrawString(text, font, brush,
                                         new RectangleF(12, 12, bmp.Width - 24, bmp.Height - 24));
                    }

                    Show(new ACImage(bmp));
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>Hands the picture box a new image and retires the one it replaces.</summary>
        private void Show(ACImage image)
        {
            ACImage old = currentImage;

            currentImage = image;
            picture.Image = image;

            Retire(old);
        }

        private static readonly Color BackColour = Color.FromArgb(255, 20, 18, 34);

        /// <summary>
        /// Holds a replaced image for a few frames before freeing it. Anything VVS picked up for
        /// the frame currently being drawn is long done with by then.
        /// </summary>
        private void Retire(ACImage image)
        {
            if (image == null)
                return;

            retired.Add(new Retiree(image, 10));
        }

        private void DrainRetired()
        {
            for (int i = retired.Count - 1; i >= 0; i--)
            {
                Retiree r = retired[i];

                if (--r.Frames > 0)
                {
                    retired[i] = r;
                    continue;
                }

                retired.RemoveAt(i);

                try { r.Image.Dispose(); }
                catch (Exception ex) { Util.LogError(ex); }
            }
        }

        private struct Retiree
        {
            public readonly ACImage Image;
            public int Frames;

            public Retiree(ACImage image, int frames)
            {
                Image = image;
                Frames = frames;
            }
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        private void Picture_MouseEvent(object sender, ControlMouseEventArgs e)
        {
            try
            {
                if (offscreen == null) return;

                // The picture is drawn 1:1 from a capture the same size as the control, so the
                // control's own coordinates are already the page's coordinates.
                // MouseEventType and MouseButton are nested inside ControlMouseEventArgs, so
                // they are named through it rather than imported by the using above.
                switch (e.EventType)
                {
                    case ControlMouseEventArgs.MouseEventType.MouseMove:
                        lastX = e.X;
                        lastY = e.Y;

                        offscreen.MouseMove(PagePoint(e.X), PagePoint(e.Y), leftDown);

                        // Keep the fast refresh going for as long as a drag lasts, so a dragged
                        // scrollbar tracks the pointer instead of catching up afterwards.
                        if (leftDown)
                            MarkActive();
                        break;

                    case ControlMouseEventArgs.MouseEventType.MouseDown:
                        // Clicking the page takes keyboard focus, which is what makes typing work
                        // and, just as importantly, what stops the keystrokes reaching the game.
                        // FocusControl is static: VVS tracks one focused control for the whole
                        // service, not one per window.
                        HudView.FocusControl = picture;

                        if (e.Button != ControlMouseEventArgs.MouseButton.Right)
                            leftDown = true;

                        offscreen.MouseDown(PagePoint(e.X), PagePoint(e.Y), e.Button == ControlMouseEventArgs.MouseButton.Right);
                        MarkActive();
                        break;

                    case ControlMouseEventArgs.MouseEventType.MouseUp:
                        if (e.Button != ControlMouseEventArgs.MouseButton.Right)
                            leftDown = false;

                        offscreen.MouseUp(PagePoint(e.X), PagePoint(e.Y), e.Button == ControlMouseEventArgs.MouseButton.Right);
                        MarkActive();
                        break;

                    case ControlMouseEventArgs.MouseEventType.MouseWheel:
                        // VVS reports wheel notches; CDP wants pixels, and positive scrolls down.
                        offscreen.MouseWheel(PagePoint(e.X), PagePoint(e.Y), -e.WheelAmount * 3);
                        MarkActive();
                        break;
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>
        /// Sends typing to the page.
        ///
        /// Every key is eaten while the page has focus. That is deliberate: without it, typing a
        /// search term also walks your character around, opens chat, and fires whatever hotkeys
        /// the letters happen to be bound to. Focus arrives by clicking the page and leaves by
        /// clicking anything else, so the game keeps its keyboard the rest of the time.
        /// </summary>
        private void Picture_KeyEvent(object sender, ControlKeyEventArgs e)
        {
            try
            {
                if (offscreen == null)
                    return;

                switch (e.EventType)
                {
                    case ControlKeyEventArgs.KeyEventType.KeyPress:
                        // The printable ones. VVS has already worked out the character, shift and
                        // keyboard layout included, which is why this does not touch scan codes.
                        if (!char.IsControl(e.Char))
                            offscreen.SendChar(e.Char);
                        break;

                    case ControlKeyEventArgs.KeyEventType.KeyDown:
                    case ControlKeyEventArgs.KeyEventType.KeyUp:
                    {
                        string name;
                        int vk = VirtualKey(e.ScanCode, e.IsExtendedKey, out name);

                        if (vk != 0)
                            offscreen.SendKey(e.EventType == ControlKeyEventArgs.KeyEventType.KeyDown,
                                              vk, name);
                        break;
                    }
                }

                e.Eat = true;
                MarkActive();
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>
        /// Turns a PS/2 scan code into a Windows virtual key code, for the keys that do something
        /// rather than type something.
        ///
        /// Only these need translating: VVS reports printable keys as characters already, and a
        /// character is all the browser wants for those. This covers editing and navigation inside
        /// a text field, which is what a search box needs.
        /// </summary>
        private static int VirtualKey(byte scanCode, bool extended, out string name)
        {
            // extended is taken but not needed: the navigation block and the numeric keypad share
            // scan codes and differ only by that flag, and both should do the same thing here.
            switch (scanCode)
            {
                case 0x0E: name = "Backspace";  return 8;
                case 0x0F: name = "Tab";        return 9;
                case 0x1C: name = "Enter";      return 13;
                case 0x01: name = "Escape";     return 27;
                case 0x47: name = "Home";       return 36;
                case 0x48: name = "ArrowUp";    return 38;
                case 0x49: name = "PageUp";     return 33;
                case 0x4B: name = "ArrowLeft";  return 37;
                case 0x4D: name = "ArrowRight"; return 39;
                case 0x4F: name = "End";        return 35;
                case 0x50: name = "ArrowDown";  return 40;
                case 0x51: name = "PageDown";   return 34;
                case 0x52: name = "Insert";     return 45;
                case 0x53: name = "Delete";     return 46;
                default:   name = null;         return 0;
            }
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private Rectangle PageRect()
        {
            Size client = hud.ClientArea;

            int w = Math.Max(120, client.Width - 10);
            int h = Math.Max(90, client.Height - 34);

            return new Rectangle(0, 0, w, h);
        }

        private void Hud_Resize(object sender, EventArgs e)
        {
            try
            {
                if (picture == null || page == null) return;

                Rectangle r = PageRect();
                page.SetControlRect(picture, r);

                // The capture has to match the control, or the 1:1 coordinate mapping that makes
                // clicks land in the right place stops being true.
                if (offscreen != null)
                    offscreen.Resize(r.Width, r.Height);

                MarkActive();
            }
            catch (Exception ex) { Util.LogError(ex); }
        }
#endif

        /// <summary>
        /// Ends a drag whose mouse-up never arrived.
        ///
        /// Releasing the button outside the control means VVS delivers the MouseUp somewhere else,
        /// and the page is left believing the button is still down - every later move keeps
        /// dragging whatever was grabbed. Asking Windows directly is the reliable way to notice.
        /// </summary>
        private void CheckForLostButton()
        {
            if (!leftDown)
                return;

            if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0)
                return;             // still held

            leftDown = false;
            offscreen.MouseUp(PagePoint(lastX), PagePoint(lastY), false);
            MarkActive();
        }

        private const int VK_LBUTTON = 0x01;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static string Host(string address)
        {
            try { return new Uri(address).Host; }
            catch { return address; }
        }

        /// <summary>Refresh faster for a moment, so a click or a scroll shows its result at once.</summary>
        private void MarkActive()
        {
            activeUntil = Environment.TickCount + ActiveWindowMs;
        }
    }
}
