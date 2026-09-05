# DreamweaveMarket

A Decal plugin for Asheron's Call that shows <https://market.acdreamweave.com> either **inside the
game** or in a pop-out window on top of it, with an entry in the Virindi View Service bar.

![icon](Resources/icon-source-256.png)

## Two ways of showing the page

**In-game.** The Market tab is a picture box inside the plugin's Virindi window, showing frames
captured from a WebView2 that renders off-screen. It sits in the game's own D3D surface, keeps the
VVS window's z-order, and works in exclusive fullscreen. Clicking and scrolling work: mouse input
is sent to the browser as Chrome DevTools Protocol events, which inject at the browser's input
layer and so do not care that the real window is nowhere near the cursor.

This is deliberately not VVS's own `HudBrowser`. That control reports itself as available, attaches
and draws - and then renders blank white for every address, plain HTTP included, so it is not a
matter of the old engine failing on modern TLS. It is simply dead on the installs tested, and the
API gives no way to find that out except by trying.

Typing works too: click the page to give it focus, then type. While the page has focus every key
goes to it and none reach the game, which is the point - otherwise a search term would also walk
your character around and trigger hotkeys. Click anything else to give the keyboard back.

Because it is a screenshot pipeline, the page updates at the refresh rate (4 fps, and briefly
faster right after you interact) rather than smoothly.

**Pop-out window.** A WebView2 (Edge/Chromium) window floating over the client, on its own UI
thread. Current engine, renders anything, but it is an overlay: it needs windowed or borderless
mode, and it does not belong to the VVS window.

So the mode is switchable rather than decided for you:

| Mode | What it does |
|---|---|
| **In Game** (default) | drawn on the Market page, inside the plugin's Virindi window |
| **Pop Out Window** | a separate desktop window floating over the client |

Pick one with the checkboxes on the Settings page, or `/market mode ingame|window`. The choice is
remembered in `settings.txt`.

There used to be a third setting, `Auto`. It only ever meant In Game - it resolved to the in-game
surface whenever there was a Virindi window to draw into, which there always is - so it was three
choices for two behaviours. Old settings files saying `Auto` are read as In Game.

## Using it

The plugin's icon appears in the VVS bar once the client is running. Click it for the window,
which has a **Market** page (the in-game browser), a **Settings** page, and an **About** page.

Drag the title bar to move it, and the bottom-right grip to resize it - the page resizes with the
window. Where you leave it is remembered for next time.

| | |
|---|---|
| **Open Market** | show the page, whichever way the current mode calls for |
| **Reload** | reload the page |
| **Open in browser** | hand the site to your normal browser instead |
| **In Game / Pop Out Window** | where the market is shown |
| **Zoom - / + / 100%** | scale the page, 50% to 250%; defaults to 80% |
| **Keep the Pop Out Window above the game** | pin it over the client, or let it go behind |

The same things by chat command:

    /market            show or hide the window
    /market reload     reload the page
    /market web        open it in your normal browser
    /market version    which build this is
    /market mode       show where the market is shown; add ingame or window to change it
    /market zoom <n>   scale the page, 50 to 250 percent
    /market diag       what loaded and what did not
    /market reset      clear saved settings and window geometry
    /market icons      list every Virindi window and its icon type
    /market url <a>    load any address in-game
    /market help       the list above

Closing the window with its X only hides it, so the page keeps its state and scroll position.

## Requirements

- Decal 3.0 and Virindi View Service (VVS is what draws the window and the bar; without it the
  plugin still loads and the chat commands still work, using a plain Decal view instead).
- The Microsoft Edge WebView2 Runtime, for the browser itself. It is already on up-to-date
  Windows 10/11; otherwise get the Evergreen runtime from
  <https://developer.microsoft.com/microsoft-edge/webview2/>.
- Run AC windowed or borderless. The market window sits on top of the client, which exclusive
  fullscreen does not allow.

## Building

Run `build.bat`. It locates Visual Studio's MSBuild, restores the WebView2 package, builds
Release/x86, and packages the result:

    build.bat                   build Release
    build.bat Debug             build Debug
    build.bat Release 1.0.5     set the version to 1.0.5 first, then build

