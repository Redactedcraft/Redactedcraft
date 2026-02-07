using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.UI;

namespace LatticeVeilMonoGame.UI.Screens;

public sealed class SingleplayerScreen : IScreen
{
    private const int ScrollStep = 40;

    private readonly MenuStack _menus;
    private readonly AssetLoader _assets;
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Logger _log;
    private readonly global::Microsoft.Xna.Framework.GraphicsDeviceManager _graphics;
    private readonly PlayerProfile _profile;

    private Texture2D? _bg;
    private Texture2D? _panel;

    private readonly Button _createBtn;
    private readonly Button _deleteBtn;
    private readonly Button _backBtn;
    private readonly Button _seedInfoBtn;
    private GameSettings _settings;
    private bool _showSeedInfo;

    private Rectangle _viewport;
    private Rectangle _panelRect;
    private Rectangle _listRect;
    private float _scroll;
    private int _rowHeight;
    private int _selectedIndex = -1;
    private double _now;
    private const double DoubleClickSeconds = 0.35;
    private double _lastClickTime;
    private int _lastClickIndex = -1;
    private string? _statusMessage;
    private double _statusUntil;
    private Point _overlayMousePos;
    private int _hoverWorldIndex = -1;

    private List<WorldListEntry> _worlds = new();

    public SingleplayerScreen(MenuStack menus, AssetLoader assets, PixelFont font, Texture2D pixel, Logger log, PlayerProfile profile, global::Microsoft.Xna.Framework.GraphicsDeviceManager graphics)
    {
        _menus = menus;
        _assets = assets;
        _font = font;
        _pixel = pixel;
        _log = log;
        _graphics = graphics;
        _profile = profile;
        _settings = GameSettings.LoadOrCreate(_log);
        _showSeedInfo = _settings.ShowSeedInfoInWorldList;

        _createBtn = new Button("CREATE WORLD", OpenCreateWorld);
        _deleteBtn = new Button("DELETE WORLD", DeleteSelectedWorld);
        _backBtn = new Button("BACK", () => _menus.Pop());
        _seedInfoBtn = new Button("", ToggleSeedInfo);
        SyncSeedInfoLabel();

        try
        {
            _log.Info("SingleplayerScreen - Loading assets...");
            _bg = _assets.LoadTexture("textures/menu/backgrounds/singleplayer_bg.png");
            _panel = _assets.LoadTexture("textures/menu/GUIS/Singleplayer_GUI.png");
            _createBtn.Texture = _assets.LoadTexture("textures/menu/buttons/CreateWorld.png");
            _deleteBtn.Texture = _assets.LoadTexture("textures/menu/buttons/DeleteDefault.png");
            _backBtn.Texture = _assets.LoadTexture("textures/menu/buttons/Back.png");
            
            _log.Info($"SingleplayerScreen - Assets loaded successfully. Panel: {(_panel != null ? "LOADED" : "NULL")}");
        }
        catch (Exception ex)
        {
            _log.Error($"Singleplayer asset load failed: {ex.Message}");
            _log.Error($"Singleplayer asset load stack trace: {ex.StackTrace}");
        }

        RefreshWorlds();
    }

