using LilAgents.Platform;

namespace LilAgents.Agents;

public enum TitleFormat { Uppercase, LowercaseTilde, Capitalized }

public enum AgentProviderKind { Claude, Codex, Copilot, Gemini, OpenCode, OpenClaw }

/// <summary>
/// Provider metadata, availability detection and session construction.
/// Install instructions are Windows-specific — the macOS build tells users to run
/// curl/brew, neither of which exists here.
/// </summary>
public static class AgentProvider
{
    public static readonly AgentProviderKind[] All =
    [
        AgentProviderKind.Claude,
        AgentProviderKind.Codex,
        AgentProviderKind.Copilot,
        AgentProviderKind.Gemini,
        AgentProviderKind.OpenCode,
        AgentProviderKind.OpenClaw
    ];

    private static readonly Dictionary<AgentProviderKind, bool> AvailabilityCache = new();
    private static readonly object Gate = new();

    public static string DisplayName(this AgentProviderKind provider) => provider switch
    {
        AgentProviderKind.Claude => "Claude",
        AgentProviderKind.Codex => "Codex",
        AgentProviderKind.Copilot => "Copilot",
        AgentProviderKind.Gemini => "Gemini",
        AgentProviderKind.OpenCode => "OpenCode",
        AgentProviderKind.OpenClaw => "OpenClaw",
        _ => "Claude"
    };

    public static string BinaryName(this AgentProviderKind provider) => provider switch
    {
        AgentProviderKind.Claude => "claude",
        AgentProviderKind.Codex => "codex",
        AgentProviderKind.Copilot => "copilot",
        AgentProviderKind.Gemini => "gemini",
        AgentProviderKind.OpenCode => "opencode",
        AgentProviderKind.OpenClaw => "openclaw",
        _ => "claude"
    };

    public static string InputPlaceholder(this AgentProviderKind provider) =>
        $"Ask {provider.DisplayName()}...";

    public static string TitleString(this AgentProviderKind provider, TitleFormat format) => format switch
    {
        TitleFormat.Uppercase => provider.DisplayName().ToUpperInvariant(),
        TitleFormat.LowercaseTilde => provider.DisplayName().ToLowerInvariant(),
        _ => provider.DisplayName()
    };

    public static string Serialize(this AgentProviderKind provider) =>
        provider.ToString().ToLowerInvariant();

    public static AgentProviderKind Parse(string? raw) => raw?.ToLowerInvariant() switch
    {
        "codex" => AgentProviderKind.Codex,
        "copilot" => AgentProviderKind.Copilot,
        "gemini" => AgentProviderKind.Gemini,
        "opencode" => AgentProviderKind.OpenCode,
        "openclaw" => AgentProviderKind.OpenClaw,
        _ => AgentProviderKind.Claude
    };

    public static string InstallInstructions(this AgentProviderKind provider) => provider switch
    {
        AgentProviderKind.Claude =>
            "To install, run this in PowerShell:\n  irm https://claude.ai/install.ps1 | iex\n\n" +
            "Or: npm install -g @anthropic-ai/claude-code\n\nOr download from https://claude.ai/download",
        AgentProviderKind.Codex =>
            "To install, run this in PowerShell:\n  npm install -g @openai/codex",
        AgentProviderKind.Copilot =>
            "To install, run this in PowerShell:\n  npm install -g @github/copilot\n\n" +
            "Then authenticate:\n  copilot",
        AgentProviderKind.Gemini =>
            "To install, run this in PowerShell:\n  npm install -g @google/gemini-cli\n\n" +
            "Then authenticate:\n  gemini",
        AgentProviderKind.OpenCode =>
            "To install, run this in PowerShell:\n  npm install -g opencode-ai\n\n" +
            "Or: winget install opencode",
        AgentProviderKind.OpenClaw =>
            "OpenClaw is a self-hosted AI gateway.\n\n" +
            "Install: npm install -g openclaw\nStart:   openclaw gateway run\n\n" +
            "Then set your gateway address and token under Provider > Advanced Settings.",
        _ => ""
    };

    // MARK: - Availability

    /// <summary>
    /// Probes PATH for every provider binary. Runs off the UI thread because it touches
    /// the registry and the filesystem.
    /// </summary>
    public static Task DetectAvailableProvidersAsync() => Task.Run(() =>
    {
        foreach (var provider in All)
        {
            bool available;
            if (provider == AgentProviderKind.OpenClaw)
            {
                available = !string.IsNullOrEmpty(Settings.Current.OpenClawAuthToken);
            }
            else
            {
                available = ShellEnvironment.FindBinary(provider.BinaryName()) is not null;
            }

            lock (Gate)
            {
                AvailabilityCache[provider] = available;
            }
        }
    });

    public static bool IsAvailable(this AgentProviderKind provider)
    {
        if (provider == AgentProviderKind.OpenClaw)
        {
            return !string.IsNullOrEmpty(Settings.Current.OpenClawAuthToken);
        }

        lock (Gate)
        {
            return AvailabilityCache.TryGetValue(provider, out var available) && available;
        }
    }

    public static AgentProviderKind FirstAvailable()
    {
        foreach (var provider in All)
        {
            if (provider.IsAvailable()) return provider;
        }
        return AgentProviderKind.Claude;
    }

    public static IAgentSession CreateSession(this AgentProviderKind provider) => provider switch
    {
        AgentProviderKind.Codex => new CodexSession(),
        AgentProviderKind.Copilot => new CopilotSession(),
        AgentProviderKind.Gemini => new GeminiSession(),
        AgentProviderKind.OpenCode => new OpenCodeSession(),
        AgentProviderKind.OpenClaw => new OpenClawSession(),
        _ => new ClaudeSession()
    };
}
