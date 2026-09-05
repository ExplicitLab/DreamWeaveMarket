using System;
using System.Drawing;
using System.IO;
using System.Reflection;

#if VVS_REFERENCED
using VirindiViewService;
using VirindiViewService.Controls;
using VirindiViewService.XMLParsers;
#endif

namespace DreamweaveMarket
{
    /// <summary>
    /// Builds the plugin's Virindi window, straight from VVS rather than through the
    /// MetaViewWrappers.
    ///
    /// The wrappers were the obvious way in - they are what the sample plugin uses - but they
    /// construct the HudView themselves and never expose the ViewProperties that go into it. That
    /// matters for exactly one thing, and it turned out to be the thing that kept crashing the
    /// bar: the icon has to be set ON THE PROPERTIES, before the HudView exists, so the bar has
    /// the final icon the moment the window registers. Assigning hud.Icon afterwards leaves the
    /// bar holding whatever the XML gave it, and it dies later, cloning an icon that no longer
    /// makes sense - inside VVS, in some other plugin's stack.
    ///
    /// Doing it directly is also less code than the layer it replaces.
    /// </summary>
    public sealed class MarketView : IDisposable
    {
#if VVS_REFERENCED
        public HudView View { get; private set; }

        public HudTabView Tabs { get; private set; }
        public HudFixedLayout MarketPage { get; private set; }
        public HudButton OpenButton { get; private set; }
        public HudButton ReloadButton { get; private set; }
        public HudButton ExternalButton { get; private set; }
        public HudCheckBox ModeInGame { get; private set; }
        public HudCheckBox ModeWindow { get; private set; }
        public HudButton ZoomIn { get; private set; }
        public HudButton ZoomOut { get; private set; }
        public HudButton ZoomReset { get; private set; }
        public HudStaticText ZoomLabel { get; private set; }
        public HudCheckBox AlwaysOnTop { get; private set; }
        public HudStaticText AboutVersion { get; private set; }
        public HudStaticText ModeLabel { get; private set; }
        public HudStaticText MarketNotice { get; private set; }

        /// <summary>Builds the window. Throws if the XML or a named control is missing.</summary>
        public void Create()
        {
            ViewProperties properties;
            ControlGroup controls;

            new Decal3XMLParser().ParseFromResource("DreamweaveMarket.mainView.xml",
                                                    out properties, out controls);

            properties.Title = "Dreamweave Market " + Util.Version;
            properties.ShowInBar = true;

            // Before the HudView is constructed - see the note at the top of this file.
            ACImage icon = LoadIcon();

            if (icon != null)
                properties.Icon = icon;

            View = new HudView(properties, controls);

            Tabs         = controls["MainTabs"]      as HudTabView;
            MarketPage   = controls["marketPage"]    as HudFixedLayout;
            OpenButton   = controls["OpenButton"]    as HudButton;
            ReloadButton = controls["ReloadButton"]  as HudButton;
            ExternalButton = controls["ExternalButton"] as HudButton;
            ModeInGame   = controls["ModeInGame"]    as HudCheckBox;
            ModeWindow   = controls["ModeWindow"]    as HudCheckBox;
            ZoomIn       = controls["ZoomIn"]        as HudButton;
            ZoomOut      = controls["ZoomOut"]       as HudButton;
            ZoomReset    = controls["ZoomReset"]     as HudButton;
            ZoomLabel    = controls["ZoomLabel"]     as HudStaticText;
            AlwaysOnTop  = controls["AlwaysOnTop"]   as HudCheckBox;
            AboutVersion = controls["AboutVersion"]  as HudStaticText;
            ModeLabel    = controls["ModeLabel"]     as HudStaticText;
            MarketNotice = controls["MarketNotice"]  as HudStaticText;

            Util.Log("view created" + (icon == null ? " (no icon)" : " with bitmap icon"));
        }

        /// <summary>
        /// Reads the embedded badge into an ACImage, or returns null to leave whatever the XML
        /// specified.
        ///
        /// The source bitmap is disposed straight away: ACImage copies what it is given, which is
        /// what the working plugins on this server rely on. Holding the bitmap open afterwards is
        /// unnecessary, and reading it from a stream WITHOUT copying - new Bitmap(stream), which
        /// keeps a reference to a stream that is about to close - is how you get an icon that
        /// draws as a flat coloured square and takes the bar down later.
        /// </summary>
        private static ACImage LoadIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("DreamweaveMarket.Resources.icon.png"))
                {
                    if (s == null)
                    {
                        Util.Log("embedded icon missing");
                        return null;
                    }

                    using (Image loaded = Image.FromStream(s))
                    using (Bitmap bmp = new Bitmap(loaded))
                        return new ACImage(bmp);
                }
            }
            catch (Exception ex)
            {
                Util.LogError(ex);
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (View != null)
                {
                    View.Dispose();
                    View = null;
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }
#else
        public void Create() { }
        public void Dispose() { }
#endif
    }
}
