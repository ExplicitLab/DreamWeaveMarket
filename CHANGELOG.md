# Changelog

## 1.8.2
- Prepared for publishing as a repository. No behaviour changes.
- Added `LICENSE` (MIT), covering this project's own code only.
- Added `.gitattributes` pinning CRLF line endings, so a clone cannot end up with a `build.bat`
  that closes instantly - cmd cannot find labels in a batch file with Unix line endings.
- The Decal and Virindi reference assemblies are no longer included; they are not this project's
  to redistribute. `build.bat` already prefers an installed Decal and VVS, and now stops with a
  clear message naming the missing files instead of letting MSBuild fail with a hint-path error.

## 1.8.1
- Removed the "Open the market in a browser" button from the About page. The same thing is on the
  Settings page already.

## 1.8.0
- The Controls page is now **Settings**.
- Removed the status line that echoed the last action. It reported things that had already
  happened visibly - the window opened, the zoom changed - and the failures worth reading go to
  chat and the log anyway.
- Added an **About** page: version, "Designed by Rez (Mostly Claude)", the market address, where
  to send feedback, and a button to open the site in a normal browser.

## 1.7.3
- **Dropdowns open downwards again.** Chromium positions a `<select>` popup from the element's
  screen coordinates and flips the list upwards when there is no room below it on the nearest
  monitor. The offscreen window sat past the bottom-right corner of the desktop, so there never
  was any room below and every dropdown opened upwards - unlike the same page in a real browser.
  The window is now level with the top of the desktop and only offset horizontally, which is all
  that was needed to hide it.

## 1.7.2
- Fixed the build: the /market zoom handler declared a local named `text`, which the chat command
  method already uses for the typed line.

## 1.7.1
- Zoom now defaults to 80%, which is where the market's filter row and dropdowns fit a window
  sized to sit comfortably in the game. The `100%` button still resets to true size.

## 1.7.0
- **Page zoom.** `-` / `+` / `100%` on the Controls page, or `/market zoom <percent>`, from 50% to
  250% in steps of 10. It applies to both In Game and the Pop Out Window and is remembered between
  sessions. The market is laid out for a desktop browser and has no idea it is being shown in a
  640-pixel box, so zooming out is the way to fit a dropdown or a wide table into the window.
- Mouse coordinates are mapped through the zoom factor. Zoom changes the size of a CSS pixel, and
  the browser counts input in CSS pixels, so without this everything would look right and every
  click would land somewhere else - further off the further from the top-left corner.

## 1.6.0
- **Auto is gone.** It only ever meant In Game: it resolved to the in-game surface whenever there
  was a Virindi window to draw into, and there always is - that is where the Controls page lives.
  Three choices for two behaviours. Settings files saying `Auto` are read as In Game.
- **Two checkboxes instead of a cycling button.** In Game and Pop Out Window, kept mutually
  exclusive, so the current setting is visible without reading a button label and working out
  which way the cycle turns. Clicking the one already ticked does nothing rather than leaving
  neither selected. The choice is remembered between sessions, as before.
- "Pop Out Window" throughout, in the window and in chat.

## 1.5.2
- **Dragging works in-game** - the scrollbar thumb, sliders, text selection. Mouse moves were
  always sent as hovers, with no button reported held, so Chromium saw a drag as a pointer wandering
  across the page. Clicking the arrows and the trough worked because those are complete clicks.
  Moves now carry the button state.
- The page keeps refreshing at the fast rate for the whole of a drag, so a dragged scrollbar
  tracks the pointer instead of catching up when you let go.
- A drag whose button is released outside the window no longer sticks. VVS delivers that mouse-up
  elsewhere, leaving the page convinced the button is still down; Windows is now asked directly
  each frame whether it really is.

## 1.5.1
- Fixed the build: HudView.FocusControl is static - VVS tracks one focused control for the whole
  service rather than one per window.

## 1.5.0
- **Typing works in-game.** Clicking the page gives it keyboard focus and characters go to the
  browser as DevTools protocol events - printable ones as `char` (VVS has already applied shift
  and keyboard layout), and backspace, enter, tab, delete and the arrow and page keys translated
  from scan code to virtual key code.
- Keys are eaten while the page has focus, which matters as much as the typing: without it a
  search term also walks your character around, opens chat, and fires whatever hotkeys those
  letters are bound to. Focus arrives by clicking the page and leaves by clicking anything else.

