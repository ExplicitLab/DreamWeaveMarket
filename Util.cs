using System;
using System.IO;
using System.Reflection;

namespace DreamweaveMarket
{
    /// <summary>Chat output, version, and the error log. Nothing here may throw.</summary>
    public static class Util
    {
        /// <summary>Three-part version read from the assembly, e.g. "1.0.2".</summary>
        public static string Version
        {
            get
            {
                try
                {
                    Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v == null ? "?" : v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "?"; }
            }
        }

        /// <summary>Where the error log goes: alongside the other Decal plugins' data.</summary>
        public static string DataFolder
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Decal Plugins");
                return Path.Combine(root, "DreamweaveMarket");
            }
        }

        /// <summary>
        /// Writes a line to the chat window.
        ///
        /// Through Globals.Host, NOT CoreManager.Current: plugins run in their own AppDomain,
        /// where that static is not reliably populated, and a null there means every message is
        /// swallowed by the catch below and the plugin looks like it is doing nothing at all.
        /// Core.Actions is tried as a fallback in case Init has not run yet.
        /// </summary>
        public static void WriteToChat(string message)
        {
            string line = "[Market] " + message;

            try
            {
                if (Globals.Host != null)
                {
                    Globals.Host.Actions.AddChatText(line, 5);
                    return;
                }
            }
            catch (Exception ex) { Log("chat via Host failed: " + ex.Message); }

            try
            {
                if (Globals.Core != null)
                {
                    Globals.Core.Actions.AddChatText(line, 5);
                    return;
                }
            }
            catch (Exception ex) { Log("chat via Core failed: " + ex.Message); }

            Log("NO CHAT OUTPUT AVAILABLE: " + message);
        }

        /// <summary>
        /// Appends to the error log. Decal runs inside the AC client process, so an exception that
        /// escapes a plugin takes the client down with it - every entry point catches and lands here.
        /// </summary>
        public static void LogError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ERROR" + Environment.NewLine
                    + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>The file Log() and LogError() write to. Reported by /market diag.</summary>
        public static string LogPath
        {
            get { return Path.Combine(DataFolder, "log.txt"); }
        }

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
