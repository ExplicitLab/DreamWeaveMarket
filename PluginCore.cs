using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;

using Decal.Adapter;

#if VVS_REFERENCED
using VirindiViewService;
using VirindiViewService.Controls;
#endif

/*
 * DreamweaveMarket - shows market.acdreamweave.com in a window over the game.
 *
 * Two halves:
 *   this file and mainView.xml   the in-game window, registered with Virindi View Service so the
 *                                plugin appears in the VVS bar
 *   BrowserHost / MarketForm     the actual browser, a WebView2 control on its own UI thread
 *
 * The window is built directly against Virindi View Service rather than through the
 * MetaViewWrappers - see MarketView for why. Every method Decal calls into is wrapped in
 * try/catch: Decal runs inside the AC client process, so an unhandled exception here takes the
 * client down with it.
 */

namespace DreamweaveMarket
{
    [WireUpBaseEvents]
    [FriendlyName("DreamweaveMarket")]
    public class PluginCore : PluginBase
    {
        public const string MarketUrl = "https://market.acdreamweave.com";

        private MarketView ui;


        // Two ways of showing the page. Only the one in use is ever started: the pop-out window
        // runs a Chromium instance, so it is not created until something actually asks for it.
        private MarketBrowser inGame;      // VVS HudBrowser, drawn inside the game
        private BrowserHost browser;       // WebView2 window floating over the client

        // Kept so /market diag can report what actually came up.
        private bool commandsHooked;
        private string viewError;
        private string browserError;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override void Startup()
        {
            // Deliberately NOT one try/catch around the whole method. Each step is caught on its
            // own, because they are independent and a failure in one used to take out the rest:
            // with a single catch, anything that threw before the chat hook was installed left
            // /market silently doing nothing AND no window - one fault, no symptoms, no message.
            //
            // Every step also logs, so startup.txt says exactly how far it got.
            Util.Log("---- Startup " + Util.Version + " ----");

            // First, and outside everything else: without Host there is no chat output, and
            // without chat output every later failure is invisible.
            try
            {
                Globals.Init(Host, Core);
                Util.Log("host = " + (Host == null ? "NULL" : "ok")
                         + ", core = " + (Core == null ? "NULL" : "ok"));
            }
            catch (Exception ex) { Util.LogError(ex); }

            try
            {
                Settings.Load();
                Util.Log("mode = " + Settings.Mode);
            }
            catch (Exception ex) { Util.LogError(ex); }

            // 1. Chat commands first: this is the diagnostic channel. Whatever else fails,
            //    /market diag still answers and says what went wrong.
            try
            {
                Core.CommandLineText += Core_CommandLineText;
                commandsHooked = true;
                Util.Log("chat commands hooked");
            }
            catch (Exception ex) { Util.LogError(ex); }

            // 2. The window, built straight from VVS - see MarketView.
            try
            {
                ui = new MarketView();
                ui.Create();

                WireControls();

                Util.Log("view ready");
            }
            catch (Exception ex)
            {
                viewError = ex.Message;
                Util.Log("VIEW FAILED - the plugin will not appear in the Virindi bar");
                Util.LogError(ex);
            }

            // 3. Window title, resizing, and last session's geometry.
            try { InitWindow(); }
            catch (Exception ex) { Util.LogError(ex); }

            // 4. The in-game browser, if this installation has one and the mode wants it. The
            //    pop-out window is NOT started here - it costs a Chromium process, so it waits
            //    until something asks to show the page that way.
            try
            {
                SetUpInGameBrowser();
            }
            catch (Exception ex) { Util.LogError(ex); }

            try
            {
#if VVS_REFERENCED
                if (ui != null && ui.AlwaysOnTop != null)
                    ui.AlwaysOnTop.Checked = Settings.AlwaysOnTop;
#endif

                ShowMode();
                ShowZoom();
                ShowAbout();
            }
            catch (Exception ex) { Util.LogError(ex); }

            try
            {
                Util.WriteToChat("DreamweaveMarket " + Util.Version + " loaded, showing "
                                 + ModeDescription
                                 + (ui == null ? "  NO VIEW - type /market diag" : "")
                                 + ".  /market opens it, /market help for the rest.");
            }
            catch (Exception ex) { Util.LogError(ex); }

            Util.Log("---- Startup done ----");
        }