## 1.4.2
- **In-game rendering works again.** 1.3.2 replaced the per-frame ACImage with a single reused
  bitmap and texture, refreshed with ClearCachedBMPTexture/GenerateCachedBMPTexture. VVS ignores
  that and keeps drawing the texture it already has, so the tab showed whatever colour the surface
  had been cleared to and never changed - white in 1.3.2, dark navy in 1.4.1. Both looked like a
  browser that had failed rather than a picture that was never updated, which is what sent the
  last few versions chasing the browser instead of the drawing. Back to a new ACImage per frame,
  as in 1.2.2, which is the version that demonstrably rendered.
- Kept the one good idea from that detour: a replaced image is freed ten frames later rather than
  immediately, so a texture VVS might still be drawing from is never pulled out from under it.
- Capture is CapturePreviewAsync again. It was never the problem.

## 1.4.1
- **Fixed 0x8007139F, which killed both browsers.** WebView2 lets several controls in one process
  share a user data folder - that is how the in-game surface and the pop-out window share a login
  - but only if every environment over that folder is created with *identical options*. The
  offscreen renderer asked for the occlusion flags it needs; the pop-out asked for nothing;
  whichever started second got ERROR_INVALID_STATE and no browser at all. The failure read as a
  broken WebView2 install rather than a mismatch. Both now go through one place,
  `WebViewEnvironment`, which describes the environment once.
- The in-game tab says what is happening instead of showing a blank white rectangle: "Loading
  market.acdreamweave.com...", or the actual error and what to do about it. A white rectangle
  meant "starting", "failed" and "not running" indistinguishably, which cost a lot of guessing.

## 1.4.0
- **The AC:DW badge is back, and the bar no longer crashes.** The window is now built directly
  against Virindi View Service - `Decal3XMLParser.ParseFromResource`, then `new HudView(properties,
  controls)` - with the icon set on the ViewProperties *before* the HudView exists. That ordering
  is the whole fix: the bar takes its copy of the icon when the window registers, so an icon
  assigned afterwards leaves it holding something else. This is how Resistance does it, and how
  every VVS plugin that works with a bitmap icon does it.
- The MetaViewWrappers are gone - about 3,400 lines of third-party wrapper deleted. They were the
  reason the icon could not be set at the right moment: they build the HudView internally and
  never expose its properties. Controls are now looked up by name and events wired directly
  (`Hit` for buttons, `Change` for checkboxes).
- The source bitmap for the icon is disposed immediately after the ACImage is built, since ACImage
  copies what it is given. Keeping it alive, as earlier versions did, was solving a non-problem.
- `/market iconid` is gone with the portal-icon workaround it existed for. `/market icons` stays.

## 1.3.5
- **The custom bitmap icon is gone, and with it the crash that took out the Virindi bar.** Handing
  VVS a Bitmap-backed ACImage is not safe: the bar clones every icon it holds whenever it rebuilds
  - which happens whenever any plugin's window is added or removed - and ACImage.Clone() calls
  new Bitmap(BitmapImage), which threw NullReferenceException for ours. The stack always named
  whichever plugin was shutting down at that moment, never this one, which is why it took four
  attempts to find. Every VVS plugin that works uses a portal.dat icon id instead, and so does
  this one now.
- The icon id lives in settings.txt and can be changed in game: `/market iconid <number>`. It
  defaults to 6112, the Ice Heaume, so a mistyped id can always be undone.
- Added `/market icons`, which lists every Virindi window and what kind of icon it carries. An
  icon that is not really a bitmap is a window waiting to take the bar down; this says which.
- The window takes itself out of the bar before its view is torn down.

### What was lost
The AC:DW badge is no longer the bar icon - it cannot be, safely. The artwork is still in
`Resources` and still used for the pop-out window.

## 1.3.4
- Icon rebuilt from the official 1024px AC:DW artwork rather than a 30px crop out of a screenshot,
  so the letters and the arrow motif are legible at bar size. A 256px copy is kept in Resources so
  it can be re-derived without the original.

## 1.3.3
- Fixed the build: CompositingMode is in System.Drawing.Drawing2D, not System.Drawing.Imaging.

