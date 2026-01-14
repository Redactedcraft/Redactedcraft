using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace RedactedCraftMonoGame.Core;

/// <summary>
/// Asset loader wrapper that checks development texture location first, then falls back to normal assets
/// Used only for AssetViewer to test textures from the development location
/// </summary>
public sealed class DevelopmentAssetLoader : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly string _devTexturesDir;
    private readonly AssetLoader _fallback;
    private readonly Dictionary<string, Texture2D> _devTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger? _log;

    public DevelopmentAssetLoader(GraphicsDevice graphicsDevice, AssetLoader fallback, Logger? log = null)
    {
        _graphicsDevice = graphicsDevice;
        _devTexturesDir = @"C:\Users\Redacted\Documents\RedactedcraftCsharp\RedactedCraftMonoGame\Defaults\Assets\textures";
        _fallback = fallback;
        _log = log;
    }

    public GraphicsDevice GraphicsDevice => _graphicsDevice;

    public Texture2D LoadTexture(string relativePath)
    {
        // Use a normalized cache key (forward slashes)
        var key = relativePath.Replace('\\', '/');
        if (_devTextures.TryGetValue(key, out var cached))
            return cached;

        // First try development location
        var devPath = Path.Combine(_devTexturesDir, relativePath);
        if (File.Exists(devPath))
        {
            try
            {
                using var fs = File.OpenRead(devPath);
                var tex = Texture2D.FromStream(_graphicsDevice, fs);
                _devTextures[key] = tex;
                return tex;
            }
            catch
            {
                // Fall back to normal loader if dev texture fails
                return _fallback.LoadTexture(relativePath);
            }
        }

        // Fall back to normal asset loading
        return _fallback.LoadTexture(relativePath);
    }

    public void Dispose()
    {
        foreach (var texture in _devTextures.Values)
            texture.Dispose();
    }
}
