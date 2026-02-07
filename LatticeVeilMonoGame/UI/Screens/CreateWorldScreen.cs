using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.UI;

namespace LatticeVeilMonoGame.UI.Screens;

public sealed class CreateWorldScreen : IScreen
{
    private const int MaxWorldNameLength = 32;
    private const int PanelMaxWidth = 1300;
    private const int PanelMaxHeight = 700;
    private const int ControlShrinkPixels = 60; // ~2 inches at 30 px/in
    private const int ContentDownShiftPixels = 30; // ~1 inch at 30 px/in
    private const int SpawnChunkPregenerationRadius = 8; // Match runtime prewarm radius
    private const int SpawnMeshPregenerationRadius = 3; // Match runtime gate radius

    private readonly MenuStack _menus;
    private readonly AssetLoader _assets;
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Logger _log;
    private readonly PlayerProfile _profile;
    private readonly global::Microsoft.Xna.Framework.GraphicsDeviceManager _graphics;
    private readonly Action<string> _onWorldCreated;

    private Texture2D? _bg;
    private Texture2D? _panel;
    private Texture2D? _artificerTexture;
    private Texture2D? _artificerSelectedTexture;
    private Texture2D? _veilwalkerTexture;
    private Texture2D? _veilwalkerSelectedTexture;
    private Texture2D? _veilseerTexture;
    private Texture2D? _veilseerSelectedTexture;

    private Rectangle _viewport;
    private Rectangle _panelRect;
    private Rectangle _contentArea;
    private Rectangle _worldNameRect;

    // UI Elements
    private readonly Button _createBtn;
    private readonly Button _cancelBtn;
    private readonly Button _artificerBtn;
    private readonly Button _veilwalkerBtn;
    private readonly Button _veilseerBtn;
    private readonly Checkbox _generateStructures;
    private readonly Checkbox _generateCaves;
    private readonly Checkbox _generateOres;
    private readonly Checkbox _multipleHomes;
    private readonly Button _homeSlotsBtn;

    // World settings
    private string _worldName = "New World";
    private Core.GameMode _selectedGameMode = Core.GameMode.Artificer;
    private int _worldSize = 2; // 0=Small, 1=Medium, 2=Large, 3=Huge
    private int _difficulty = 1; // 0=Peaceful, 1=Easy, 2=Normal, 3=Hard
    private bool _structuresEnabled = true;
    private bool _cavesEnabled = true;
    private bool _oresEnabled = true;
    private bool _multipleHomesEnabled = true;
    private int _maxHomesPerPlayer = 8;
    private bool _worldNameActive = true;
    private string _statusMessage = string.Empty;
    private double _statusUntil;
    private double _now;

    public CreateWorldScreen(MenuStack menus, AssetLoader assets, PixelFont font, Texture2D pixel, Logger log, PlayerProfile profile, global::Microsoft.Xna.Framework.GraphicsDeviceManager graphics, Action<string> onWorldCreated)
    {
        _menus = menus;
        _assets = assets;
        _font = font;
        _pixel = pixel;
        _log = log;
        _profile = profile;
        _graphics = graphics;
        _onWorldCreated = onWorldCreated;

        // Create UI elements
        _createBtn = new Button("CREATE WORLD", CreateWorld);
        _cancelBtn = new Button("CANCEL", () => _menus.Pop());
        _artificerBtn = new Button("ARTIFICER", () => SetGameMode(Core.GameMode.Artificer));
        _veilwalkerBtn = new Button("VEILWALKER", () => SetGameMode(Core.GameMode.Veilwalker));
        _veilseerBtn = new Button("VEILSEER", () => SetGameMode(Core.GameMode.Veilseer));
        _generateStructures = new Checkbox("Generate Structures", _structuresEnabled);
        _generateCaves = new Checkbox("Generate Caves", _cavesEnabled);
        _generateOres = new Checkbox("Generate Ores", _oresEnabled);
        _multipleHomes = new Checkbox("Enable Multiple Homes", _multipleHomesEnabled);
        _homeSlotsBtn = new Button(string.Empty, CycleMaxHomesPerPlayer);
        SyncHomeSlotsLabel();

        LoadAssets();
        RefreshGameModeButtonTextures();
    }

