namespace RedactedCraftMonoGame.Core;

public sealed class GameStartOptions
{
    public string? JoinToken { get; init; }
    public bool Offline { get; init; }
    public bool Smoke { get; init; }
    public bool SmokeAssetsOk { get; init; }
    public string[]? SmokeMissingAssets { get; init; }
    public bool AssetView { get; init; }
    
    // Asset mode properties
    public string AssetMode { get; init; } = "online"; // local | repo | online
    public string? AssetRoot { get; init; }

    public bool HasJoinToken => !string.IsNullOrWhiteSpace(JoinToken);
}
