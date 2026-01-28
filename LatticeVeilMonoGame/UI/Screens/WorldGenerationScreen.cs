using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.Online.Lan;
using LatticeVeilMonoGame.UI;

namespace LatticeVeilMonoGame.UI.Screens;

public sealed class WorldGenerationScreen : IScreen
{
    private const int BlocksPerTick = 256;

    private readonly MenuStack _menus;
    private readonly AssetLoader _assets;
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Logger _log;
    private readonly PlayerProfile _profile;
    private readonly global::Microsoft.Xna.Framework.GraphicsDeviceManager _graphics;
    private readonly string _worldPath;
    private readonly string _metaPath;
    private readonly Action<string>? _onFinished;
    private readonly ILanSession? _lanSession;
    private readonly bool _enterWorldOnComplete;

    private Texture2D? _bg;
    private Texture2D? _panel;

    private Rectangle _viewport;
    private Rectangle _panelRect;
    private Rectangle _barRect;

    private WorldMeta? _meta;
    private VoxelWorldGenerator? _generator;
    private float _progress;
    private bool _done;
    private double _doneAt;
    private double _now;
    private string _status = "GENERATING...";
    private string _worldName = "WORLD";
    private GameMode _mode = GameMode.Sandbox;
    private bool _enteredWorld;

    public WorldGenerationScreen(MenuStack menus, AssetLoader assets, PixelFont font, Texture2D pixel, Logger log, PlayerProfile profile, global::Microsoft.Xna.Framework.GraphicsDeviceManager graphics, string worldName, GameMode mode, string worldPath, string metaPath, Action<string>? onFinished, ILanSession? lanSession = null, bool enterWorldOnComplete = true)
    {
        _menus = menus;
        _assets = assets;
        _font = font;
        _pixel = pixel;
        _log = log;
        _profile = profile;
        _graphics = graphics;
        _worldPath = worldPath;
        _metaPath = metaPath;
        _onFinished = onFinished;
        _lanSession = lanSession;
        _enterWorldOnComplete = enterWorldOnComplete;
        _worldName = worldName;
        _mode = mode;

        try
        {
            _bg = _assets.LoadTexture("textures/menu/backgrounds/WorldGeneration_bg.png");
            _panel = _assets.LoadTexture("textures/menu/GUIS/WorldGeneration_GUI.png");
        }
        catch (Exception ex)
        {
            _log.Warn($"World generation asset load: {ex.Message}");
        }
    }

    public void OnResize(Rectangle viewport)
    {
        _viewport = viewport;

        var panelW = Math.Clamp((int)(viewport.Width * 0.7f), 420, viewport.Width - 40);
        var panelH = Math.Clamp((int)(viewport.Height * 0.5f), 240, viewport.Height - 40);
        _panelRect = new Rectangle(
            viewport.X + (viewport.Width - panelW) / 2,
            viewport.Y + (viewport.Height - panelH) / 2,
            panelW,
            panelH);

        var margin = 30;
        var barW = _panelRect.Width - margin * 2;
        var barH = Math.Clamp((int)(panelH * 0.12f), 18, 28);
        var barX = _panelRect.X + margin;
        var barY = _panelRect.Center.Y - barH / 2;
        _barRect = new Rectangle(barX, barY, barW, barH);
    }

    public void Update(GameTime gameTime, InputState input)
    {
        _now = gameTime.TotalGameTime.TotalSeconds;

        if (_generator == null && !_done)
            InitializeGenerator();

        if (!_done)
        {
            GenerateBatch();
            return;
        }

        if (_now >= _doneAt && !_enteredWorld)
        {
            _enteredWorld = true;
            var viewport = _viewport;
            _menus.Pop();
            if (_enterWorldOnComplete)
                _menus.Push(new GameWorldScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, _worldPath, _metaPath, _lanSession), viewport);
        }
    }

    public void Draw(SpriteBatch sb, Rectangle viewport)
    {
        if (viewport != _viewport)
            OnResize(viewport);

        sb.Begin(samplerState: SamplerState.PointClamp);

        if (_bg is not null)
            sb.Draw(_bg, UiLayout.WindowViewport, Color.White);
        else
            sb.Draw(_pixel, UiLayout.WindowViewport, new Color(0, 0, 0));

        sb.End();

        sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiLayout.Transform);

        if (_panel is not null)
            sb.Draw(_panel, _panelRect, Color.White);
        else
            sb.Draw(_pixel, _panelRect, new Color(25, 25, 25));

        var title = "WORLD GENERATION";
        var titleSize = _font.MeasureString(title);
        _font.DrawString(sb, title, new Vector2(_panelRect.Center.X - titleSize.X / 2f, _panelRect.Y + 16), Color.White);

        var nameLine = $"WORLD: {_worldName}";
        var modeLine = $"MODE: {_mode.ToString().ToUpperInvariant()}";
        _font.DrawString(sb, nameLine, new Vector2(_panelRect.X + 24, _panelRect.Y + 44), Color.White);
        _font.DrawString(sb, modeLine, new Vector2(_panelRect.X + 24, _panelRect.Y + 44 + _font.LineHeight + 4), Color.White);

        DrawProgressBar(sb);

        var status = $"{_status} {(int)Math.Round(_progress * 100)}%";
        var statusSize = _font.MeasureString(status);
        _font.DrawString(sb, status, new Vector2(_panelRect.Center.X - statusSize.X / 2f, _barRect.Bottom + 12), Color.White);

        sb.End();
    }

    private void InitializeGenerator()
    {
        _meta = WorldMeta.Load(_metaPath, _log);
        if (_meta == null)
        {
            _status = "FAILED TO LOAD WORLD DATA";
            _done = true;
            _doneAt = _now + 1.2;
            return;
        }

        _worldName = _meta.Name;
        _mode = _meta.GameMode;
        _generator = new VoxelWorldGenerator(_meta, _worldPath);
        _status = "GENERATING...";
    }

    private void GenerateBatch()
    {
        if (_generator == null)
            return;

        _generator.Step(BlocksPerTick);
        _progress = _generator.Progress;

        if (_generator.IsComplete)
        {
            _progress = 1f;
            _status = "COMPLETE";
            _done = true;
            _doneAt = _now + 0.6;
            _log.Info($"World generated: {_worldName}");
            _onFinished?.Invoke(_worldName);
        }
    }

    private void DrawProgressBar(SpriteBatch sb)
    {
        sb.Draw(_pixel, _barRect, new Color(20, 20, 20));
        DrawBorder(sb, _barRect, Color.White);

        var fillW = (int)Math.Round((_barRect.Width - 4) * _progress);
        if (fillW > 0)
        {
            var fillRect = new Rectangle(_barRect.X + 2, _barRect.Y + 2, fillW, _barRect.Height - 4);
            sb.Draw(_pixel, fillRect, new Color(70, 160, 90));
        }
    }

    private void DrawBorder(SpriteBatch sb, Rectangle rect, Color color)
    {
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), color);
        sb.Draw(_pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), color);
    }
}


