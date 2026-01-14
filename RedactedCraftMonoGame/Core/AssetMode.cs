namespace RedactedCraftMonoGame.Core;

/// <summary>
/// Asset loading mode configuration
/// </summary>
public sealed class AssetMode
{
    public string Mode { get; init; } = "online"; // local | repo | online
    public string? Root { get; init; }
    public string DefaultsDir { get; init; } = "";
    public string DocumentsDir { get; init; } = "";
    public bool OnlineFetchEnabled { get; init; } = true;

    public static AssetMode FromOptions(GameStartOptions options, Logger? logger = null)
    {
        var mode = options.AssetMode.ToLowerInvariant();
        var defaultsDir = GetDefaultsDirectory();
        
        string documentsDir = "";
        string? root = null;
        bool onlineFetch = true;

        switch (mode)
        {
            case "local":
                documentsDir = ""; // Disable Documents
                root = defaultsDir; // Only use Defaults
                onlineFetch = false;
                break;
                
            case "repo":
                documentsDir = ""; // Disable Documents
                root = options.AssetRoot ?? defaultsDir; // Use repo root, fallback to Defaults
                onlineFetch = false;
                break;
                
            case "online":
            default:
                documentsDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "RedactedCraft");
                root = null; // Use normal logic
                onlineFetch = true;
                break;
        }

        return new AssetMode
        {
            Mode = mode,
            Root = root,
            DefaultsDir = defaultsDir,
            DocumentsDir = documentsDir,
            OnlineFetchEnabled = onlineFetch
        };
    }

    private static string GetDefaultsDirectory()
    {
        // Get the directory where the executable is running
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        return System.IO.Path.Combine(exeDir, "Defaults", "Assets");
    }

    public string GetAssetsDir()
    {
        if (!string.IsNullOrEmpty(Root))
            return Root;
            
        if (!string.IsNullOrEmpty(DocumentsDir))
            return DocumentsDir;
            
        return DefaultsDir;
    }

    public string ResolveAssetPath(string relativePath)
    {
        // Try repo root first (if specified)
        if (!string.IsNullOrEmpty(Root) && Root != DefaultsDir)
        {
            var repoPath = System.IO.Path.Combine(Root, relativePath);
            if (System.IO.File.Exists(repoPath))
                return repoPath;
        }
        
        // Try Documents (if enabled)
        if (!string.IsNullOrEmpty(DocumentsDir))
        {
            var docsPath = System.IO.Path.Combine(DocumentsDir, relativePath);
            if (System.IO.File.Exists(docsPath))
                return docsPath;
        }
        
        // Fallback to Defaults
        return System.IO.Path.Combine(DefaultsDir, relativePath);
    }
}