    private void LoadAssets()
    {
        _log.Info("CreateWorldScreen - Loading assets...");
        _bg = TryLoadTexture("textures/menu/backgrounds/CreateWorld_bg.png");
        _panel = TryLoadTexture("textures/menu/GUIS/CreateWorld_GUI.png");
        _createBtn.Texture = TryLoadTexture("textures/menu/buttons/CreateWorld.png");
        _cancelBtn.Texture = TryLoadTexture("textures/menu/buttons/Back.png");

        _artificerTexture = TryLoadTexture("textures/menu/buttons/Artificer.png");
        _artificerSelectedTexture = TryLoadTexture("textures/menu/buttons/ArtificerSelected.png");
        _veilwalkerTexture = TryLoadTexture("textures/menu/buttons/Veilwalker.png");
        _veilwalkerSelectedTexture = TryLoadTexture("textures/menu/buttons/VeilwalkerSelected.png");
        _veilseerTexture = TryLoadTexture("textures/menu/buttons/Veilseer.png");
        _veilseerSelectedTexture = TryLoadTexture("textures/menu/buttons/VeilseerSelected.png");

        RefreshGameModeButtonTextures();
        _log.Info("CreateWorldScreen - Assets loaded");
    }

    public void OnResize(Rectangle viewport)
    {
        _viewport = viewport;

        var panelW = Math.Min(PanelMaxWidth, viewport.Width - 20);
        var panelH = Math.Min(PanelMaxHeight, viewport.Height - 30);
        _panelRect = new Rectangle(
            viewport.X + (viewport.Width - panelW) / 2,
            viewport.Y + (viewport.Height - panelH) / 2,
            panelW,
            panelH);

        // Keep interactive controls inside the visible center of the ornate panel texture.
        var innerPadX = Math.Clamp(_panelRect.Width / 16, 38, 72);
        var innerPadTop = Math.Clamp(_panelRect.Height / 8, 44, 78);
        var innerPadBottom = Math.Clamp(_panelRect.Height / 10, 34, 60);
        _contentArea = new Rectangle(
            _panelRect.X + innerPadX,
            _panelRect.Y + innerPadTop,
            _panelRect.Width - innerPadX * 2,
            _panelRect.Height - innerPadTop - innerPadBottom
        );

        var worldNameY = _contentArea.Y + 8 + ContentDownShiftPixels;
        var worldNameAreaX = _contentArea.X;
        var worldNameAreaW = _contentArea.Width;
        var worldNameW = Math.Clamp(worldNameAreaW - ControlShrinkPixels, 280, 980);
        _worldNameRect = new Rectangle(
            worldNameAreaX + (worldNameAreaW - worldNameW) / 2,
            worldNameY + _font.LineHeight + 8,
            worldNameW,
            _font.LineHeight + 18);

        var modeButtonGap = 20;
        var baseModeButtonW = Math.Clamp((_contentArea.Width - modeButtonGap * 4) / 3, 140, 260);
        var modeButtonW = Math.Max(100, baseModeButtonW - ControlShrinkPixels);
        var modeButtonH = Math.Clamp((int)(modeButtonW * 0.34f), 44, 96);
        var modeY = _worldNameRect.Bottom + 36;
        var modeStartX = _contentArea.X + (_contentArea.Width - (modeButtonW * 3 + modeButtonGap * 2)) / 2;

        _artificerBtn.Bounds = new Rectangle(modeStartX, modeY, modeButtonW, modeButtonH);
        _veilwalkerBtn.Bounds = new Rectangle(modeStartX + modeButtonW + modeButtonGap, modeY, modeButtonW, modeButtonH);
        _veilseerBtn.Bounds = new Rectangle(modeStartX + (modeButtonW + modeButtonGap) * 2, modeY, modeButtonW, modeButtonH);

        var checkboxY = _artificerBtn.Bounds.Bottom + 36;
        LayoutCenteredToggle(_generateStructures, checkboxY);
        LayoutCenteredToggle(_generateCaves, checkboxY + 32);
        LayoutCenteredToggle(_generateOres, checkboxY + 64);
        LayoutCenteredToggle(_multipleHomes, checkboxY + 96);
        _homeSlotsBtn.Bounds = new Rectangle(
            _contentArea.Center.X - 110,
            checkboxY + 132,
            220,
            30);
        _homeSlotsBtn.Enabled = _multipleHomesEnabled;
        _homeSlotsBtn.ForceDisabledStyle = !_multipleHomesEnabled;

        // Match Singleplayer world-list screen create button sizing/placement.
        var rowButtonW = Math.Clamp((int)(_panelRect.Width * 0.28f), 160, 260);
        var rowButtonH = Math.Clamp((int)(rowButtonW * 0.28f), 40, 70);
        var buttonY = _panelRect.Bottom - 24 - rowButtonH - 90;
        var createBtnW = panelW / 3 - 15;
        var createBtnH = (int)(createBtnW * 0.25f);
        var createBtnX = _panelRect.X + (_panelRect.Width - createBtnW) / 2;
        _createBtn.Bounds = new Rectangle(createBtnX, buttonY, createBtnW, createBtnH);

        // Match Options screen back button position (bottom-left of full screen).
        var backBtnMargin = 20;
        var backBtnBaseW = Math.Max(_cancelBtn.Texture?.Width ?? 0, 320);
        var backBtnBaseH = Math.Max(_cancelBtn.Texture?.Height ?? 0, (int)(backBtnBaseW * 0.28f));
        var backBtnScale = Math.Min(1f, Math.Min(240f / backBtnBaseW, 240f / backBtnBaseH));
        var backBtnW = Math.Max(1, (int)Math.Round(backBtnBaseW * backBtnScale));
        var backBtnH = Math.Max(1, (int)Math.Round(backBtnBaseH * backBtnScale));
        _cancelBtn.Bounds = new Rectangle(
            viewport.X + backBtnMargin,
            viewport.Bottom - backBtnMargin - backBtnH,
            backBtnW,
            backBtnH);
    }

