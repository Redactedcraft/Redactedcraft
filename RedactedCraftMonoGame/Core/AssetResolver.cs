using System.IO;

namespace RedactedCraftMonoGame.Core;

public static class AssetResolver
{
    public static string Resolve(string relativePath) => Paths.ResolveAssetPath(relativePath);

    public static bool TryResolve(string relativePath, out string fullPath)
    {
        fullPath = Paths.ResolveAssetPath(relativePath);
        return File.Exists(fullPath);
    }

    public static string DescribeInstallLocation() => Paths.AssetsDir;
}
