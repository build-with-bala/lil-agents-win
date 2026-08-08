# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

A Windows port of [ryanstephen/lil-agents](https://github.com/ryanstephen/lil-agents), a
macOS Swift/AppKit desktop-pet app that fronts AI CLIs. This is a rewrite in C# / WPF
against the same design, not a translation — no Swift source is carried over.

## Building

```powershell
dotnet build LilAgents.sln -c Release
dotnet test  tests/LilAgents.Tests/LilAgents.Tests.csproj
```

**WPF only compiles on Windows.** `net8.0-windows` requires the Windows Desktop SDK;
there is no macOS or Linux build path. CI (`.github/workflows/build.yml`) runs on
`windows-latest` and is the source of truth for whether the tree compiles.

## Architecture

```
App.xaml.cs            single-instance mutex, tray, lifecycle
AgentsController       CompositionTarget.Rendering tick loop, geometry, global mouse hook
Walker                 per-character state machine: walk, bubble, popover, session
Settings               JSON at %APPDATA%\lil-agents\settings.json (replaces UserDefaults)

Platform/              all Win32 interop lives here
  Native               P/Invoke surface
  TaskbarGeometry      SHAppBarMessage + TaskbarAl registry + monitor enumeration
  ShellEnvironment     PATH union: process ∪ registry ∪ known install dirs
  ResolvedCli          classifies .exe / npm .cmd shim / .ps1
  CliLauncher          ProcessStartInfo construction, UTF-8, cmd.exe quoting
  MouseHook            WH_MOUSE_LL, for click-outside dismissal
  DpiHelper            DIP ↔ pixel conversion
  EmbeddedAssets       sprites, sounds, icons out of the assembly

Agents/                one class per provider + shared process plumbing
Rendering/             MovementCurve, SpriteSheet
UI/                    OverlayWindow base, Walker/Bubble/Popover windows, terminal, themes
```

## Things that will bite you

**`.cmd` shims.** `npm install -g` on Windows produces `gemini.cmd`, not `gemini.exe`, and
CreateProcess cannot execute a `.cmd`. `ResolvedCli` reads the shim and recovers the
underlying `node.exe <script.js>` so the CLI can be started directly with argument-array
semantics. cmd.exe is a fallback only; if you touch `CliLauncher.BuildCmdCommand`, keep
`CliLauncherTests` green — a mistake there is command injection into the user's shell.

**Encoding.** Every session forces UTF-8 on stdout/stderr. Windows consoles default to a
legacy code page and the agent CLIs stream NDJSON with non-ASCII in it.

**Coordinate systems.** macOS is bottom-left origin, Windows is top-left. Any position
maths ported from the Swift source needs flipping — see `Walker.PositionWindow` and
`Walker.PositionBubble` for the two conversions and their derivations.

**Pixels, not DIPs.** Windows are positioned with `SetWindowPos` in physical pixels rather
than `Window.Left`/`Top`, so a character crossing between displays with different scaling
does not jump. Do not mix the two.

**Topmost decay.** The taskbar is itself topmost; z-order within that band shifts. The
controller re-asserts `HWND_TOPMOST` every two seconds. Without it walkers sink behind
the taskbar after a while.

## Testing

`dotnet test` covers the pure logic: movement curve, NDJSON line assembly, `.cmd`
classification and quoting, taskbar band maths for every edge/alignment, provider event
parsing, and markdown rendering. Window behaviour and animation have no automated
coverage and need a human on a Windows machine.

WPF text objects need STA; xunit runs MTA. `MarkdownRendererTests` spins up its own STA
thread rather than adding a runner package — follow that pattern for new UI-touching tests.

## Assets

Sprites were generated from the upstream `.mov` files with ffmpeg and quantised:

```bash
ffmpeg -i walk-bruce-01.mov -vf "scale=225:400:flags=lanczos" -pix_fmt rgba \
  -start_number 0 sprites/bruce/frame_%03d.png
pngquant --quality=70-92 --speed 1 --force --ext .png sprites/bruce/*.png
```

Everything under `Assets/` is embedded in the assembly, so the published single-file exe
has no companion folder. Sounds are spilled to `%LOCALAPPDATA%\lil-agents\cache` on first
play because `MediaPlayer` needs a URI.

## Attribution

Keep `LICENSE` (Ryan Stephen's MIT) and `NOTICE` intact. The character art and sounds are
his work.