    public void OnResize(Rectangle viewport)
    {
        _viewport = viewport;
        
        // Log viewport and panel sizing
        _log.Info($"SingleplayerScreen OnResize - Viewport: {viewport.Width}x{viewport.Height}");
        
        var panelW = Math.Min(1300, viewport.Width - 20); // Match options screen
        var panelH = Math.Min(700, viewport.Height - 30); // Match options screen
        _panelRect = new Rectangle(
            viewport.X + (viewport.Width - panelW) / 2,
            viewport.Y + (viewport.Height - panelH) / 2,
            panelW,
            panelH);
        
        _log.Info($"SingleplayerScreen - Panel: {_panelRect.Width}x{_panelRect.Height} at ({_panelRect.X},{_panelRect.Y})");

        var margin = 24; // Reduced from 32 to give more space
        var headerH = _font.LineHeight * 2 + 8; // Reduced from +12 to give more space
        var buttonAreaH = Math.Clamp((int)(panelH * 0.2f), 60, 90);
        
        // Calculate proper content area inside 9-patch borders
        const int borderSize = 16; // Must match the borderSize in DrawNinePatch
        var contentArea = new Rectangle(
            _panelRect.X + borderSize,
            _panelRect.Y + borderSize,
            _panelRect.Width - borderSize * 2,
            _panelRect.Height - borderSize * 2
        );
        
        _log.Info($"SingleplayerScreen - ContentArea: {contentArea.Width}x{contentArea.Height} at ({contentArea.X},{contentArea.Y})");
        
        _listRect = new Rectangle(
            contentArea.X + margin + 45, // Move right by 45 pixels (shrink more)
            contentArea.Y + headerH + 60, // Move down by 60 pixels (2 inches total)
            contentArea.Width - margin * 2 - 90, // Shrink width by 90 pixels total
            contentArea.Height - headerH - buttonAreaH - margin - 60); // Reduce height by 60 pixels
        
        _log.Info($"SingleplayerScreen - ListRect: {_listRect.Width}x{_listRect.Height} at ({_listRect.X},{_listRect.Y})");

        _rowHeight = _font.LineHeight + 4; // Reduced from +8 to make rows smaller

        var buttonW = Math.Clamp((int)(_panelRect.Width * 0.28f), 160, 260);
        var buttonH = Math.Clamp((int)(buttonW * 0.28f), 40, 70);
        var gap = 16;
        var available = _panelRect.Width - margin * 2;
        var deleteSize = buttonH;
        var totalW = buttonW * 2 + deleteSize + gap * 2;
        if (totalW > available)
        {
            var scale = available / (float)totalW;
            buttonW = Math.Max(120, (int)(buttonW * scale));
            buttonH = Math.Max(34, (int)(buttonH * scale));
            gap = Math.Max(8, (int)(gap * scale));
            deleteSize = buttonH;
            totalW = buttonW * 2 + deleteSize + gap * 2;
        }

        var buttonY = _panelRect.Bottom - margin - buttonH - 90; // Move up by 90 pixels (3 inches)
        var deleteY = buttonY - (deleteSize - buttonH);
        // Position back button in bottom-left corner of full screen with proper aspect ratio
        var backBtnMargin = 20;
        var backBtnBaseW = Math.Max(_backBtn.Texture?.Width ?? 0, 320);
        var backBtnBaseH = Math.Max(_backBtn.Texture?.Height ?? 0, (int)(backBtnBaseW * 0.28f));
        var backBtnScale = Math.Min(1f, Math.Min(240f / backBtnBaseW, 240f / backBtnBaseH)); // Match options screen
        var backBtnW = Math.Max(1, (int)Math.Round(backBtnBaseW * backBtnScale));
        var backBtnH = Math.Max(1, (int)Math.Round(backBtnBaseH * backBtnScale));
        _backBtn.Bounds = new Rectangle(
            viewport.X + backBtnMargin, 
            viewport.Bottom - backBtnMargin - backBtnH, 
            backBtnW, 
            backBtnH
        );
        
        // Center create button on the GUI backdrop
        var createBtnW = panelW / 3 - 15; // Match options screen apply button size
        var createBtnH = (int)(createBtnW * 0.25f); // Match options screen apply button ratio
        var createBtnX = _panelRect.X + (_panelRect.Width - createBtnW) / 2;
        _createBtn.Bounds = new Rectangle(createBtnX, buttonY, createBtnW, createBtnH);
        
        // Position delete button to the right of create button - make it square and match create button height
        deleteSize = createBtnH; // Make delete button square and same height as create button
        _deleteBtn.Bounds = new Rectangle(createBtnX + createBtnW + gap, buttonY, deleteSize, deleteSize);

        var infoW = 170;
        var infoH = Math.Max(30, _font.LineHeight + 12);
        _seedInfoBtn.Bounds = new Rectangle(_listRect.Right - infoW, _listRect.Y - infoH - 8, infoW, infoH);

        ClampScroll();
    }

