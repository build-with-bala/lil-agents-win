# lil agents — Windows port design

**Date:** 2026-08-08
**Upstream:** [ryanstephen/lil-agents](https://github.com/ryanstephen/lil-agents) (macOS, Swift/AppKit, MIT © Ryan Stephen)
**Target:** C# / WPF on .NET 8, published as a self-contained single-file exe for win-x64 and win-arm64

## Problem

The upstream app is ~4,300 lines of Swift against AppKit, AVFoundation and CoreVideo.
None of those exist on Windows, so nothing ports mechanically. This is a rewrite against
the same design, with full feature parity minus Sparkle auto-update.

## Platform mapping

```
macOS (Swift/AppKit)                    Windows (C#/.NET 8/WPF)
─────────────────────────────────────── ──────────────────────────────────────────
NSWindow .borderless + .statusBar level  Window AllowsTransparency + WindowStyle.None
                                         + Topmost + WS_EX_TOOLWINDOW|NOACTIVATE
CVDisplayLink tick loop                  CompositionTarget.Rendering (vsync)
AVPlayerLayer (HEVC alpha .mov)          Image ← BitmapSource[241], index by elapsed
com.apple.dock defaults → dock strip     SHAppBarMessage(ABM_GETTASKBARPOS) → rect+edge
NSScreen.screens / visibleFrame          EnumDisplayMonitors + MONITORINFO.rcWork
CGWindowListCreateImage alpha hit-test   read alpha straight from sprite pixels
NSStatusItem + NSMenu                    NotifyIcon + ContextMenuStrip
UserDefaults                             %APPDATA%\lil-agents\settings.json
NSSound                                  MediaPlayer (sounds spilled to cache dir)
NSEvent global mouse monitor             WH_MOUSE_LL low-level hook
zsh -l -i -c env  → PATH                 PATH ∪ registry ∪ known install dirs
Sparkle auto-update                      out of scope
```

## Architecture

```
App.xaml.cs ── single-instance mutex, tray icon, lifecycle
   │
   ├── AgentsController ─── CompositionTarget.Rendering tick
   │      ├── TaskbarGeometry.Query()  → { Rect, Edge, IsAutoHide, IsCentered }
   │      ├── EnumerateMonitors()      → per-monitor bounds/work area/DPI
   │      └── MouseHook                → click-outside dismissal
   │
   ├── Walker × 2  (Bruce, Jazz)
   │      ├── SpriteSheet      241 frames, decode-on-demand + 24-frame LRU
   │      ├── MovementCurve    accel → linear → decel, ported verbatim
   │      ├── WalkerWindow     WM_NCHITTEST alpha click-through
   │      ├── BubbleWindow     thinking / completion phrases
   │      └── PopoverWindow ── TerminalControl (markdown, themes, slash commands)
   │
   └── Agents/
        IAgentSession ── TextReceived / ErrorReceived / ToolUsed / ToolResultReceived
        │                / SessionReady / TurnCompleted / ProcessExited
        ├── ProcessAgentSession (base: resolve, launch, UTF-8 pump, line assembly)
        │     ├── ClaudeSession    long-lived proc, stream-json in AND out
        │     ├── CodexSession     one proc per turn, history replayed into prompt
        │     ├── CopilotSession   --continue after turn 1, JSON→plaintext fallback
        │     ├── GeminiSession    --resume latest, JSONL→plaintext fallback
        │     └── OpenCodeSession  run --format json
        └── OpenClawSession        ClientWebSocket + Ed25519 (BouncyCastle)
```

## Decisions and their reasons

### Sprites instead of video

Media Foundation will not decode HEVC's alpha auxiliary layer and WPF's MediaElement has
no alpha path at all, so the walk videos were transcoded to 241 alpha PNGs per character
(225×400, pngquant'd; 29 MB → 9.5 MB with 61 alpha levels retained).

Frames are held as compressed bytes and decoded on demand. Full decode would cost ~87 MB
per character in BGRA; decoding one frame is well under a millisecond and only happens 24
times a second.

### Alpha hit testing gets simpler, not harder

Upstream cannot read its own pixels — `AVPlayerLayer` composites on the GPU — so it calls
`CGWindowListCreateImage` on a 1×1 rect at the cursor. With sprites the pixels are in
hand. `WM_NCHITTEST` returns `HTTRANSPARENT` below alpha 30 and `HTCLIENT` above, which
gives true click-through on the character's empty bounding box.

### Walk band — the one invented behaviour

macOS derives a narrow strip from the Dock's icon count. Windows exposes no equivalent
query; nothing reports how wide the taskbar's button cluster is. So:

```
Win11, TaskbarAl=1 (centered):  band = centered 40% of monitor width
Win11/10, TaskbarAl=0 (left):   band = left 40%, inset 2% past Start
vertical or auto-hidden taskbar: band = bottom edge of the monitor work area
```

`TaskbarAl` is read from `HKCU\...\Explorer\Advanced`; its absence (Windows 10) means
left-aligned.

### `.cmd` shims

`npm install -g @google/gemini-cli` produces `%APPDATA%\npm\gemini.cmd`, not an exe, and
CreateProcess — which is what `Process.Start` with `UseShellExecute=false` calls — cannot
execute a `.cmd`.

Rather than route user prompts through cmd.exe and inherit its metacharacter parsing,
`ResolvedCli` reads the shim and recovers the `node.exe <script.js>` invocation inside it,
which is then started directly with argument-array semantics. cmd.exe remains a fallback
with full CommandLineToArgvW quoting plus `^`-escaping outside quotes.

Residual caveat: cmd expands `%VAR%` even inside quotes, with no reliable suppression. Only
reachable on the fallback path, which no npm-installed CLI takes.

### UTF-8 everywhere

Windows consoles default to a legacy OEM code page. Every session sets
`StandardOutputEncoding`/`StandardErrorEncoding` to UTF-8 and pumps raw bytes through a
stateful `Decoder`, because a UTF-8 sequence can straddle a pipe read boundary.

Raw byte pumping rather than `OutputDataReceived` because the latter only surfaces whole
lines, which would make providers that stream partial text appear frozen until turn end.

### Coordinate flip

macOS is bottom-left origin with `window.y` = bottom edge; Windows is top-left. The
standing position becomes `bandTop − height × 0.85 − yOffset`, and the bubble anchor
`charTop + height × 0.12 − bubbleHeight`. Windows are moved with `SetWindowPos` in physical
pixels, never `Window.Left`/`Top`, so a walker crossing a DPI boundary does not jump.

### Ed25519 via BouncyCastle

.NET has no built-in Ed25519. NSec would pull a native libsodium and complicate arm64
single-file publishing; BouncyCastle is pure managed. The device id is the SHA-256 hex
fingerprint of the raw public key, matching the OpenClaw gateway's derivation, so keys
authenticate identically to the macOS CryptoKit ones.

## Deliberate divergences from upstream

| | Upstream | Here | Why |
|---|---|---|---|
| Markdown code fences | state resets per streaming chunk | state held on the renderer | a fence split across chunks lost its formatting |
| Fonts | SF Mono / Chicago / Geneva | Cascadia Mono / Tahoma / Segoe UI | the macOS faces do not exist on Windows |
| Install instructions | curl / brew | PowerShell / npm | curl and brew are not the Windows path |
| Auto-update | Sparkle | none | no Windows equivalent wired up |

## Testing

`dotnet test` on `windows-latest` covers the pure logic: movement curve shape and
continuity, NDJSON assembly across chunk boundaries (including CRLF and multi-byte),
`.cmd` classification and injection-resistant quoting, taskbar band maths for every
edge/alignment/monitor-offset case, per-provider event parsing, and markdown rendering.

Window placement, click-through and animation have no automated coverage. They require a
human on a Windows machine.

## Out of scope

- Auto-update (Sparkle has no port)
- Code signing — the exe is unsigned and trips SmartScreen on first run
- Installer (portable exe only)