## 1.3.2
- **Removed the portal.dat icon id from the view.** `ACImage.Clone()` does
  `new Bitmap(BitmapImage)` unconditionally, and an ACImage built from an icon id has no bitmap -
  so the argument is null and it throws NullReferenceException from inside VVS. The bar clones
  every icon it holds each time it rebuilds, which happens whenever *any* plugin's window is added
  or removed, so the crash lands in someone else's stack trace and takes the whole bar with it.
  The icon is now only ever set in code, from a real bitmap, with a generated bitmap as the
  fallback rather than an icon id.
- **No more per-frame DirectX textures.** The in-game surface built a new ACImage for every
  captured frame and disposed the previous one immediately - freeing a texture several times a
  second on the render thread. If VVS had already taken that image for the frame it was drawing,
  that is a use-after-free, and a use-after-free in native memory surfaces later as an
  AccessViolationException somewhere else entirely, in another plugin's call, with nothing in the
  stack pointing back here. There is now one bitmap and one texture per surface: frames are
  painted into the bitmap and the texture is regenerated in place.
- A surface replaced by a resize is held for ten frames before being freed, so nothing is ever
  released while VVS might still be drawing from it.

## 1.3.1
- **Fixed the crash that took out the Virindi bar and other plugins.** The custom icon was built
  with `new Bitmap(stream)`, which keeps a reference to the stream rather than copying it, and the
  stream was closed immediately after. VVS accepted the result and drew it as a flat pink square;
  the real damage came later, whenever the bar rebuilt itself and called `ACImage.Clone()` on the
  icons it holds. That clone does `new Bitmap(original)` on a bitmap whose native image is gone,
  throwing NullReferenceException inside VVS - which is why the visible failure was another
  plugin's shutdown, not this one. The bitmap is now copied out of the stream while it is still
  open, and never disposed.
- Window geometry is restored at LoginComplete rather than during Startup. Assigning ClientArea
  before VVS has finished bringing the window up reaches into its layout too early, and an
  exception raised in there does not stay inside this plugin. This is the same point RezTools
  restores at, for the same reason.
- Size and position are restored independently, so one failing cannot cost you the other.
- Added `/market reset`, which clears saved settings and window geometry - the way out if a saved
  value is itself the problem.

## 1.3.0
- The window can be resized. The MetaView wrappers never set `UserResizeable`, so a VVS window
  built from XML comes up locked at the size the XML gave it, with no resize grip. Dragging by the
  title bar always worked; the grip needed the flag. Minimum size 360x260.
- The browser surface follows the window: the notebook fills the client area, and the capture is
  resized with the control so clicks keep landing in the right place.
- Window size and position are remembered between sessions, saved as you resize rather than only
  at shutdown, so a client crash does not lose them. A position on a monitor that is no longer
  attached is ignored rather than restored somewhere unreachable.
- The window title now carries the version.

## 1.2.2
- Fixed the build properly: MouseEventType and MouseButton are nested types inside
  ControlMouseEventArgs, so they are named through it. (1.2.1 guessed the global namespace, on the
  strength of metadata that reports an empty namespace for every nested type.)

## 1.2.1
- Attempted fix for the same build error; wrong.

## 1.2.0
- **The market now renders in-game for real.** VVS's own HudBrowser is gone - it reported itself
  as available and then drew a blank page for every address, plain HTTP included. In its place the
  Market tab is a picture box fed by an off-screen WebView2: the same Chromium that renders the
  site correctly in the pop-out window, captured four times a second and drawn inside the game.
- Clicking, scrolling and hovering work in-game. Mouse input goes to the browser as Chrome
  DevTools Protocol events, so it lands properly even though the real window is off-screen.
- The refresh rate rises briefly after a click or scroll, so interaction feels immediate without
  paying for 4 fps of screenshots the rest of the time.
- Frames stop entirely while the window is closed or the Market tab is not on top.
- `Auto` prefers in-game again, now that in-game works.

### Known limits
- **No keyboard.** Search boxes and signing in need the pop-out window (`/market mode window`).
  VVS key handling and an off-screen browser do not compose easily; it can be added later.
- It is a screenshot pipeline, so animations and hover effects update at the refresh rate rather
  than smoothly, and a Chromium process runs while the tab is open.

## 1.1.3
- Auto now resolves to the pop-out window rather than the in-game browser. HudBrowser.IsAvailable
  returns true on installs where the control attaches, draws, and then renders blank white for
  every address including plain HTTP - available only means VVS could load WebControlWrapper.dll.
  In-game is now opt-in via `/market mode ingame`.