    public void Update(GameTime gameTime, InputState input)
    {
        _now = gameTime.TotalGameTime.TotalSeconds;
        if (_statusMessage != null && _now > _statusUntil)
            _statusMessage = null;

        if (input.IsNewKeyPress(Keys.Escape))
        {
            _menus.Pop();
            return;
        }

        _createBtn.Update(input);
        _deleteBtn.Update(input);
        _backBtn.Update(input);
        _seedInfoBtn.Update(input);
        _overlayMousePos = input.MousePosition;
        HandleListInput(input);

        if (input.IsNewKeyPress(Keys.Enter))
            JoinSelectedWorld();
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

        // Log GUI asset info
        if (_panel != null)
        {
            DrawNinePatch(sb, _panel, _panelRect);
        }
        else
        {
            sb.Draw(_pixel, _panelRect, new Color(25, 25, 25));
        }

        // Account for GUI box borders (16px each side for 9-patch)
        const int borderSize = 16; // Must match the borderSize in DrawNinePatch

        var title = "WORLDS";
        var titleSize = _font.MeasureString(title);
        var titlePos = new Vector2(_panelRect.Center.X - titleSize.X / 2f, _panelRect.Y + 16 + borderSize);
        _font.DrawString(sb, title, titlePos, Color.White);

        DrawWorldList(sb);

        _createBtn.Draw(sb, _pixel, _font);
        _deleteBtn.Draw(sb, _pixel, _font);
        _backBtn.Draw(sb, _pixel, _font);
        _seedInfoBtn.Draw(sb, _pixel, _font);

        DrawWorldHoverTooltip(sb);

        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            var size = _font.MeasureString(_statusMessage);
            var pos = new Vector2(_panelRect.Center.X - size.X / 2f, _panelRect.Bottom - _font.LineHeight - 8);
            _font.DrawString(sb, _statusMessage, pos, Color.White);
        }