    public void Update(GameTime gameTime, InputState input)
    {
        _now = gameTime.TotalGameTime.TotalSeconds;
        if (_statusUntil > 0 && _now >= _statusUntil)
            _statusMessage = string.Empty;

        if (input.IsNewKeyPress(Keys.Escape))
        {
            _menus.Pop();
            return;
        }

        if (input.IsNewLeftClick())
        {
            _worldNameActive = _worldNameRect.Contains(input.MousePosition);
        }

        if (_worldNameActive)
        {
            HandleTextInput(input, ref _worldName, MaxWorldNameLength);
            if (input.IsNewKeyPress(Keys.Enter))
            {
                CreateWorld();
                return;
            }
        }

        _artificerBtn.Update(input);
        _veilwalkerBtn.Update(input);
        _veilseerBtn.Update(input);

        if (!_worldNameActive && input.IsNewKeyPress(Keys.Left))
        {
            SetGameMode(_selectedGameMode switch
            {
                Core.GameMode.Artificer => Core.GameMode.Veilseer,
                Core.GameMode.Veilwalker => Core.GameMode.Artificer,
                _ => Core.GameMode.Veilwalker
            });
        }
        else if (!_worldNameActive && input.IsNewKeyPress(Keys.Right))
        {
            SetGameMode(_selectedGameMode switch
            {
                Core.GameMode.Artificer => Core.GameMode.Veilwalker,
                Core.GameMode.Veilwalker => Core.GameMode.Veilseer,
                _ => Core.GameMode.Artificer
            });
        }

        _generateStructures.Update(input);
        _structuresEnabled = _generateStructures.Value;
        
        _generateCaves.Update(input);
        _cavesEnabled = _generateCaves.Value;
        
        _generateOres.Update(input);
        _oresEnabled = _generateOres.Value;

        _multipleHomes.Update(input);
        _multipleHomesEnabled = _multipleHomes.Value;
        _homeSlotsBtn.Enabled = _multipleHomesEnabled;
        _homeSlotsBtn.ForceDisabledStyle = !_multipleHomesEnabled;
        _homeSlotsBtn.Update(input);

        _createBtn.Update(input);
        _cancelBtn.Update(input);
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

        // Draw panel
        if (_panel != null)
        {
            DrawNinePatch(sb, _panel, _panelRect);
        }
        else
        {
            sb.Draw(_pixel, _panelRect, new Color(25, 25, 25));
        }

        // Draw title
        const int borderSize = 16;
        var title = "CREATE NEW WORLD";
        var titleSize = _font.MeasureString(title);
        var titlePos = new Vector2(_panelRect.Center.X - titleSize.X / 2f, _panelRect.Y + 16 + borderSize);
        _font.DrawString(sb, title, titlePos, Color.White);

        // Draw world info
        var x = _contentArea.X + 24;
        _font.DrawString(sb, "WORLD NAME", new Vector2(_worldNameRect.X, _worldNameRect.Y - _font.LineHeight - 6), Color.White);
        sb.Draw(_pixel, _worldNameRect, _worldNameActive ? new Color(35, 35, 35, 235) : new Color(20, 20, 20, 220));
        DrawBorder(sb, _worldNameRect, Color.White);

        var worldNameText = string.IsNullOrWhiteSpace(_worldName) ? "(enter world name)" : _worldName;
        var worldNameColor = string.IsNullOrWhiteSpace(_worldName) ? new Color(180, 180, 180) : Color.White;
        var worldNamePos = new Vector2(_worldNameRect.X + 8, _worldNameRect.Y + (_worldNameRect.Height - _font.LineHeight) / 2f);
        _font.DrawString(sb, worldNameText, worldNamePos, worldNameColor);

        if (_worldNameActive && ((_now * 2.0) % 2.0) < 1.0)
        {
            var cursorText = _worldName;
            var cursorX = worldNamePos.X + _font.MeasureString(cursorText).X + 2f;
            var cursorRect = new Rectangle((int)cursorX, _worldNameRect.Y + 6, 2, _worldNameRect.Height - 12);
            sb.Draw(_pixel, cursorRect, Color.White);
        }

        _font.DrawString(sb, "GAME MODE", new Vector2(x, _worldNameRect.Bottom + 18), Color.White);

        _artificerBtn.Draw(sb, _pixel, _font);
        _veilwalkerBtn.Draw(sb, _pixel, _font);
        _veilseerBtn.Draw(sb, _pixel, _font);

        var detailsY = _artificerBtn.Bounds.Bottom + 10;
        var detailColumn = Math.Max(200, _contentArea.Width / 4);
        _font.DrawString(sb, $"World Size: {GetWorldSizeLabel()}", new Vector2(x, detailsY), Color.White);
        _font.DrawString(sb, $"Difficulty: {GetDifficultyLabel()}", new Vector2(x + detailColumn, detailsY), Color.White);
        _font.DrawString(sb, $"Selected: {_selectedGameMode.ToString().ToUpperInvariant()}", new Vector2(x + detailColumn * 2, detailsY), Color.White);

        // Draw checkboxes
        _generateStructures.Draw(sb, _pixel, _font);
        _generateCaves.Draw(sb, _pixel, _font);
        _generateOres.Draw(sb, _pixel, _font);
        _multipleHomes.Draw(sb, _pixel, _font);
        _homeSlotsBtn.Draw(sb, _pixel, _font);

        // Draw buttons
        _createBtn.Draw(sb, _pixel, _font);
        _cancelBtn.Draw(sb, _pixel, _font);

        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            var statusSize = _font.MeasureString(_statusMessage);
            var statusPos = new Vector2(_panelRect.Center.X - statusSize.X / 2f, _createBtn.Bounds.Top - _font.LineHeight - 8);
            _font.DrawString(sb, _statusMessage, statusPos, new Color(230, 210, 90));
        }