- Fixed every chat message the plugin has ever written being silently discarded. Output went
  through `CoreManager.Current.Actions`, but Decal runs plugins in their own AppDomain where that
  static is not reliably populated; the null went into a catch and nothing was ever printed. It
  now goes through `Host.Actions`, as RezTools and the VVS sample template do. This is why
  `/market test`, `/market diag` and the startup banner all appeared to do nothing.
- An unrecognised `/market ...` now answers instead of staying silent.
- `Globals` holds the Host and Core handed to Startup.

## 1.1.2
- Fixed the build: HudBrowser.TitleChanged takes VVS's own `delTC` delegate (void, no parameters),
  not EventHandler, so the handler signature was wrong.

## 1.1.1
- The in-game browser now reports page titles. HudBrowser gives back almost nothing about a failed
  load, and a title is the one signal that a page was actually fetched and parsed - `/market diag`
  shows the last one, and every change is logged.
- Added `/market test`, which loads a plain-HTTP page in-game. If that renders but the market does
  not, the engine works and HTTPS is the problem.
- Added `/market url <address>` to point the in-game browser anywhere.

## 1.1.0
- The market page can now render **inside the game**, using Virindi View Service's own HudBrowser
  control, drawn into the client's D3D surface as part of the plugin's window. It keeps the VVS
  window's z-order and works in exclusive fullscreen, neither of which the pop-out window can do.
- Three display modes, switchable at runtime and remembered between sessions: Auto (in-game when
  this install supports it, pop-out window otherwise), InGame, and Window. `/market mode
  auto|ingame|window`, or the button on the Controls page.
- The pop-out WebView2 window is no longer started at load. It costs a Chromium process, so it is
  created the first time something actually asks to show the page that way.
- The window now has two pages: Market (the in-game browser) and Controls.
- Settings are kept in `Documents\Decal Plugins\DreamweaveMarket\settings.txt`.
- `/market diag` also reports the display mode, whether the in-game browser is available on this
  install, and whether it is attached.

## 1.0.4
- Fixed the failure mode where nothing worked and nothing said why. Startup was one big
  try/catch with the chat hook installed last, so anything that threw earlier - the view, the
  icon, the browser thread - swallowed the error and left both the Virindi bar entry AND
  /market dead. Each step is now caught on its own, and the chat commands are hooked first.
- Added `/market diag`: reports whether the commands hooked, which view system is in use, whether
  Virindi View Service is running, whether the icon was applied, and the state of the browser
  window.
- Startup writes a step-by-step log to `Documents\Decal Plugins\DreamweaveMarket\log.txt`
  (renamed from errors.txt, and now written on success too, not only on failure).

## 1.0.3
- Fixed build.bat closing instantly with no message: the file had Unix line endings, so cmd could
  not find the `:fail` label and gave up silently. Every text file in the project now ships with
  Windows (CRLF) line endings.
- build.bat no longer expands `%ProgramFiles(x86)%` inside a parenthesised block - the closing
  bracket in that folder name ends the block early. Captured once at the top instead.
- Decal and VVS folder detection moved into a subroutine, and the script echoes what it found
  before building, so a failure says which paths were used.

## 1.0.2
- Added a Virindi View Service window, so the plugin appears in the VVS bar. Previously it had no
  view at all, which is why nothing showed up there.
- Custom icon (the AC:DW badge), pushed onto the HudView as a bitmap rather than borrowing a
  portal.dat icon id.
- Buttons in the window for open/hide, reload, open in the default browser, and an always-on-top
  toggle. `/market help` lists the chat commands.
- Rebuilt as a classic (non-SDK) x86 project modelled on the RezTools layout, with the Decal and
  VVS reference assemblies in `reference-only\`.
- Version lives in `Properties\AssemblyInfo.cs`; the DLL is renamed to carry it, and the dist
  folder and zip take their names from the compiled DLL.
- `build.bat` finds Visual Studio's MSBuild, prefers a real Decal install when it finds one, and
  pauses on success and on failure.

## 1.0.1
- Decal/Virindi reference DLLs bundled; `build.bat` pauses so errors can be read.

## 1.0.0
- Initial release: `/market` toggles a WebView2 window showing market.acdreamweave.com.
