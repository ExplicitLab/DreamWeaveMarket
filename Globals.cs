using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DreamweaveMarket
{
    /// <summary>
    /// The two objects Decal hands the plugin at startup, kept where the rest of the code can
    /// reach them.
    ///
    /// Host in particular matters: Decal runs plugins in their own AppDomain, and the static
    /// CoreManager.Current is not reliably populated in it. Anything reached through Current can
    /// therefore fail - silently, since chat output is wrapped in try/catch - while the same call
    /// through the PluginHost passed to Startup works. Chat output goes through Host.Actions for
    /// exactly that reason.
    /// </summary>
    public static class Globals
    {
        public static PluginHost Host { get; private set; }
        public static CoreManager Core { get; private set; }

        public static void Init(PluginHost host, CoreManager core)
        {
            Host = host;
            Core = core;
        }
    }
}
