using System.IO;

namespace RedactedCraftMonoGame.Core;

public static class AssetResolver
{
    private static Logger? _logger;

    public static void Initialize(Logger logger)
    {
        _logger = logger;
    }

    public static string Resolve(string relativePath) => Paths.ResolveAssetPath(relativePath);

    public static bool TryResolve(string relativePath, out string fullPath)
    {
        fullPath = Paths.ResolveAssetPath(relativePath);
        var exists = File.Exists(fullPath);
        
        if (!exists && _logger != null)
        {
            // Log missing asset with attempted paths
            var defaultsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults", "Assets");
            var defaultsPath = Path.Combine(defaultsDir, relativePath);
            var documentsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RedactedCraft", "Assets");
            var documentsPath = Path.Combine(documentsDir, relativePath);
            
            _logger.Warn($"Missing asset: {relativePath}");
            _logger.Warn($"  Attempted: {fullPath}");
            if (File.Exists(defaultsPath))
                _logger.Warn($"  Available fallback: {defaultsPath}");
        }
        
        return exists;
    }

    public static string DescribeInstallLocation() => Paths.AssetsDir;
}