        sb.End();
    }

    private void DrawNinePatch(SpriteBatch sb, Texture2D texture, Rectangle destination)
    {
        if (texture == null) return;
        
        // Define the border sizes (adjust these based on your GUI texture)
        const int borderSize = 16;
        
        var source = new Rectangle(0, 0, texture.Width, texture.Height);
        
        // Create the 9 patches
        var patches = new[]
        {
            // Corners
            new Rectangle(source.X, source.Y, borderSize, borderSize), // top-left
            new Rectangle(source.X + borderSize, source.Y, source.Width - borderSize * 2, borderSize), // top-middle
            new Rectangle(source.Right - borderSize, source.Y, borderSize, borderSize), // top-right
            new Rectangle(source.X, source.Y + borderSize, borderSize, source.Height - borderSize * 2), // left-middle
            new Rectangle(source.X + borderSize, source.Y + borderSize, source.Width - borderSize * 2, source.Height - borderSize * 2), // center
            new Rectangle(source.Right - borderSize, source.Y + borderSize, borderSize, source.Height - borderSize * 2), // right-middle
            new Rectangle(source.X, source.Bottom - borderSize, borderSize, borderSize), // bottom-left
            new Rectangle(source.X + borderSize, source.Bottom - borderSize, source.Width - borderSize * 2, borderSize), // bottom-middle
            new Rectangle(source.Right - borderSize, source.Bottom - borderSize, borderSize, borderSize) // bottom-right
        };
        
        var destPatches = new[]
        {
            // Corners
            new Rectangle(destination.X, destination.Y, borderSize, borderSize), // top-left
            new Rectangle(destination.X + borderSize, destination.Y, destination.Width - borderSize * 2, borderSize), // top-middle
            new Rectangle(destination.Right - borderSize, destination.Y, borderSize, borderSize), // top-right
            new Rectangle(destination.X, destination.Y + borderSize, borderSize, destination.Height - borderSize * 2), // left-middle
            new Rectangle(destination.X + borderSize, destination.Y + borderSize, destination.Width - borderSize * 2, destination.Height - borderSize * 2), // center
            new Rectangle(destination.Right - borderSize, destination.Y + borderSize, borderSize, destination.Height - borderSize * 2), // right-middle
            new Rectangle(destination.X, destination.Bottom - borderSize, borderSize, borderSize), // bottom-left
            new Rectangle(destination.X + borderSize, destination.Bottom - borderSize, destination.Width - borderSize * 2, borderSize), // bottom-middle
            new Rectangle(destination.Right - borderSize, destination.Bottom - borderSize, borderSize, borderSize) // bottom-right
        };
        
        // Draw all patches
        for (int i = 0; i < 9; i++)
        {
            if (destPatches[i].Width > 0 && destPatches[i].Height > 0)
                sb.Draw(texture, destPatches[i], patches[i], Color.White);
        }
    }

    private void DrawWorldList(SpriteBatch sb)
    {
        _hoverWorldIndex = -1;
        if (_worlds.Count == 0)
        {
            var msg = "NO WORLDS FOUND";
            var size = _font.MeasureString(msg);
            var pos = new Vector2(_listRect.Center.X - size.X / 2f, _listRect.Center.Y - size.Y / 2f);
            _font.DrawString(sb, msg, pos, Color.White);
            return;
        }

        for (int i = 0; i < _worlds.Count; i++)
        {
            var y = _listRect.Y + i * _rowHeight - (int)Math.Round(_scroll);
            var rowRect = new Rectangle(_listRect.X, y, _listRect.Width, _rowHeight);
            if (rowRect.Bottom < _listRect.Y || rowRect.Y > _listRect.Bottom)
                continue;

            var bg = new Color(20, 20, 20, 180);
            if (i == _selectedIndex)
                bg = new Color(70, 70, 70, 220);
            sb.Draw(_pixel, rowRect, bg);
            DrawBorder(sb, rowRect, Color.White);

            var entry = _worlds[i];
            var modeText = entry.CurrentMode.ToString().ToUpperInvariant();
            var label = $"{entry.Name}  [{modeText}]";
            if (_showSeedInfo)
                label += $"  Seed:{ShortSeed(entry.Seed)}";
            var pos = new Vector2(rowRect.X + 10, rowRect.Y + (_rowHeight - _font.LineHeight) / 2f);
            _font.DrawString(sb, label, pos, Color.White);

            if (_showSeedInfo && rowRect.Contains(_overlayMousePos))
                _hoverWorldIndex = i;
        }
    }

    private void DrawWorldHoverTooltip(SpriteBatch sb)
    {
        if (!_showSeedInfo || _hoverWorldIndex < 0 || _hoverWorldIndex >= _worlds.Count)
            return;

        var entry = _worlds[_hoverWorldIndex];
        var tooltip = $"Seed: {entry.Seed} | Initial: {entry.InitialMode.ToString().ToUpperInvariant()}";
        var size = _font.MeasureString(tooltip);
        var rect = new Rectangle(
            Math.Clamp(_overlayMousePos.X + 14, _viewport.X + 8, _viewport.Right - (int)Math.Ceiling(size.X) - 24),
            Math.Clamp(_overlayMousePos.Y - _font.LineHeight - 16, _viewport.Y + 8, _viewport.Bottom - _font.LineHeight - 20),
            (int)Math.Ceiling(size.X) + 14,
            _font.LineHeight + 10);
        sb.Draw(_pixel, rect, new Color(0, 0, 0, 220));
        DrawBorder(sb, rect, new Color(200, 200, 200));
        _font.DrawString(sb, tooltip, new Vector2(rect.X + 7, rect.Y + 5), new Color(230, 230, 230));
    }

    private void HandleListInput(InputState input)
    {
        if (_listRect.Contains(input.MousePosition) && input.ScrollDelta != 0)
        {
            var step = Math.Sign(input.ScrollDelta) * ScrollStep;
            _scroll -= step;
            ClampScroll();
        }

        if (input.IsNewLeftClick() && _listRect.Contains(input.MousePosition))
        {
            var idx = (int)((input.MousePosition.Y - _listRect.Y + _scroll) / _rowHeight);
            if (idx >= 0 && idx < _worlds.Count)
            {
                if (idx == _lastClickIndex && (_now - _lastClickTime) <= DoubleClickSeconds)
                {
                    _selectedIndex = idx;
                    _lastClickIndex = -1;
                    _lastClickTime = 0;
                    JoinSelectedWorld();
                    return;
                }

                _selectedIndex = idx;
                _lastClickIndex = idx;
                _lastClickTime = _now;
            }
        }
    }

    private void RefreshWorlds()
    {
        _hoverWorldIndex = -1;
        try
        {
            Directory.CreateDirectory(Paths.WorldsDir);
            _worlds = Directory.GetDirectories(Paths.WorldsDir)
                .Select(path =>
                {
                    var name = Path.GetFileName(path) ?? string.Empty;
                    var metaPath = Path.Combine(path, "world.json");
                    var meta = File.Exists(metaPath) ? WorldMeta.Load(metaPath, _log) : null;
                    return new WorldListEntry
                    {
                        Name = name,
                        CurrentMode = meta?.CurrentWorldGameMode ?? meta?.GameMode ?? GameMode.Artificer,
                        InitialMode = meta?.InitialGameMode ?? meta?.GameMode ?? GameMode.Artificer,
                        Seed = meta?.Seed ?? 0
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to read worlds: {ex.Message}");
            _worlds = new List<WorldListEntry>();
        }

        if (_worlds.Count == 0)
        {
            _selectedIndex = -1;
            _scroll = 0;
        }
        else
        {
            _selectedIndex = Math.Clamp(_selectedIndex, 0, _worlds.Count - 1);
        }

        ClampScroll();
    }

    private void ClampScroll()
    {
        var contentHeight = _worlds.Count * _rowHeight;
        var max = Math.Max(0, contentHeight - _listRect.Height);
        if (_scroll < 0)
            _scroll = 0;
        else if (_scroll > max)
            _scroll = max;
    }

    private void OpenCreateWorld()
    {
        _log.Info("Opening Create World screen with new world generation system");
        _menus.Push(new CreateWorldScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, OnWorldCreated), _viewport);
    }

    private void OnWorldCreated(string worldName)
    {
        RefreshWorlds();
        if (!string.IsNullOrWhiteSpace(worldName))
        {
            var idx = _worlds.FindIndex(w => string.Equals(w.Name, worldName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _selectedIndex = idx;
        }
    }

    private void DeleteSelectedWorld()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _worlds.Count)
        {
            ShowStatus("SELECT A WORLD TO DELETE");
            return;
        }

        var name = _worlds[_selectedIndex].Name;
        var result = System.Windows.Forms.MessageBox.Show(
            $"Delete world?\n\n{name}",
            "Confirm Delete",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Warning);

        if (result != System.Windows.Forms.DialogResult.Yes)
            return;

        try
        {
            var path = Path.Combine(Paths.WorldsDir, name);
            if (Directory.Exists(path))
                Directory.Delete(path, true);

            _log.Info($"Deleted world: {name}");
            RefreshWorlds();
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to delete world {name}: {ex.Message}");
            ShowStatus("FAILED TO DELETE WORLD");
        }
    }

    private void ShowStatus(string message)
    {
        _statusMessage = message;
        _statusUntil = _now + 2.5;
    }

    private void JoinSelectedWorld()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _worlds.Count)
        {
            ShowStatus("SELECT A WORLD TO JOIN");
            return;
        }

        var name = _worlds[_selectedIndex].Name;
        var worldPath = Path.Combine(Paths.WorldsDir, name);
        var metaPath = Path.Combine(worldPath, "world.json");
        if (!Directory.Exists(worldPath) || !File.Exists(metaPath))
        {
            _log.Warn($"World data missing for '{name}'.");
            ShowStatus("WORLD DATA MISSING");
            return;
        }

        _menus.Push(new GameWorldScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, worldPath, metaPath), _viewport);
    }

    private void DrawBorder(SpriteBatch sb, Rectangle rect, Color color)
    {
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), color);
        sb.Draw(_pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), color);
    }

    private void ToggleSeedInfo()
    {
        _showSeedInfo = !_showSeedInfo;
        _settings.ShowSeedInfoInWorldList = _showSeedInfo;
        _settings.Save(_log);
        SyncSeedInfoLabel();
    }

    private void SyncSeedInfoLabel()
    {
        _seedInfoBtn.Label = $"SEED INFO: {(_showSeedInfo ? "ON" : "OFF")}";
    }

    private static string ShortSeed(int seed)
    {
        var value = seed.ToString();
        if (value.Length <= 8)
            return value;

        return $"{value.Substring(0, 4)}...{value.Substring(value.Length - 2)}";
    }

    private sealed class WorldListEntry
    {
        public string Name { get; init; } = string.Empty;
        public GameMode CurrentMode { get; init; } = GameMode.Artificer;
        public GameMode InitialMode { get; init; } = GameMode.Artificer;
        public int Seed { get; init; }
    }
}



