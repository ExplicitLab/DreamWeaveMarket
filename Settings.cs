using System;
using System.Collections.Generic;
using System.IO;

namespace DreamweaveMarket
{
    /// <summary>
    /// How the market page is shown. Two modes, because there are two behaviours.
    ///
    /// There used to be a third, Auto, from when the in-game surface was VVS's own browser control
    /// and might not exist on a given install. Once the in-game surface became a picture box fed
    /// by our own browser, it worked wherever the window did - so Auto always resolved to InGame,
    /// and the setting offered three choices for two outcomes. Settings files saying "Auto" are
    /// read as InGame.
    /// </summary>
    public enum DisplayMode
    {
        /// <summary>Drawn in the plugin's own Virindi window, on the Market page.</summary>
        InGame,
        /// <summary>A separate desktop window floating over the client.</summary>
        Window
    }

    /// <summary>
    /// A handful of remembered choices, in a plain key=value file. Small enough that a settings
    /// format would cost more than it saves, and readable enough to fix by hand.
    /// </summary>
    public static class Settings
    {
        private static readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string Path_
        {
            get { return Path.Combine(Util.DataFolder, "settings.txt"); }
        }

        public static DisplayMode Mode
        {
            get
            {
                try
                {
                    string text = Get("mode", "InGame");

                    // Settings written before the modes were collapsed to two.
                    if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                        return DisplayMode.InGame;

                    return (DisplayMode)Enum.Parse(typeof(DisplayMode), text, true);
                }
                catch { return DisplayMode.InGame; }
            }
            set { Set("mode", value.ToString()); }
        }

        /// <summary>
        /// Remembered window geometry. Stored as plain "w,h" and "x,y" so the file stays
        /// hand-editable, and treated as a suggestion - a size below the minimum or a position
        /// off every monitor is ignored rather than restored.
        /// </summary>
        public static System.Drawing.Size WindowSize
        {
            get { return ParseSize(Get("windowsize", "")); }
            set { Set("windowsize", value.Width + "," + value.Height); }
        }

        public static System.Drawing.Point WindowLocation
        {
            get
            {
                System.Drawing.Size s = ParseSize(Get("windowpos", ""));
                return s.IsEmpty
                    ? new System.Drawing.Point(int.MinValue, int.MinValue)
                    : new System.Drawing.Point(s.Width, s.Height);
            }
            set { Set("windowpos", value.X + "," + value.Y); }
        }

        private static System.Drawing.Size ParseSize(string text)
        {
            try
            {
                if (text == null) return System.Drawing.Size.Empty;

                string[] parts = text.Split(',');
                if (parts.Length != 2) return System.Drawing.Size.Empty;

                int a, b;
                if (!int.TryParse(parts[0].Trim(), out a)) return System.Drawing.Size.Empty;
                if (!int.TryParse(parts[1].Trim(), out b)) return System.Drawing.Size.Empty;

                return new System.Drawing.Size(a, b);
            }
            catch { return System.Drawing.Size.Empty; }
        }

        /// <summary>
        /// The portal.dat icon id shown in the Virindi bar. Changeable without a rebuild -
        /// /market iconid &lt;number&gt; - because picking one means browsing the game's own art,
        /// and there is no list of them worth hard-coding.
        ///
        /// 6112 (0x17E0) is the Ice Heaume of Frore: a known-good id, so a mistyped one can always
        /// be undone with /market iconid 6112.
        /// </summary>
        public static int IconId
        {
            get
            {
                int v;
                return int.TryParse(Get("iconid", "6112"), out v) && v > 0 ? v : 6112;
            }
            set { Set("iconid", value.ToString()); }
        }

        /// <summary>
        /// Page zoom, as a percentage. Stored as a whole number so the file stays readable and
        /// there is no decimal-separator trouble on a non-English Windows.
        ///
        /// 80% by default: the market is laid out for a desktop browser, and at 100% a window
        /// sized to sit comfortably in the game is too narrow for its filter row and its
        /// dropdowns. 80% is the point where the page fits without the text getting small.
        /// </summary>
        public static int ZoomPercent
        {
            get
            {
                int v;
                if (!int.TryParse(Get("zoom", "80"), out v)) return 80;
                return Clamp(v);
            }
            set { Set("zoom", Clamp(value).ToString()); }
        }

        public static int Clamp(int percent)
        {
            if (percent < 50) return 50;
            if (percent > 250) return 250;
            return percent;
        }

        public static bool AlwaysOnTop
        {
            get { return Get("alwaysontop", "true") == "true"; }
            set { Set("alwaysontop", value ? "true" : "false"); }
        }

        /// <summary>Throws every remembered value away. The way out when a saved window
        /// position or mode is itself the problem.</summary>
        public static void Reset()
        {
            try
            {
                values.Clear();

                if (File.Exists(Path_))
                    File.Delete(Path_);

                Util.Log("settings reset");
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        public static void Load()
        {
            try
            {
                values.Clear();

                if (!File.Exists(Path_)) return;

                foreach (string line in File.ReadAllLines(Path_))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;

                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;

                    values[s.Substring(0, eq).Trim()] = s.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Util.DataFolder);

                List<string> lines = new List<string>();
                lines.Add("# DreamweaveMarket settings");

                foreach (KeyValuePair<string, string> kv in values)
                    lines.Add(kv.Key + "=" + kv.Value);

                File.WriteAllLines(Path_, lines.ToArray());
            }
            catch (Exception ex) { Util.LogError(ex); }
        }

        private static string Get(string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        private static void Set(string key, string value)
        {
            values[key] = value;
            Save();
        }
    }
}
