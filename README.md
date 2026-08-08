# lil agents for Windows

Tiny AI companions that live on your Windows taskbar.

**Bruce** and **Jazz** walk back and forth along your taskbar. Click one to open an AI
terminal. They walk, they think, they vibe.

Supports **Claude Code**, **OpenAI Codex**, **GitHub Copilot**, **Google Gemini**,
**OpenCode** and **OpenClaw** — switch between them from the tray icon.

A Windows port of [ryanstephen/lil-agents](https://github.com/ryanstephen/lil-agents),
rewritten in C# / WPF. See [NOTICE](NOTICE) for attribution.

## features

- Animated characters rendered from per-frame alpha sprites
- Click a character to chat with AI in a themed popover terminal
- Switch between six providers from the tray menu
- Four visual themes: Peach, Midnight, Cloud, Moss
- Slash commands: `/clear`, `/copy`, `/help` in the chat input
- Copy last response and new-session buttons in the title bar
- Thinking bubbles with playful phrases while your agent works
- Sound effects on completion
- First-run onboarding
- Multi-monitor and per-monitor DPI aware

## requirements

- Windows 10 (1809+) or Windows 11
- x64 or arm64 — a separate build is published for each
- No .NET install needed: the published exe is self-contained
- At least one supported CLI:

  | Provider | Install |
  |---|---|
  | Claude Code | `irm https://claude.ai/install.ps1 \| iex` |
  | OpenAI Codex | `npm install -g @openai/codex` |
  | GitHub Copilot | `npm install -g @github/copilot` |
  | Google Gemini | `npm install -g @google/gemini-cli` |
  | OpenCode | `npm install -g opencode-ai` |
  | OpenClaw | `npm install -g openclaw`, then set the gateway under Provider → Advanced Settings |

Installed a CLI while the app was running? Tray → Provider → **Rescan for CLIs**.

## getting the exe

Every push builds on CI. Grab `LilAgents-win-x64` (or `-win-arm64`) from the artifacts of
the latest [build run](../../actions/workflows/build.yml).

Windows will show a **SmartScreen** warning the first time, because the exe is unsigned:
click *More info* → *Run anyway*.

## building

```powershell
dotnet build LilAgents.sln -c Release
dotnet test  tests/LilAgents.Tests/LilAgents.Tests.csproj
```

Producing the single-file exe:

```powershell
dotnet publish src/LilAgents/LilAgents.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish/win-x64
```

Requires the .NET 8 SDK on Windows. WPF cannot be compiled on macOS or Linux.

## how it differs from the macOS original

The Swift app is ~4,300 lines against AppKit, AVFoundation and CoreVideo, none of which
exist here, so this is a rewrite against the same design rather than a translation.
The behavioural differences worth knowing:

| | macOS | Windows |
|---|---|---|
| Walk surface | Dock icon strip, computed from `com.apple.dock` | Taskbar band from `SHAppBarMessage`; Win11 centre-alignment respected |
| Animation | Transparent HEVC via `AVPlayerLayer` | 241 alpha PNG frames per character, decoded on demand |
| Click-through | Screen-captures a 1×1 rect to sample alpha | Reads sprite alpha directly in `WM_NCHITTEST` |
| CLI discovery | `zsh -l -i -c env` | PATH ∪ registry ∪ known install dirs; npm `.cmd` shims decoded to `node <script>` |
| Auto-update | Sparkle | not implemented |

There is no Dock-width equivalent on Windows — no API reports how wide the taskbar's
button cluster is — so the walk band is a fraction of the monitor width, centred on
Windows 11 and left-anchored elsewhere. See `TaskbarGeometry.ComputeBand`.

## privacy

Same posture as upstream. lil agents runs entirely on your PC and sends no personal data
anywhere.

- **Your data stays local.** The app plays bundled animations and reads your taskbar
  geometry to position the characters. No project data, file paths, or personal
  information is collected or transmitted.
- **AI providers.** Conversations are handled entirely by the CLI process you choose,
  running locally. lil agents does not intercept, store, or transmit your chat content.
  Anything sent to the provider is governed by their terms and privacy policies.
- **OpenClaw** is the exception by design: it talks to whatever gateway you configure.
  Its token is stored in `%APPDATA%\lil-agents\settings.json` in plain text.
- **No accounts, no analytics, no update pings.**

## license

MIT. See [LICENSE](LICENSE).
