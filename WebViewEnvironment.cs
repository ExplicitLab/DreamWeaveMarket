using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace DreamweaveMarket
{
    /// <summary>
    /// The one place a WebView2 environment is described, because the two browsers in this plugin
    /// have to describe it identically.
    ///
    /// WebView2 allows several controls in one process to share a user data folder - they attach
    /// to the same browser process, which is how the in-game surface and the pop-out window can
    /// share a login. What it does not allow is two environments over the same folder created with
    /// DIFFERENT options. The second one fails with 0x8007139F, ERROR_INVALID_STATE, and every
    /// browser after that in the process is dead.
    ///
    /// That is exactly what happened when the offscreen renderer asked for the occlusion flags it
    /// needs and the pop-out window asked for nothing: whichever started second got nothing at
    /// all, and the failure looked like a broken WebView2 install rather than a mismatch. So both
    /// come through here, and the arguments below apply to both whether or not each needs them.
    /// </summary>
    internal static class WebViewEnvironment
    {
        /// <summary>
        /// Chromium command line shared by every browser this plugin starts.
        ///
        /// These keep a window rendering when Windows believes nobody can see it, which the
        /// offscreen renderer depends on - it deliberately puts its window off the edge of the
        /// desktop. Harmless for the pop-out window, and it must carry them too: matching options
        /// is the requirement, not needing them.
        /// </summary>
        public const string BrowserArguments =
            "--disable-features=CalculateNativeWinOcclusion "
            + "--disable-backgrounding-occluded-windows "
            + "--disable-renderer-backgrounding "
            + "--disable-background-timer-throttling";

        /// <summary>
        /// Cookies, logins and cache. Under LocalAppData rather than beside the plugin, which is
        /// usually not writable, and shared by both browsers so a login works in either.
        /// </summary>
        public static string DataFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DreamweaveMarket", "WebView2");
            }
        }

        public static Task<CoreWebView2Environment> CreateAsync()
        {
            string dir = DataFolder;
            Directory.CreateDirectory(dir);

            return CoreWebView2Environment.CreateAsync(
                null, dir, new CoreWebView2EnvironmentOptions(BrowserArguments));
        }
    }
}
