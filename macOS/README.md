# GaGa for macOS

A port of [GaGa](https://github.com/Solomag/GaGa) by [thekingziga](https://github.com/thekingziga/GaGa) — a very lightweight
radio player that lives in the menu bar — to macOS, in Swift and AppKit.

Like the Windows original, stations come from a plain INI file that you can
edit, move and share, instead of being added one by one through a GUI.

## Running it

```
./build.sh            # builds macOS/build/GaGa.app
./build.sh --install  # ... and copies it to /Applications
./package.sh          # ... and wraps it as a .dmg and a .zip
```

Needs Xcode command line tools. No other dependencies. The result is a
universal (Apple silicon + Intel) app bundle.

The app is signed ad-hoc rather than with a Developer ID, and is not
notarized. On your own machine that is invisible: a locally built app
carries no quarantine flag and opens normally. Anything that arrives over
the internet does carry one, and Gatekeeper refuses an ad-hoc signature, so
whoever receives it has to right click the app and choose Open the first
time, or run `xattr -dr com.apple.quarantine /Applications/GaGa.app`.
Avoiding that step needs a paid Developer ID and notarization.

## How it works

The app is both a menu bar item and a regular window, and it keeps running
in the menu bar when you close the window.

**Menu bar icon**, same mouse mapping as the Windows version:

* Left click toggles play/stop (opens the menu when no stream is selected).
* Middle click toggles mute (opens the menu when idle).
* Right click, or control click, opens the menu with the list of streams.

The icon shows the state: idle, playing, muted, and an animation while
buffering. Hovering shows the state and the stream name.

**Window**: the same streams as a list. Double click a station to play it,
or select one and use the transport controls at the bottom (play/stop,
mute, volume). Closing the window leaves the app in the menu bar; clicking
the dock icon brings it back.

**Power**: closing the lid stops playback and opening it starts the same
station again, whether or not the machine actually slept in between — a
closed lid on power, or with an external display, keeps running otherwise.
The same happens across sleep and wake: a live stream cannot be paused and
picked up later, so it is reopened rather than resumed. Stopping playback
yourself before closing the lid is remembered; it will not start itself
again when you open it.

Playing does **not** keep the machine awake. CoreAudio normally holds a
`PreventUserIdleSystemSleep` assertion for any process with audio running,
which a radio player has no business doing, so the app opts out with
`kAudioHardwarePropertySleepingIsAllowed`. Idle sleep works exactly as it
would with nothing playing.

**Media keys** work through the system Now Playing controls, so play/pause
on the keyboard and in Control Center reach the app. No accessibility
permission is needed. Toggle them in the Options submenu.

**Favorites**: *Add to Favorites* puts the station you're listening to into
a `[Favorites]` section of the streams file, which becomes the first submenu,
even when it's already in another one. *Remove from Favorites* takes it out.

**Recording**: *Recording > Record This Station* saves the stream into
`~/Music/GaGa Recordings`, as received (MP3, AAC, Ogg...), with no
re-encoding. It opens a second connection to the station, because AVPlayer
doesn't hand out the audio it plays. It stops with the stream. HLS stations
(.m3u8) can't be recorded.

**Recent Stations** (the last 15 played), **Track History** (the last 100
titles the stations announced, with Copy and Search on the Web) and a
**Sleep Timer** (15, 30, 60 or 120 minutes) are in the menu. A station that
drops the connection is opened again by itself; one that keeps dropping, or
never sends any audio, stops with the reason.

**Start at Login** in Options registers the app as a login item
(System Settings > General > Login Items).

**Updates**: GaGa checks the [releases](https://github.com/Solomag/GaGa/releases)
a few seconds after startup (can be turned off in Options) and, if you
agree, downloads the new version, replaces itself and restarts. This needs
GaGa.app in a folder you can write to, such as /Applications.

## The streams file

`~/Library/Application Support/GaGa/Streams.ini`, recreated with defaults
if you delete it, and reloaded whenever you open the menu or the window.
"Edit Streams File" opens it in whatever app handles `.ini`.

```ini
[SomaFM]
Groove Salad = https://ice1.somafm.com/groovesalad-128-mp3
Drone Zone = https://ice1.somafm.com/dronezone-128-mp3
```

Use a `/` in a section name for nested submenus:

```ini
[Videogames/Oldies]
Kohina = http://kohina.radio.ethz.ch/kohina.mp3

[Videogames]
A.I. Radio = http://ai-radio.org/radio.mp3.m3u
```

On a syntax error the menu is replaced with an error item that reports the
exact line and offers to open the file.

Streams can be direct URLs or playlists — `.m3u`, `.pls`, `.asx`, `.xspf`,
local files included, which are downloaded and unwrapped to the first
stream they point at. `.m3u8` is passed straight through, since AVPlayer
handles HLS itself.

The default streams file starts with `Capris`, pointing at
`~/Library/Mobile Documents/com~apple~CloudDocs/capris.m3u`, and that is
also the stream selected on a fresh install. Because the entry points at
the playlist file rather than a copy of its contents, editing that file in
iCloud Drive changes what plays. Every other station in the file is
available from the same list.

Settings live next to the streams file in `Settings.json`: volume, last
stream, and the two options (play on startup, media keys). A fresh install
starts at 30% volume with Capris selected and auto play off, so opening the
app and clicking play is all it takes.

The application icon is generated rather than checked in by hand. To change
it, edit `Icon/make-icon.swift` and run `swift Icon/make-icon.swift`, which
rewrites `Icon/AppIcon.icns`; `build.sh` copies that into the bundle.

## Differences from the Windows version

* No balance slider. AVPlayer has no pan control, and adding one means an
  audio processing tap around the live stream; macOS has a system-wide
  balance in System Settings > Sound instead.
* Icons are SF Symbols rather than bundled `.ico` files.
* Media keys go through Now Playing instead of a low-level keyboard hook.
* Settings are JSON in Application Support rather than a serialized blob
  next to the executable, which is where macOS apps are expected to write.