If the window closes instantly with nothing in it, `build.bat` has been saved with Unix line
endings - cmd cannot find the script's labels and quits without a word. Save it as CRLF.

Output:

    dist\DreamweaveMarket-1.8.2\           the plugin folder - register the DLL in here
    dist\DreamweaveMarket-1.8.2.zip        the same folder zipped

You need Visual Studio 2019/2022 (Community is fine) or the Build Tools, with the ".NET desktop
development" workload. The bare .NET Framework MSBuild will not do, because it cannot restore the
WebView2 NuGet package.

### Versioning

The version lives in one place, `Properties\AssemblyInfo.cs`. The build stamps it onto the DLL's
own file name (`DreamweaveMarket-1.8.2.dll`), and `build.bat` then reads the version back **out of
the compiled DLL** to name the dist folder and the zip. So the assembly, the file name, the folder
and the package always carry the same number, and none of them can claim a version the DLL does
not have. `build.bat Release 1.0.5` rewrites AssemblyInfo for you; keep `CHANGELOG.md` in step.

### Reference assemblies

The project compiles against four assemblies that are **not in this repository** - they belong to
Decal and to Virindi, and redistributing them is not this project's to do:

    Decal.Adapter.dll
    Decal.Interop.Core.dll
    Decal.Interop.Inject.dll
    VirindiViewService.dll