        sb.End();
    }

    private string GetWorldSizeLabel()
    {
        return _worldSize switch
        {
            0 => "Small",
            1 => "Medium", 
            2 => "Large",
            3 => "Huge",
            _ => "Large"
        };
    }

    private string GetDifficultyLabel()
    {
        return _difficulty switch
        {
            0 => "Peaceful",
            1 => "Easy",
            2 => "Normal", 
            3 => "Hard",
            _ => "Normal"
        };
    }

    private void SetGameMode(Core.GameMode mode)
    {
        if (_selectedGameMode == mode)
            return;

        _selectedGameMode = mode;
        RefreshGameModeButtonTextures();
    }

    private void RefreshGameModeButtonTextures()
    {
        _artificerBtn.Texture = _selectedGameMode == Core.GameMode.Artificer
            ? _artificerSelectedTexture ?? _artificerTexture
            : _artificerTexture;

        _veilwalkerBtn.Texture = _selectedGameMode == Core.GameMode.Veilwalker
            ? _veilwalkerSelectedTexture ?? _veilwalkerTexture
            : _veilwalkerTexture;

        _veilseerBtn.Texture = _selectedGameMode == Core.GameMode.Veilseer
            ? _veilseerSelectedTexture ?? _veilseerTexture
            : _veilseerTexture;
    }

    private Texture2D? TryLoadTexture(string assetPath)
    {
        try
        {
            return _assets.LoadTexture(assetPath);
        }
        catch (Exception ex)
        {
            _log.Warn($"Missing texture '{assetPath}': {ex.Message}");
            return null;
        }
    }

    private void DrawNinePatch(SpriteBatch sb, Texture2D texture, Rectangle destination)
    {
        if (texture == null) return;
        
        const int borderSize = 16;
        var source = new Rectangle(0, 0, texture.Width, texture.Height);
        
        var patches = new[]
        {
            new Rectangle(source.X, source.Y, borderSize, borderSize),
            new Rectangle(source.X + borderSize, source.Y, source.Width - borderSize * 2, borderSize),
            new Rectangle(source.Right - borderSize, source.Y, borderSize, borderSize),
            new Rectangle(source.X, source.Y + borderSize, borderSize, source.Height - borderSize * 2),
            new Rectangle(source.X + borderSize, source.Y + borderSize, source.Width - borderSize * 2, source.Height - borderSize * 2),
            new Rectangle(source.Right - borderSize, source.Y + borderSize, borderSize, source.Height - borderSize * 2),
            new Rectangle(source.X, source.Bottom - borderSize, borderSize, borderSize),
            new Rectangle(source.X + borderSize, source.Bottom - borderSize, source.Width - borderSize * 2, borderSize),
            new Rectangle(source.Right - borderSize, source.Bottom - borderSize, borderSize, borderSize)
        };
        
        var destPatches = new[]
        {
            new Rectangle(destination.X, destination.Y, borderSize, borderSize),
            new Rectangle(destination.X + borderSize, destination.Y, destination.Width - borderSize * 2, borderSize),
            new Rectangle(destination.Right - borderSize, destination.Y, borderSize, borderSize),
            new Rectangle(destination.X, destination.Y + borderSize, borderSize, destination.Height - borderSize * 2),
            new Rectangle(destination.X + borderSize, destination.Y + borderSize, destination.Width - borderSize * 2, destination.Height - borderSize * 2),
            new Rectangle(destination.Right - borderSize, destination.Y + borderSize, borderSize, destination.Height - borderSize * 2),
            new Rectangle(destination.X, destination.Bottom - borderSize, borderSize, borderSize),
            new Rectangle(destination.X + borderSize, destination.Bottom - borderSize, destination.Width - borderSize * 2, borderSize),
            new Rectangle(destination.Right - borderSize, destination.Bottom - borderSize, borderSize, borderSize)
        };
        
        for (int i = 0; i < 9; i++)
        {
            sb.Draw(texture, destPatches[i], patches[i], Color.White);
        }
    }

    private void CreateWorld()
    {
        var worldName = NormalizeWorldName(_worldName);
        _worldName = worldName;
        if (string.IsNullOrWhiteSpace(worldName))
        {
            _log.Warn("Cannot create world: empty name");
            SetStatus("ENTER A WORLD NAME");
            return;
        }

        try
        {
            _log.Info($"Creating new world: {worldName}");
            _log.Info($"Settings: GameMode={_selectedGameMode}, Size={_worldSize}, Difficulty={_difficulty}");
            _log.Info($"Features: Structures={_structuresEnabled}, Caves={_cavesEnabled}, Ores={_oresEnabled}");

            // Create world directory
            var worldPath = Path.Combine(Paths.WorldsDir, worldName);
            if (Directory.Exists(worldPath))
            {
                _log.Warn($"World directory already exists: {worldPath}");
                SetStatus("WORLD NAME ALREADY EXISTS");
                return;
            }

            Directory.CreateDirectory(worldPath);
            var (width, height, depth) = GetWorldDimensions();
            var seed = Environment.TickCount;

            // Create world metadata
            var meta = WorldMeta.CreateFlat(worldName, _selectedGameMode, width, height, depth, seed);
            meta.CreatedAt = DateTimeOffset.UtcNow.ToString("O");
            meta.PlayerCollision = true;
            meta.EnableMultipleHomes = _multipleHomesEnabled;
            meta.MaxHomesPerPlayer = _multipleHomesEnabled ? Math.Clamp(_maxHomesPerPlayer, 1, 32) : 1;

            // Save world metadata
            var metaPath = Path.Combine(worldPath, "world.json");
            meta.Save(metaPath, _log);
            PregenerateSpawnChunks(meta, worldPath);

            _log.Info($"World created successfully: {worldName}");
            _onWorldCreated?.Invoke(worldName);
            _menus.Pop();
        }
        catch (Exception ex)
        {
            SetStatus("FAILED TO CREATE WORLD");
            _log.Error($"Failed to create world: {ex.Message}");
            _log.Error($"Stack trace: {ex.StackTrace}");
        }
    }

    private void HandleTextInput(InputState input, ref string value, int maxLen)
    {
        var shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
        foreach (var key in input.GetNewKeys())
        {
            if (key == Keys.Back)
            {
                if (value.Length > 0)
                    value = value.Substring(0, value.Length - 1);
                continue;
            }

            if (key == Keys.Space)
            {
                Append(ref value, ' ', maxLen);
                continue;
            }

            if (key == Keys.OemMinus || key == Keys.Subtract)
            {
                Append(ref value, shift ? '_' : '-', maxLen);
                continue;
            }

            if (key == Keys.OemPeriod || key == Keys.Decimal)
            {
                Append(ref value, '.', maxLen);
                continue;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                Append(ref value, (char)('0' + (key - Keys.D0)), maxLen);
                continue;
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                Append(ref value, (char)('0' + (key - Keys.NumPad0)), maxLen);
                continue;
            }

            if (key >= Keys.A && key <= Keys.Z)
            {
                var c = (char)('A' + (key - Keys.A));
                if (!shift)
                    c = char.ToLowerInvariant(c);
                Append(ref value, c, maxLen);
            }
        }
    }

    private static void Append(ref string value, char c, int maxLen)
    {
        if (value.Length >= maxLen)
            return;
        value += c;
    }

    private static string NormalizeWorldName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;

        var chars = value.ToCharArray();
        var invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        return new string(chars).Trim();
    }

    private void SetStatus(string message, double seconds = 2.5)
    {
        _statusMessage = message;
        _statusUntil = seconds <= 0 ? 0 : _now + seconds;
    }

    private void LayoutCenteredToggle(Checkbox checkbox, int y)
    {
        const int boxSize = 25;
        const int labelGap = 10;
        var labelWidth = (int)MathF.Ceiling(_font.MeasureString(checkbox.Label).X);
        var rowWidth = Math.Max(200, Math.Max(boxSize + labelGap + labelWidth, 280 - ControlShrinkPixels));
        var x = _contentArea.Center.X - rowWidth / 2;
        checkbox.Bounds = new Rectangle(x, y, rowWidth, boxSize);
    }

    private void CycleMaxHomesPerPlayer()
    {
        if (!_multipleHomesEnabled)
            return;

        _maxHomesPerPlayer++;
        if (_maxHomesPerPlayer > 20)
            _maxHomesPerPlayer = 2;
        SyncHomeSlotsLabel();
    }

    private void SyncHomeSlotsLabel()
    {
        _homeSlotsBtn.Label = $"HOME SLOTS: {_maxHomesPerPlayer}";
    }

    private (int width, int height, int depth) GetWorldDimensions()
    {
        return _worldSize switch
        {
            0 => (256, 256, 256),
            1 => (384, 256, 384),
            2 => (512, 256, 512),
            3 => (768, 256, 768),
            _ => (512, 256, 512)
        };
    }

    private void DrawBorder(SpriteBatch sb, Rectangle rect, Color color)
    {
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), color);
        sb.Draw(_pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), color);
    }

    private void PregenerateSpawnChunks(WorldMeta meta, string worldPath)
    {
        try
        {
            var world = new VoxelWorld(meta, worldPath, _log);
            Directory.CreateDirectory(world.ChunksDir);

            var spawn = GetSpawnPoint(meta);
            var spawnChunk = VoxelWorld.WorldToChunk((int)spawn.X, 0, (int)spawn.Y, out _, out _, out _);
            var chunkRadius = Math.Max(0, SpawnChunkPregenerationRadius);
            var chunkRadiusSq = chunkRadius * chunkRadius;
            var generatedCoords = new List<ChunkCoord>();
            var meshCoords = new List<ChunkCoord>();

            for (var dz = -chunkRadius; dz <= chunkRadius; dz++)
            {
                for (var dx = -chunkRadius; dx <= chunkRadius; dx++)
                {
                    var distSq = dx * dx + dz * dz;
                    if (distSq > chunkRadiusSq)
                        continue;

                    var coord = new ChunkCoord(spawnChunk.X + dx, 0, spawnChunk.Z + dz);
                    world.GetOrCreateChunk(coord);
                    generatedCoords.Add(coord);

                    if (distSq <= SpawnMeshPregenerationRadius * SpawnMeshPregenerationRadius)
                        meshCoords.Add(coord);
                }
            }

            // Save all pregenerated chunks now (do not use batched SaveModifiedChunks limit).
            foreach (var coord in generatedCoords)
                world.SaveChunk(coord);

            // Prebake gate meshes and cache them to disk to make first join much faster.
            var settings = GameSettings.LoadOrCreate(_log);
            var atlas = CubeNetAtlas.Build(_assets, _log, settings.QualityPreset);
            var cachedMeshes = 0;
            if (atlas != null)
            {
                try
                {
                    foreach (var coord in meshCoords)
                    {
                        if (!world.TryGetChunk(coord, out var chunk) || chunk == null)
                            continue;

                        var mesh = VoxelMesherGreedy.BuildChunkMeshFast(world, chunk, atlas, _log);
                        ChunkMeshCache.Save(worldPath, mesh);
                        cachedMeshes++;
                    }
                }
                finally
                {
                    atlas.Texture.Dispose();
                }
            }

            _log.Info($"Spawn pregeneration complete: chunks={generatedCoords.Count}, cachedMeshes={cachedMeshes}, center={spawnChunk}.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Spawn pregeneration failed: {ex.Message}");
        }
    }

    private static Vector2 GetSpawnPoint(WorldMeta meta)
    {
        var spawnX = meta.Size.Width * 0.25f;
        var spawnZ = meta.Size.Depth * 0.25f;
        spawnX = Math.Max(16f, Math.Min(spawnX, meta.Size.Width - 16f));
        spawnZ = Math.Max(16f, Math.Min(spawnZ, meta.Size.Depth - 16f));
        return new Vector2(spawnX, spawnZ);
    }
}