        protected override void Shutdown()
        {
            try
            {
                Core.CommandLineText -= Core_CommandLineText;

                SaveWindowGeometry();

#if VVS_REFERENCED
                // Out of the bar before the view is torn down, so nothing of ours is left in a
                // list that gets walked again after we are gone.
                try
                {
                    HudView barView = Hud;

                    if (barView != null)
                        barView.ShowInBar = false;
                }
                catch (Exception ex) { Util.LogError(ex); }

                try
                {
                    HudView hudView = Hud;

                    if (hudView != null)
                    {
                        hudView.Resize -= Window_Geometry_Changed;
                        hudView.Moved -= Window_Geometry_Changed;
                    }
                }
                catch (Exception ex) { Util.LogError(ex); }
#endif

                if (inGame != null)
                {
                    inGame.Detach();
                    inGame = null;
                }

                if (browser != null)
                {
                    browser.Dispose();
                    browser = null;
                }

                UnwireControls();

                if (ui != null)
                {
                    ui.Dispose();
                    ui = null;
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        // ------------------------------------------------------------------
        // The VVS bar icon
        // ------------------------------------------------------------------

        /// <summary>
        /// Replaces the portal.dat icon named in mainView.xml with the plugin's own bitmap.
        ///
        /// The XML attribute can only name an id that already exists in the game's data, so every
        /// plugin using it borrows some existing item's art and two plugins can easily end up
        /// wearing the same picture. VVS will take a Bitmap instead, which is the only way to get
        /// an icon that is actually ours. Failure here is not fatal - the view keeps the default
        /// icon from the XML.
        /// </summary>
#if VVS_REFERENCED
        /// <summary>The window, or null if it could not be built.</summary>
        private HudView Hud
        {
            get { return ui == null ? null : ui.View; }
        }
#endif

        // ------------------------------------------------------------------
        // Control events
        //
        // VVS raises Hit for a button press and Change for a checkbox; both are plain
        // EventHandler. Wired here rather than by attribute, since there is no wrapper doing it.
        // ------------------------------------------------------------------

        private void WireControls()
        {
#if VVS_REFERENCED
            if (ui.OpenButton != null)     ui.OpenButton.Hit += OpenButton_Hit;
            if (ui.ReloadButton != null)   ui.ReloadButton.Hit += ReloadButton_Hit;
            if (ui.ExternalButton != null) ui.ExternalButton.Hit += ExternalButton_Hit;
            if (ui.ZoomIn != null)         ui.ZoomIn.Hit += ZoomIn_Hit;
            if (ui.ZoomOut != null)        ui.ZoomOut.Hit += ZoomOut_Hit;
            if (ui.ZoomReset != null)      ui.ZoomReset.Hit += ZoomReset_Hit;
            if (ui.ModeInGame != null)     ui.ModeInGame.Change += ModeBox_Change;
            if (ui.ModeWindow != null)     ui.ModeWindow.Change += ModeBox_Change;
            if (ui.AlwaysOnTop != null)    ui.AlwaysOnTop.Change += AlwaysOnTop_Change;
#endif
        }

        private void UnwireControls()
        {
#if VVS_REFERENCED
            try
            {
                if (ui == null) return;

                if (ui.OpenButton != null)     ui.OpenButton.Hit -= OpenButton_Hit;
                if (ui.ReloadButton != null)   ui.ReloadButton.Hit -= ReloadButton_Hit;
                if (ui.ExternalButton != null) ui.ExternalButton.Hit -= ExternalButton_Hit;
                if (ui.ZoomIn != null)         ui.ZoomIn.Hit -= ZoomIn_Hit;
                if (ui.ZoomOut != null)        ui.ZoomOut.Hit -= ZoomOut_Hit;
                if (ui.ZoomReset != null)      ui.ZoomReset.Hit -= ZoomReset_Hit;
                if (ui.ModeInGame != null)     ui.ModeInGame.Change -= ModeBox_Change;
                if (ui.ModeWindow != null)     ui.ModeWindow.Change -= ModeBox_Change;
                if (ui.AlwaysOnTop != null)    ui.AlwaysOnTop.Change -= AlwaysOnTop_Change;
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        private void OpenButton_Hit(object sender, EventArgs e)
        {
            try { ShowMarket(); }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private void ReloadButton_Hit(object sender, EventArgs e)
        {
            try
            {
                if (UsingInGame && inGame != null && inGame.Attached)
                    inGame.Reload();
                else if (browser != null)
                    browser.Reload();
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private void ExternalButton_Hit(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(MarketUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Util.LogError(ex);
            }
        }

        private void ZoomIn_Hit(object sender, EventArgs e)
        {
            try { SetZoom(Settings.ZoomPercent + ZoomStep); }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private void ZoomOut_Hit(object sender, EventArgs e)
        {
            try { SetZoom(Settings.ZoomPercent - ZoomStep); }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private void ZoomReset_Hit(object sender, EventArgs e)
        {
            try { SetZoom(100); }
            catch (Exception ex) { Util.LogError(ex); }
        }

        // ------------------------------------------------------------------
        // Zoom
        // ------------------------------------------------------------------

        private const int ZoomStep = 10;

        /// <summary>
        /// Sets the page zoom, on whichever browsers exist, and remembers it.
        ///
        /// This is the answer to the market's own layout being wider than a window inside the
        /// game: zooming out fits more of the page in, at the cost of smaller text. The site is
        /// laid out for a desktop browser and has no idea it is being shown in a 640-pixel box.
        /// </summary>
        private void SetZoom(int percent)
        {
            percent = Settings.Clamp(percent);
            Settings.ZoomPercent = percent;

            double factor = percent / 100.0;

            if (inGame != null)
                inGame.SetZoom(factor);

            if (browser != null)
                browser.SetZoom(factor);

            ShowZoom();
            Util.Log("zoom set to " + percent + "%");
        }

        /// <summary>Fills in the version on the About page.</summary>
        private void ShowAbout()
        {
#if VVS_REFERENCED
            try
            {
                if (ui != null && ui.AboutVersion != null)
                    ui.AboutVersion.Text = "Version " + Util.Version;
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        private void ShowZoom()
        {
#if VVS_REFERENCED
            try
            {
                if (ui != null && ui.ZoomLabel != null)
                    ui.ZoomLabel.Text = "Zoom: " + Settings.ZoomPercent + "%";
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        /// <summary>
        /// The two mode checkboxes, kept mutually exclusive so they behave as radio buttons.
        ///
        /// Unticking the one that is already on would leave neither selected and no way to know
        /// what to show, so that is treated as no change and the tick is put straight back.
        /// </summary>
        private void ModeBox_Change(object sender, EventArgs e)
        {
#if VVS_REFERENCED
            try
            {
                if (settingModeBoxes)
                    return;

                bool wantInGame;

                if (ReferenceEquals(sender, ui.ModeInGame))
                    wantInGame = ui.ModeInGame.Checked;
                else if (ReferenceEquals(sender, ui.ModeWindow))
                    wantInGame = !ui.ModeWindow.Checked;
                else
                    return;

                if (wantInGame == UsingInGame)
                {
                    // Clicking the box that is already on: nothing changes, but the tick it just
                    // removed has to go back.
                    ShowMode();
                    return;
                }

                SetMode(wantInGame ? DisplayMode.InGame : DisplayMode.Window);
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        /// <summary>Guard against the Change events raised by ShowMode setting the boxes itself.</summary>
        private bool settingModeBoxes;

        private void AlwaysOnTop_Change(object sender, EventArgs e)
        {
#if VVS_REFERENCED
            try
            {
                bool on = ui.AlwaysOnTop != null && ui.AlwaysOnTop.Checked;

                Settings.AlwaysOnTop = on;

                if (browser != null)
                    browser.SetTopMost(on);
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        // ------------------------------------------------------------------
        // The window: title, drag, resize, remembered geometry
        // ------------------------------------------------------------------

        private const int MinWidth = 360;
        private const int MinHeight = 260;

        /// <summary>
        /// Makes the window resizable and restores where it was last time.
        ///
        /// The MetaView wrappers never set UserResizeable - VVS supports it, nothing asks for it -
        /// so a window built from XML comes up locked at the size the XML gave it. Dragging by the
        /// title bar works without any of this; the resize grip does not appear until the flag is
        /// on. The page inside follows on its own: the notebook has no rectangle in the XML so it
        /// fills the client area, and the browser surface is sized from ClientArea on every
        /// Resize.
        /// </summary>
        private void InitWindow()
        {
#if VVS_REFERENCED
            HudView hud = Hud;

            if (hud == null)
                return;

            hud.UserResizeable = true;
            hud.MinimumClientArea = new Size(MinWidth, MinHeight);

            // Saved as it happens, throttled: Shutdown is not reached if the client crashes, and
            // losing the window size to a crash is the sort of small annoyance that makes a
            // plugin feel unreliable.
            hud.Resize += Window_Geometry_Changed;
            hud.Moved += Window_Geometry_Changed;

            Util.Log("window made resizable");

            // Geometry is NOT restored here. Startup runs before the character is in the world and
            // before VVS has finished bringing the window up; assigning ClientArea at that point
            // reaches into VVS's own layout too early, and an exception raised inside VVS there
            // does not stay inside this plugin - it takes the bar, and every other plugin's window
            // with it. Restored at LoginComplete instead, which is the same point RezTools uses.
#endif
        }

#if VVS_REFERENCED
        private bool geometryRestored;

        private void RestoreGeometry(HudView hud)
        {
            if (geometryRestored)
                return;

            geometryRestored = true;

            // Size and position are applied in separate try blocks on purpose: if one of them is
            // going to upset VVS, it should not also cost us the other, and neither should ever
            // cost us the window.
            try
            {
                Size size = Settings.WindowSize;

                if (size.Width >= MinWidth && size.Height >= MinHeight)
                {
                    // MaximumClientArea is whatever VVS will allow at this resolution; asking for
                    // more than that is refused rather than clamped, so clamp here.
                    Size cap = hud.MaximumClientArea;

                    if (cap.Width > 0 && size.Width > cap.Width) size.Width = cap.Width;
                    if (cap.Height > 0 && size.Height > cap.Height) size.Height = cap.Height;

                    hud.ClientArea = size;
                    Util.Log("window size restored to " + size);
                }
            }
            catch (Exception ex) { Util.LogError(ex); }

            try
            {
                Point loc = Settings.WindowLocation;

                // A saved position from a monitor that is no longer attached would put the window
                // somewhere it can never be dragged back from, so only restore one still on screen.
                if (loc.X != int.MinValue && OnAScreen(loc))
                {
                    hud.Location = loc;
                    Util.Log("window position restored to " + loc);
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private static bool OnAScreen(Point p)
        {
            try
            {
                Rectangle desktop = System.Windows.Forms.SystemInformation.VirtualScreen;

                // The title bar has to land on a screen; a little overhang is fine.
                return p.X > desktop.Left - 200 && p.X < desktop.Right - 40
                    && p.Y > desktop.Top - 8 && p.Y < desktop.Bottom - 40;
            }
            catch { return false; }
        }
#endif

        private int lastGeometrySave;

        private void Window_Geometry_Changed(object sender, EventArgs e)
        {
            try
            {
                int now = Environment.TickCount;

                // Resize and Moved fire continuously while a window is being dragged; this file
                // does not need writing sixty times a second.
                if (unchecked(now - lastGeometrySave) < 3000)
                    return;

                lastGeometrySave = now;
                SaveWindowGeometry();
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private void SaveWindowGeometry()
        {
#if VVS_REFERENCED
            try
            {
                HudView hud = Hud;

                if (hud == null)
                    return;

                Size area = hud.ClientArea;

                if (area.Width >= MinWidth && area.Height >= MinHeight)
                    Settings.WindowSize = area;

                Settings.WindowLocation = hud.Location;
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        /// <summary>
        /// Restores last session's window size and position, once the client is actually in the
        /// world. See the note in InitWindow for why this is not done at startup.
        /// </summary>
        [BaseEvent("LoginComplete", "CharacterFilter")]
        private void CharacterFilter_LoginComplete(object sender, EventArgs e)
        {
#if VVS_REFERENCED
            try
            {
                HudView hud = Hud;

                if (hud != null)
                    RestoreGeometry(hud);
            }
            catch (Exception ex) { Util.LogError(ex); }
#endif
        }

        // ------------------------------------------------------------------
        // Display modes
        // ------------------------------------------------------------------

        /// <summary>Whether the page is currently shown in-game rather than in a pop-out window.</summary>
        private bool UsingInGame
        {
            get { return Settings.Mode == DisplayMode.InGame && HasVvsView; }
        }

        private bool HasVvsView
        {
            get
            {
#if VVS_REFERENCED
                return Hud != null;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Creates the in-game browser and puts it on the Market page, or leaves a note there
        /// saying why it could not. Safe to call more than once.
        /// </summary>
        private void SetUpInGameBrowser()
        {
            if (!UsingInGame)
            {
                SetNotice("The market is set to open in a Pop Out Window - the Open Market "
                          + "button, or /market. Use \"Show In Game\" to draw it here instead.");
                return;
            }

            if (inGame != null && inGame.Attached)
                return;

            inGame = new MarketBrowser(MarketUrl);

            object hud = null;
#if VVS_REFERENCED
            hud = Hud;
#endif

            if (inGame.Attach(hud, "marketPage"))
            {
                SetNotice("");
            }
            else
            {
                inGame = null;
                SetNotice("The In Game view could not start - see the log. Use the Pop Out "
                          + "Window instead: /market mode window.");
            }
        }

        /// <summary>Starts the Pop Out Window on demand. It costs a Chromium process, so it is
        /// created the first time it is actually needed rather than at startup.</summary>
        private BrowserHost EnsureWindow()
        {
            if (browser != null)
                return browser;

            try
            {
                browser = new BrowserHost(MarketUrl);
                browser.SetTopMost(Settings.AlwaysOnTop);
                browser.SetZoom(Settings.ZoomPercent / 100.0);
                Util.Log("pop out window browser started");
            }
            catch (Exception ex)
            {
                browserError = ex.Message;
                Util.LogError(ex);
            }

            return browser;
        }

        /// <summary>Shows the market page whichever way the current mode calls for.</summary>
        private void ShowMarket()
        {
            if (UsingInGame && inGame != null && inGame.Attached)
            {
#if VVS_REFERENCED
                // Bring the Market page to the front of the plugin's own window.
                if (ui.Tabs != null)
                    ui.Tabs.CurrentTab = 0;

                if (Hud != null)
                    Hud.Visible = true;
#endif

                return;
            }

            BrowserHost b = EnsureWindow();

            if (b == null)
            {
                return;
            }

            b.Toggle();
        }

        private void SetMode(DisplayMode mode)
        {
            Settings.Mode = mode;
            Util.Log("display mode set to " + mode);

            if (UsingInGame)
            {
                SetUpInGameBrowser();
            }
            else if (inGame != null)
            {
                // Tear the in-game control down rather than leaving it drawing behind a tab
                // nobody is looking at - it is a whole browser engine.
                inGame.Detach();
                inGame = null;
                SetNotice("Showing the market in a Pop Out Window.");
            }

            ShowMode();
            Util.WriteToChat("Now showing the market " + ModeDescription);
        }

        private string ModeDescription
        {
            get { return UsingInGame ? "in game" : "in a Pop Out Window"; }
        }

        private void ShowMode()
        {
            try
            {
#if VVS_REFERENCED
                if (ui == null)
                    return;

                if (ui.ModeLabel != null)
                    ui.ModeLabel.Text = "Showing the market " + ModeDescription;

                settingModeBoxes = true;
                try
                {
                    if (ui.ModeInGame != null) ui.ModeInGame.Checked = UsingInGame;
                    if (ui.ModeWindow != null) ui.ModeWindow.Checked = !UsingInGame;
                }
                finally { settingModeBoxes = false; }
#endif
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private void SetNotice(string message)
        {
            try
            {
#if VVS_REFERENCED
                if (ui != null && ui.MarketNotice != null)
                    ui.MarketNotice.Text = message;
#endif
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        // ------------------------------------------------------------------
        // Chat commands
        // ------------------------------------------------------------------

        private void Core_CommandLineText(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                string text = e.Text == null ? "" : e.Text.Trim();

                if (text.Length == 0 || !text.StartsWith("/market", StringComparison.OrdinalIgnoreCase))
                    return;

                string arg = text.Substring("/market".Length).Trim().ToLowerInvariant();

                // Commands that take an argument, handled before the exact-match switch below.
                if (arg.StartsWith("zoom "))
                {
                    e.Eat = true;

                    int percent;
                    string zoomText = arg.Substring(5).Trim().TrimEnd('%');

                    if (int.TryParse(zoomText, out percent))
                        SetZoom(percent);
                    else
                        Util.WriteToChat("Give a percentage, e.g. /market zoom 80");

                    return;
                }

                if (arg.StartsWith("url "))
                {
                    e.Eat = true;
                    NavigateTo(arg.Substring(4).Trim());
                    return;
                }

                switch (arg)
                {
                    case "":
                        e.Eat = true;
                        ShowMarket();
                        break;

                    case "reload":
                        e.Eat = true;
                        if (UsingInGame && inGame != null && inGame.Attached)
                            inGame.Reload();
                        else if (browser != null)
                            browser.Reload();
                        Util.WriteToChat("Reloading " + ShortUrl);
                        break;

                    case "mode":
                        e.Eat = true;
                        Util.WriteToChat("Showing the market " + ModeDescription);
                        Util.WriteToChat("  /market mode ingame|window to change it");
                        break;

                    case "mode auto":
                        e.Eat = true;
                        Util.WriteToChat("There is no auto mode any more - it only ever meant "
                                         + "in-game. Switching to in-game.");
                        SetMode(DisplayMode.InGame);
                        break;

                    case "mode ingame":
                        e.Eat = true;
                        SetMode(DisplayMode.InGame);
                        break;

                    case "mode window":
                    case "mode separate":
                        e.Eat = true;
                        SetMode(DisplayMode.Window);
                        break;

                    case "version":
                        e.Eat = true;
                        Util.WriteToChat("DreamweaveMarket " + Util.Version);
                        break;

                    case "web":
                        e.Eat = true;
                        Process.Start(new ProcessStartInfo(MarketUrl) { UseShellExecute = true });
                        break;

                    case "zoom":
                        e.Eat = true;
                        Util.WriteToChat("Zoom " + Settings.ZoomPercent
                                         + "%  -  /market zoom <percent> to change it (50-250)");
                        break;

                    case "icons":
                        e.Eat = true;
                        ReportIcons();
                        break;

                    case "reset":
                        e.Eat = true;
                        Settings.Reset();
                        Util.WriteToChat("Settings cleared. Reload the plugin or restart the client.");
                        break;

                    case "diag":
                        e.Eat = true;
                        ReportDiagnostics();
                        break;

                    default:
                        // Anything else starting with /market: answer, rather than silently
                        // doing nothing and looking like the plugin is dead.
                        if (arg != "help")
                        {
                            e.Eat = true;
                            Util.WriteToChat("Unknown command \"" + arg + "\". /market help lists them.");
                            break;
                        }
                        goto case "help";

                    case "help":
                        e.Eat = true;
                        Util.WriteToChat("/market - show or hide the window");
                        Util.WriteToChat("/market reload - reload the page");
                        Util.WriteToChat("/market mode - ingame or window");
                        Util.WriteToChat("/market zoom <percent> - scale the page, 50 to 250");
                        Util.WriteToChat("/market web - open it in your normal browser");
                        Util.WriteToChat("/market version - which build this is");
                        Util.WriteToChat("/market diag - what loaded and what did not");
                        Util.WriteToChat("/market reset - clear saved settings and window geometry");
                        Util.WriteToChat("/market icons - list every Virindi window and its icon type");
                        Util.WriteToChat("/market url <address> - load any address in-game");
                        break;
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Says which parts of the plugin came up. The first thing to reach for when the window is
        /// missing from the Virindi bar or nothing happens on /market.
        /// </summary>
        private void ReportDiagnostics()
        {
            try
            {
                Util.WriteToChat("DreamweaveMarket " + Util.Version);
                Util.WriteToChat("  commands: " + (commandsHooked ? "hooked" : "NOT hooked"));

                if (ui == null)
                    Util.WriteToChat("  view: NONE" + (viewError == null ? "" : " - " + viewError));
                else
                    Util.WriteToChat("  view: built");

#if VVS_REFERENCED
                bool vvs = false;
                try { vvs = VirindiViewService.Service.Running; }
                catch (Exception ex) { Util.LogError(ex); }

                Util.WriteToChat("  Virindi View Service: " + (vvs ? "running" : "NOT running"));
#else
                Util.WriteToChat("  Virindi View Service: not compiled in");
#endif

                Util.WriteToChat("  mode: " + ModeDescription);

                Util.WriteToChat("  in-game surface: "
                                 + (inGame != null && inGame.Attached ? "attached" : "not attached")
                                 + (inGame == null ? "" : " - " + inGame.Status));

                if (browser == null)
                    Util.WriteToChat("  pop out window: not started");
                else
                    Util.WriteToChat("  pop out window: " + browser.State);

                Util.WriteToChat("  log: " + Util.LogPath);
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>
        /// Points the in-game browser at an arbitrary address. The engine gives almost no error
        /// reporting, so loading a known-good page by hand is the practical way to tell a broken
        /// engine apart from a page it cannot fetch.
        /// </summary>
        private void NavigateTo(string address)
        {
            try
            {
                if (address.Length == 0)
                    return;

                if (inGame == null || !inGame.Attached)
                {
                    Util.WriteToChat("The in-game browser is not attached - nothing to navigate.");
                    return;
                }

                if (address.IndexOf("://", StringComparison.Ordinal) < 0)
                    address = "http://" + address;

                inGame.Navigate(address);
                Util.WriteToChat("Loading " + address + " in-game. /market diag reports the title.");
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        /// <summary>
        /// Lists every Virindi window and what kind of icon it carries.
        ///
        /// Written while chasing a crash inside VVS's own bar: ACImage.Clone() calls
        /// new Bitmap(BitmapImage) and only an icon whose data really is a bitmap survives that,
        /// so an icon of any other kind is a window waiting to take the bar down. This says which
        /// windows those are.
        /// </summary>
        private void ReportIcons()
        {
#if VVS_REFERENCED
            try
            {
                System.Collections.ObjectModel.ReadOnlyCollection<HudView> views
                    = HudView.GetAllViews();

                Util.WriteToChat("Virindi windows: " + views.Count);

                foreach (HudView v in views)
                {
                    string title;
                    string kind;
                    bool inBar;

                    try { title = string.IsNullOrEmpty(v.Title) ? "(untitled)" : v.Title; }
                    catch { title = "(unreadable)"; }

                    try { inBar = v.ShowInBar; }
                    catch { inBar = false; }

                    try
                    {
                        ACImage icon = v.Icon;

                        if (icon == null)
                            kind = "none";
                        else
                            kind = icon.ImageDataType.ToString()
                                   + (icon.ImageDataType == ACImage.eACImageUnderlyingType.Bitmap
                                      && icon.BitmapImage == null ? " (BITMAP IS NULL)" : "");
                    }
                    catch (Exception ex) { kind = "unreadable: " + ex.Message; }

                    Util.WriteToChat("  " + (inBar ? "[bar] " : "      ") + title + " - " + kind);
                }
            }
            catch (Exception ex)
            {
                Util.LogError(ex);
                Util.WriteToChat("Could not list the windows - see the log.");
            }
#else
            Util.WriteToChat("Not built against Virindi View Service.");
#endif
        }

        private static string ShortUrl
        {
            get { return MarketUrl.Replace("https://", ""); }
        }
    }
}