If you have Decal and VVS installed, `build.bat` finds them and you need do nothing. Otherwise
copy those four files into `reference-only\` - see the README in that folder. Either way the build
stops with a clear message rather than a confusing MSBuild error if it cannot find them.

**Never copy them into your Decal or plugin folders.** Decal and VVS load their own at runtime,
and a stale duplicate next to the plugin causes version mismatches and a plugin that will not
load.

## Installing

1. Build, then copy `dist\DreamweaveMarket-<version>\` somewhere permanent, e.g.
   `C:\Games\DecalPlugins\DreamweaveMarket\`. It contains the plugin DLL, the two WebView2
   assemblies and `WebView2Loader.dll` - all four are needed.
2. Open the Decal Agent, **Plugins > Add**, and browse to `DreamweaveMarket-<version>.dll`.
   Make sure it is enabled.
3. Restart the client.

Because the DLL name carries the version, upgrading means adding the new DLL and removing the old
entry - which is the point: you can always see in the Decal Agent which build is registered.

## How it is put together

    PluginCore.cs      the Decal plugin: lifecycle, modes, buttons, chat commands
    MarketView.cs      builds the Virindi window directly from VVS, icon included
    mainView.xml       the window layout, embedded and parsed by VVS's own Decal3XMLParser
    MarketBrowser.cs   the in-game path: a picture box fed frames, with mouse input forwarded
    OffscreenBrowser.cs  a WebView2 rendering off the edge of the desktop, captured per frame
    WebViewEnvironment.cs  the shared WebView2 environment - both browsers must describe it alike
    BrowserHost.cs     the pop-out path: a dedicated STA thread with its own message loop
    MarketForm.cs      the WinForms window hosting the WebView2 control
    Settings.cs        remembered display mode and window geometry, in a plain key=value file
    Globals.cs         the Host and Core handles Decal passes to Startup
    Util.cs            chat output, version, log

The in-game browser cannot be declared in `mainView.xml` - the MetaViewWrappers know nothing about
`HudBrowser` - so it is created in code and added to the `marketPage` layout, and resized from the
window's `Resize` event.

The pop-out browser runs on its own UI thread rather than the client's, so a slow page cannot
stall the game's message pump. Its profile lives in
`%LOCALAPPDATA%\DreamweaveMarket\WebView2`, because the plugin folder is usually not writable.

`WebView2Loader.dll` is not optional. It is the native shim the managed WebView2 assemblies call
into to find the installed runtime; without it, creating the browser throws DllNotFoundException.
It must be the **x86** build, to match the 32-bit AC client.

## Troubleshooting

**Nothing in the VVS bar, or /market does nothing.** Type `/market diag`. It answers in chat with
which parts came up:

    commands: hooked
    view: VirindiViewService
    Virindi View Service: running
    icon: applied
    mode: Auto (in-game)
    in-game surface: attached - rendering - Browse | Dreamweave Market
    pop-out window: not started

If `/market diag` gets no answer at all, the plugin is not loaded - check that it is listed and
enabled in the Decal Agent, and that you registered the versioned DLL
(`DreamweaveMarket-1.8.2.dll`, not `DreamweaveMarket.dll`).

If it answers but says `view: NONE`, the window failed to build; if it says Virindi View Service
is not running, VVS is not installed or loaded ahead of this plugin - the plugin still works
through chat commands, using a plain Decal view.

Either way the step-by-step startup log is at
`Documents\Decal Plugins\DreamweaveMarket\log.txt`.

**The Market page says the in-game browser could not start.** `WebControlWrapper.dll` is not
installed alongside Virindi View Service. Nothing to fix in this plugin - switch to
`/market mode window` and use the pop-out window.

**The Virindi bar disappears, or another plugin crashes with a NullReferenceException in
`ACImage.Clone`.** The bar clones every icon it holds whenever it rebuilds - which happens
whenever any plugin's window is added or removed - and `Clone` calls `new Bitmap(BitmapImage)`.
An icon whose data is not really a bitmap fails that, from inside VVS, taking the bar and every
plugin docked in it with it. The stack trace names whichever plugin happened to be shutting down,
not the one that owns the bad icon.

This plugin used to be that culprit, up to 1.3.4, because its icon was loaded from an embedded
PNG. It now uses a portal.dat icon id like every other VVS plugin. `/market icons` lists every
window and its icon type if you need to find another offender.

### About the icon

The badge is embedded as `Resources/icon.png` and turned into an `ACImage` that is set on the
window's `ViewProperties` **before** the `HudView` is constructed. That ordering is not optional:
the bar takes its copy of the icon when the window registers, so assigning `hud.Icon` afterwards
leaves the bar holding a different one, and it throws from inside VVS the next time it rebuilds -
in whichever other plugin happens to be shutting down at that moment.

**Nothing appears on the Market tab.** `/market diag` reports the in-game surface state, including
any error from the browser. The most likely cause is the same as for the pop-out window: a missing
WebView2 runtime or `WebView2Loader.dll`.

**Typing does nothing in-game.** Click inside the page first - the keyboard only goes to it once
it has focus. If a key still does nothing, it is one that is not translated; `/market mode window`
gives you a normal browser window.

**The pop-out window will not open.** Usually the WebView2 runtime is missing, or
`WebView2Loader.dll` (x86) is not in the plugin folder. The error dialog says which.

If it reports `0x8007139F` (ERROR_INVALID_STATE), two WebView2 environments were created over the
same user data folder with different options. Both browsers here go through `WebViewEnvironment`
to prevent that; if you add another WebView2 anywhere in this plugin, it must use that class too.

**Decal will not load the plugin.** It has to be built x86 - which the project already pins - and
the plugin folder must not contain copies of the DLLs from `reference-only\`.

## Contributing

Bug reports and pull requests are welcome. Two things to know before changing anything:

- **Keep CRLF line endings.** `.gitattributes` enforces it. `build.bat` in particular will close
  instantly with no message if it is ever saved with Unix line endings, because cmd cannot find
  the script's labels.
- **The icon is set on `ViewProperties` before the `HudView` is constructed**, in `MarketView`.
  Setting `hud.Icon` afterwards crashes the Virindi bar - not immediately, and not in this
  plugin's stack trace, but the next time the bar rebuilds, in whichever other plugin happens to
  be shutting down. `CHANGELOG.md` has the details under 1.4.0.

## Licence

MIT - see `LICENSE`. This covers this project's own code only, not the Decal or Virindi
assemblies it builds against.

## Credits

Designed by Rez (mostly Claude). Feedback and bug reports: the AC:DW Discord.

Project layout, build script and versioning scheme follow RezTools. The direct-to-VVS view
construction, and the icon ordering that goes with it, follow Resistance.
