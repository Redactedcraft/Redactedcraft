using System;
using System.IO;

namespace RedactedCraftMonoGame.Core;

public static class AssetInstaller
{
    /// <summary>
    /// Copies Defaults/Assets into Documents\RedactedCraft\Assets if files are missing.
    /// Defaults are install-source only; runtime always reads from Documents\RedactedCraft\Assets.
    /// </summary>
    public static void EnsureDefaultsInstalled(string defaultsRoot, Logger log)
    {
        try
        {
            Directory.CreateDirectory(Paths.AssetsDir);

            var defaultsAssets = Path.Combine(defaultsRoot, "Assets");
            if (!Directory.Exists(defaultsAssets))
            {
                log.Warn($"Defaults assets folder missing: {defaultsAssets}");
                return;
            }

            foreach (var srcPath in Directory.GetFiles(defaultsAssets, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(defaultsAssets, srcPath);
                var dst = Path.Combine(Paths.AssetsDir, rel);

                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                if (!File.Exists(dst))
                {
                    File.Copy(srcPath, dst);
                    log.Info($"Installed default asset: {rel}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"EnsureDefaultsInstalled failed: {ex.Message}");
        }
    }
}
