using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.Online.Eos;
using LatticeVeilMonoGame.Online.Gate;
using LatticeVeilMonoGame.Online.Lan;
using LatticeVeilMonoGame.UI;

namespace LatticeVeilMonoGame.UI.Screens;

public sealed class MultiplayerScreen : IScreen
{

    private readonly MenuStack _menus;
    private readonly AssetLoader _assets;
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Logger _log;

    private readonly PlayerProfile _profile;
    private readonly global::Microsoft.Xna.Framework.GraphicsDeviceManager _graphics;

    private readonly LanDiscovery _lanDiscovery;
    private EosClient? _eosClient;
    private readonly OnlineGateClient _onlineGate;
    private readonly OnlineSocialStateService _social;

    private readonly Button _refreshBtn;
    private readonly Button _addBtn;
    private readonly Button _hostBtn;
    private readonly Button _joinBtn;
	private readonly Button _yourIdBtn;
    private readonly Button _friendsBtn;
    private readonly Button _backBtn;

    private enum SessionType { Lan, Friend, Server }
    private sealed class SessionEntry
    {
        public SessionType Type;
        public string Title = "";
        public string HostName = "";
        public string Status = "";
        public string JoinTarget = "";
        public string WorldName = "";
        public string GameMode = "";
    }

    private List<SessionEntry> _sessions = new();
    private int _selectedIndex = -1;
    private Vector2 _lastMousePos = Vector2.Zero;

    private Dictionary<string, string> _pendingInvitesByHostPuid = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextInvitePollUtc = DateTime.MinValue;
    private DateTime _nextSessionRefreshUtc = DateTime.MinValue;

    private bool _joining;
    private string _status = "";

    private Texture2D? _bg;
    private Texture2D? _panel;

    private double _lastLanRefresh;
    private const double LanRefreshIntervalSeconds = 1.0;

    private Rectangle _viewport;
    private Rectangle _panelRect;
    private Rectangle _listRect;
    private Rectangle _listBodyRect;
    private Rectangle _buttonRowRect;
    private Rectangle _infoRect;
    private Rectangle _playerNameInputRect;

    private EosIdentityStore _identityStore;
    private bool _identitySyncBusy;
    private double _lastIdentitySyncAttempt = -100;
    private const double IdentitySyncIntervalSeconds = 8.0;
    private string _reservedUsername = string.Empty;

    private double _now;
    private const double DoubleClickSeconds = 0.35;
    private const string OnlineLoadingStatusPrefix = "Online loading:";
    private double _lastClickTime;
    private int _lastClickIndex = -1;

    public MultiplayerScreen(MenuStack menus, AssetLoader assets, PixelFont font, Texture2D pixel, Logger log, PlayerProfile profile,
        global::Microsoft.Xna.Framework.GraphicsDeviceManager graphics, EosClient? eosClient)
    {
        _menus = menus;
        _assets = assets;
        _font = font;
        _pixel = pixel;
        _log = log;
        _profile = profile;
        _graphics = graphics;

        _eosClient = eosClient ?? EosClientProvider.GetOrCreate(_log, "epic", allowRetry: true);
        if (_eosClient == null)
            _log.Warn("MultiplayerScreen: EOS client not available.");
        _onlineGate = OnlineGateClient.GetOrCreate();
        _social = OnlineSocialStateService.GetOrCreate(_log);
        _identityStore = EosIdentityStore.LoadOrCreate(_log);
        _reservedUsername = (_identityStore.ReservedUsername ?? string.Empty).Trim();

        _lanDiscovery = new LanDiscovery(_log);
        _lanDiscovery.StartListening();

        _refreshBtn = new Button("REFRESH", OnRefreshClicked) { BoldText = true };
        _addBtn = new Button("ADD", OnAddClicked) { BoldText = true };
        _hostBtn = new Button("HOST", OpenHostWorlds) { BoldText = true };
        _joinBtn = new Button("JOIN", OnJoinClicked) { BoldText = true };
		_yourIdBtn = new Button("SHARE ID", OnYourIdClicked) { BoldText = true };
        _friendsBtn = new Button("PROFILE", OnFriendsClicked) { BoldText = true };
        _backBtn = new Button("BACK", () => { Cleanup(); _menus.Pop(); }) { BoldText = true };

        try
        {
            _bg = _assets.LoadTexture("textures/menu/backgrounds/Multiplayer_bg.png");
            _panel = _assets.LoadTexture("textures/menu/GUIS/Multiplayer_GUI.png");
            _backBtn.Texture = _assets.LoadTexture("textures/menu/buttons/Back.png");
        }
        catch
        {
            // optional
        }

        RefreshSessions();
    }

    public void OnResize(Rectangle viewport)
    {
        _viewport = viewport;

        const int PanelMaxWidth = 1300;
        const int PanelMaxHeight = 700;
        
        var panelW = Math.Min(PanelMaxWidth, viewport.Width - 20); // Match SingleplayerScreen
        var panelH = Math.Min(PanelMaxHeight, viewport.Height - 30); // Match SingleplayerScreen
        var panelX = viewport.X + (viewport.Width - panelW) / 2;
        var panelY = viewport.Y + (viewport.Height - panelH) / 2;
        _panelRect = new Rectangle(panelX, panelY, panelW, panelH);

        // Apply scaling to all UI elements and center them
        const float UIScale = 0.80f; // Make UI 20% smaller (80% of original)
        
        // Calculate centered UI area within the GUI box (stretched slightly right)
        var scaledUIWidth = (int)(panelW * UIScale);
        var scaledUIHeight = (int)(panelH * UIScale);
        var uiOffsetX = panelX + (panelW - scaledUIWidth) / 2 + (panelW - scaledUIWidth) / 8; // Shift 1/8 to the right
        var uiOffsetY = panelY + (panelH - scaledUIHeight) / 2;
        
        var pad = (int)(24 * UIScale); // Scaled padding
        var headerH = (int)((_font.LineHeight + 12) * UIScale);
        _infoRect = new Rectangle(uiOffsetX + pad, uiOffsetY + pad, (int)((scaledUIWidth - pad * 2) * UIScale), headerH);

        var buttonRowH = (int)Math.Max(60, (_font.LineHeight * 2 + 18) * UIScale);
        _buttonRowRect = new Rectangle(uiOffsetX + pad, uiOffsetY + scaledUIHeight - pad - buttonRowH, (int)((scaledUIWidth - pad * 2) * UIScale), buttonRowH);
        _listRect = new Rectangle(uiOffsetX + pad, _infoRect.Bottom + (int)(15 * UIScale), (int)((scaledUIWidth - pad * 2) * UIScale), _buttonRowRect.Top - _infoRect.Bottom - (int)(25 * UIScale));

        var listHeaderH = (int)((_font.LineHeight + 20) * UIScale);
        _listBodyRect = new Rectangle(_listRect.X + (int)(8 * UIScale), _listRect.Y + listHeaderH, (int)((_listRect.Width - 16) * UIScale), Math.Max(0, _listRect.Height - listHeaderH - (int)(8 * UIScale)));
        _playerNameInputRect = new Rectangle(
            _listBodyRect.X + (int)(10 * UIScale),
            _listBodyRect.Y + (int)(_font.LineHeight * UIScale + 24 * UIScale),
            Math.Max(220, Math.Min(420, (int)(_listBodyRect.Width * UIScale - 210 * UIScale))),
            (int)((_font.LineHeight + 12) * UIScale));
        LayoutActionButtons();

        // Standard back button with proper aspect ratio positioned in bottom-left corner
        var backBtnMargin = 20;
        var backBtnBaseW = Math.Max(_backBtn.Texture?.Width ?? 0, 320);
        var backBtnBaseH = Math.Max(_backBtn.Texture?.Height ?? 0, (int)(backBtnBaseW * 0.28f));
        var backBtnScale = Math.Min(1f, Math.Min(240f / backBtnBaseW, 240f / backBtnBaseH)); // Match SettingsScreen
        var backBtnW = Math.Max(1, (int)Math.Round(backBtnBaseW * backBtnScale));
        var backBtnH = Math.Max(1, (int)Math.Round(backBtnBaseH * backBtnScale));
        _backBtn.Bounds = new Rectangle(viewport.X + backBtnMargin, viewport.Bottom - backBtnMargin - backBtnH, backBtnW, backBtnH);
    }

    public void Update(GameTime gameTime, InputState input)
    {
        _now = gameTime.TotalGameTime.TotalSeconds;

        if (input.IsNewKeyPress(Keys.Escape))
        {
            Cleanup();
            _menus.Pop();
            return;
        }

        HandleListInput(input);
        UpdateOnlineActionEnabledState();
        _refreshBtn.Update(input);
        _addBtn.Update(input);
        _hostBtn.Update(input);
        _joinBtn.Update(input);
        _friendsBtn.Update(input);
        _backBtn.Update(input);

        // Update mouse position for hover effects
        _lastMousePos = new Vector2(input.MousePosition.X, input.MousePosition.Y);

        if (DateTime.UtcNow >= _nextInvitePollUtc)
        {
            _nextInvitePollUtc = DateTime.UtcNow.AddSeconds(3);
            _ = PollInvitesAsync();
        }

        if (DateTime.UtcNow >= _nextSessionRefreshUtc)
        {
            _nextSessionRefreshUtc = DateTime.UtcNow.AddSeconds(2);
            RefreshSessions();
        }

        _social.Tick();
        UpdateLanDiscovery(gameTime);
        RefreshIdentityStateFromEos(EnsureEosClient());
        if (!_identitySyncBusy && _now - _lastIdentitySyncAttempt >= IdentitySyncIntervalSeconds)
        {
            _lastIdentitySyncAttempt = _now;
            _identitySyncBusy = true;
            _ = SyncIdentityStateAsync();
        }
    }

    private void UpdateOnlineActionEnabledState()
    {
        var eos = EnsureEosClient();
        var snapshot = EosRuntimeStatus.Evaluate(eos);
        var onlineAvailable = snapshot.Reason == EosRuntimeReason.Ready;

        _refreshBtn.Enabled = true;
        _hostBtn.Enabled = true;
        _joinBtn.Enabled = true;
        _yourIdBtn.Enabled = onlineAvailable;
        _friendsBtn.Enabled = onlineAvailable;
    }

    public void Draw(SpriteBatch sb, Rectangle viewport)
    {
        if (viewport != _viewport)
            OnResize(viewport);

        sb.Begin(samplerState: SamplerState.PointClamp);

        if (_bg is not null) sb.Draw(_bg, UiLayout.WindowViewport, Color.White);

        sb.End();

        sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiLayout.Transform);

        DrawPanel(sb);
        DrawInfo(sb);
        DrawList(sb);
        DrawButtons(sb);
        DrawStatus(sb);
        _backBtn.Draw(sb, _pixel, _font);

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

    private void DrawPanel(SpriteBatch sb)
    {
        if (_panel is not null)
            DrawNinePatch(sb, _panel, _panelRect);
        else
            sb.Draw(_pixel, _panelRect, new Color(0, 0, 0, 180));
    }

    private void DrawInfo(SpriteBatch sb)
    {
        // Draw username centered in the info area
        var username = _profile?.Username ?? "Unknown";
        var usernameSize = _font.MeasureString(username);
        var usernamePos = new Vector2(_infoRect.Center.X - usernameSize.X / 2f, _infoRect.Center.Y - usernameSize.Y / 2f);
        _font.DrawString(sb, username, usernamePos, new Color(220, 180, 80));
    }

    private void DrawList(SpriteBatch sb)
    {
        sb.Draw(_pixel, _listRect, new Color(20, 20, 20, 200));
        DrawBorder(sb, _listRect, Color.White);

        if (_sessions.Count == 0)
        {
            var msg = "No games found on LAN or with Friends.";
            var size = _font.MeasureString(msg);
            _font.DrawString(sb, msg, new Vector2(_listRect.Center.X - size.X / 2, _listRect.Center.Y - size.Y / 2), Color.Gray);
            return;
        }

        var rowH = (int)(64 * 0.80f);
        var rowY = _listBodyRect.Y + 2;
        for (var i = 0; i < _sessions.Count; i++)
        {
            var s = _sessions[i];
            var rowRect = new Rectangle(_listBodyRect.X, rowY - 1, _listBodyRect.Width, rowH);
            
            if (i == _selectedIndex)
            {
                sb.Draw(_pixel, rowRect, new Color(100, 100, 100, 200));
                DrawBorder(sb, rowRect, Color.Yellow); // Bright border for selection
            }
            else if (rowRect.Contains(_lastMousePos))
            {
                sb.Draw(_pixel, rowRect, new Color(40, 40, 40, 150));
            }

            var iconSize = (int)(48 * 0.80f);
            var iconRect = new Rectangle(rowRect.X + 8, rowRect.Y + (rowRect.Height - iconSize) / 2, iconSize, iconSize);
            
            // Draw Type Icon (Color coded for now)
            var iconColor = s.Type switch
            {
                SessionType.Lan => Color.Cyan,
                SessionType.Friend => Color.LimeGreen,
                SessionType.Server => Color.Orange,
                _ => Color.White
            };
            sb.Draw(_pixel, iconRect, iconColor * 0.5f);
            DrawBorder(sb, iconRect, iconColor);

            var textX = iconRect.Right + 12;
            var textY = rowRect.Y + 4;

            var title = s.Title;
            if (s.Type == SessionType.Friend)
            {
                var gmDisplay = string.IsNullOrWhiteSpace(s.GameMode) ? "" : $" > {s.GameMode}";
                var statusSuffix = "";
                if (_pendingInvitesByHostPuid.TryGetValue(s.JoinTarget, out var inviteStatus))
                {
                    statusSuffix = inviteStatus switch
                    {
                        "accepted" => " [CAN JOIN]",
                        "rejected" => " [REJECTED]",
                        _ => " [PENDING]"
                    };
                }
                title = $"{s.HostName} > {s.WorldName}{gmDisplay}{statusSuffix}";
            }
            else if (s.Type == SessionType.Lan)
            {
                var gmDisplay = string.IsNullOrWhiteSpace(s.GameMode) ? "" : $" > {s.GameMode}";
                title = $"LAN: {s.Title}{gmDisplay}";
            }

            _font.DrawString(sb, title, new Vector2(textX, textY), Color.White);
            textY += _font.LineHeight;
            _font.DrawString(sb, $"Host: {s.HostName} | {s.Status}", new Vector2(textX, textY), Color.LightGray);

            rowY += rowH + 4;
            if (rowY > _listBodyRect.Bottom - rowH)
                break;
        }
    }

    
    private void DrawButtons(SpriteBatch sb)
    {
        var joinText = "JOIN";
        if (_selectedIndex >= 0 && _selectedIndex < _sessions.Count)
        {
            var s = _sessions[_selectedIndex];
            if (s.Type == SessionType.Friend && _pendingInvitesByHostPuid.TryGetValue(s.JoinTarget, out var status))
            {
                if (status == "pending") joinText = "PENDING";
                else if (status == "accepted") joinText = "CAN JOIN";
            }
        }
        _joinBtn.Label = joinText;

        _refreshBtn.Draw(sb, _pixel, _font);
        _addBtn.Draw(sb, _pixel, _font);
        _hostBtn.Draw(sb, _pixel, _font);
        _joinBtn.Draw(sb, _pixel, _font);
        _friendsBtn.Draw(sb, _pixel, _font);
    }

    private void DrawStatus(SpriteBatch sb)
    {
        if (string.IsNullOrWhiteSpace(_status))
            return;

        var pos = new Vector2(_panelRect.X + 14, _panelRect.Bottom - _font.LineHeight - 8);
        _font.DrawString(sb, _status, pos, Color.White);
    }

    private void OnRefreshClicked()
    {
        _social.ForceRefresh();
        RefreshSessions();
        _status = "";
    }

    private void OnAddClicked()
    {
        _status = "Direct IP join not implemented yet.";
    }

    private void OnJoinClicked()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _sessions.Count)
        {
            _status = "Select a game to join.";
            return;
        }

        if (_joining) return;

        var entry = _sessions[_selectedIndex];
        _joining = true;

        if (entry.Type == SessionType.Lan)
        {
            var parts = entry.JoinTarget.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 ? int.Parse(parts[1]) : 27015;
            _status = $"Connecting to LAN {host}:{port}...";
            _ = JoinLanAsync(host, port);
        }
        else if (entry.Type == SessionType.Friend)
        {
            if (_pendingInvitesByHostPuid.TryGetValue(entry.JoinTarget, out var status))
            {
                if (status == "accepted")
                {
                    _status = $"Connecting to {entry.HostName} via P2P...";
                    _joining = true;
                    _ = JoinP2PAsync(entry.JoinTarget, entry.HostName);
                    return;
                }
                else if (status == "rejected")
                {
                    _status = "Invite was rejected.";
                    _joining = false;
                }
                else
                {
                    _status = "Invite is still pending.";
                    _joining = false;
                }
            }
            else
            {
                _status = $"Sending invite to {entry.HostName}...";
                _joining = true;
                _ = SendInviteAsync(entry.JoinTarget, entry.WorldName);
            }
        }
        else
        {
            _status = "Joining dedicated servers not implemented yet.";
            _joining = false;
        }
    }

    private void OnYourIdClicked()
    {
        var eos = EnsureEosClient();
        var snapshot = EosRuntimeStatus.Evaluate(eos);
        if (snapshot.Reason is EosRuntimeReason.SdkNotCompiled or EosRuntimeReason.ConfigMissing or EosRuntimeReason.DisabledByEnvironment or EosRuntimeReason.ClientUnavailable)
        {
            _status = "Online services not available.";
            return;
        }

        var reserved = _identityStore.ReservedUsername ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reserved))
        {
            _status = "You must claim a username in the launcher first.";
            return;
        }

        var targetUsername = _profile.GetDisplayUsername();
        if (string.IsNullOrWhiteSpace(targetUsername))
        {
            _status = "You must set a username in the launcher first.";
            return;
        }

        var normalized = EosIdentityStore.NormalizeDisplayName(targetUsername);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            _status = "Invalid username format.";
            return;
        }

        if (normalized != reserved)
        {
            _status = $"Your username ({targetUsername}) doesn't match your reserved name ({reserved}).";
            return;
        }

        _status = $"Your ID: {normalized}";
    }

    private void OnFriendsClicked()
    {
        if (!_onlineGate.CanUseOfficialOnline(_log, out var gateDenied))
        {
            _status = gateDenied;
            return;
        }

        _menus.Push(
            new ProfileScreen(
                _menus,
                _assets,
                _font,
                _pixel,
                _log,
                _profile,
                _graphics,
                EnsureEosClient()),
            _viewport);
    }

    private void OpenHostWorlds()
    {
        var eos = EnsureEosClient();
        var snapshot = EosRuntimeStatus.Evaluate(eos);
        var hostOnline = snapshot.Reason == EosRuntimeReason.Ready;
        
        if (hostOnline && !_onlineGate.CanUseOfficialOnline(_log, out var gateDenied))
        {
            _status = gateDenied;
            return;
        }

        _menus.Push(new MultiplayerHostScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, _eosClient, hostOnline), _viewport);
    }

    private void LayoutActionButtons()
    {
        const float UIScale = 0.80f; // Make UI 20% smaller (80% of original)
        const int gap = (int)(12 * UIScale);
        var rowH = (int)Math.Max(36, (_font.LineHeight + 16) * UIScale);
        var rowY = _buttonRowRect.Y + Math.Max(0, (_buttonRowRect.Height - rowH) / 2);

        _hostBtn.Visible = true;
        _joinBtn.Visible = true;
        _friendsBtn.Visible = true;
        _refreshBtn.Visible = true;
        _addBtn.Visible = true;
        _yourIdBtn.Visible = false; // Hide YOUR ID button

        var eos = EnsureEosClient();
        var snapshot = EosRuntimeStatus.Evaluate(eos);
        var onlineAvailable = snapshot.Reason == EosRuntimeReason.Ready;

        _friendsBtn.Enabled = onlineAvailable;

        // Layout 5 buttons in one row (without YOUR ID)
        var buttonCount = 5;
        var minButtonW = (int)(100 * UIScale);
        var canFitOneRow = _buttonRowRect.Width >= (minButtonW * buttonCount + gap * (buttonCount - 1));
        
        if (canFitOneRow)
        {
            var buttonW = (int)((_buttonRowRect.Width - gap * (buttonCount - 1)) / buttonCount);
            var x = _buttonRowRect.X;
            
            _refreshBtn.Bounds = new Rectangle(x, rowY, buttonW, rowH);
            x += buttonW + gap;

            _addBtn.Bounds = new Rectangle(x, rowY, buttonW, rowH);
            x += buttonW + gap;
            
            _hostBtn.Bounds = new Rectangle(x, rowY, buttonW, rowH);
            x += buttonW + gap;
            
            _joinBtn.Bounds = new Rectangle(x, rowY, buttonW, rowH);
            x += buttonW + gap;
            
            _friendsBtn.Bounds = new Rectangle(x, rowY, buttonW, rowH);
        }
        else
        {
            // Two rows if needed
            var twoRowH = (int)Math.Max(28, ((_buttonRowRect.Height - gap) / 2) * UIScale);
            var topY = _buttonRowRect.Y;
            var bottomY = topY + twoRowH + gap;
            var leftX = _buttonRowRect.X;

            // Top row: REFRESH, ADD, HOST
            var topButtonW = (int)((_buttonRowRect.Width - gap * 2) / 3);
            _refreshBtn.Bounds = new Rectangle(leftX, topY, topButtonW, twoRowH);
            _addBtn.Bounds = new Rectangle(leftX + topButtonW + gap, topY, topButtonW, twoRowH);
            _hostBtn.Bounds = new Rectangle(leftX + (topButtonW + gap) * 2, topY, topButtonW, twoRowH);

            // Bottom row: JOIN, FRIENDS
            var bottomButtonW = (int)((_buttonRowRect.Width - gap) / 2);
            _joinBtn.Bounds = new Rectangle(leftX, bottomY, bottomButtonW, twoRowH);
            _friendsBtn.Bounds = new Rectangle(leftX + bottomButtonW + gap, bottomY, bottomButtonW, twoRowH);
        }
    }

    private void HandleListInput(InputState input)
    {
        if (!input.IsNewLeftClick())
            return;

        var p = input.MousePosition;
        if (!_listBodyRect.Contains(p))
            return;

        var rowH = (int)(64 * 0.80f) + 4;
        var idx = (int)((p.Y - _listBodyRect.Y) / rowH);
        if (idx >= 0 && idx < _sessions.Count)
        {
            _selectedIndex = idx;

            // Double-click to join
            var dt = _now - _lastClickTime;
            var isDouble = idx == _lastClickIndex && dt >= 0 && dt < DoubleClickSeconds;
            _lastClickIndex = idx;
            _lastClickTime = _now;

            if (isDouble)
                OnJoinClicked();
        }
    }

    private async Task SendInviteAsync(string targetPuid, string worldName)
    {
        var ok = await _onlineGate.SendWorldInviteAsync(targetPuid, worldName);
        if (ok)
        {
            _status = "Invite sent! Waiting for response...";
            _pendingInvitesByHostPuid[targetPuid] = "pending";
            RefreshSessions();
        }
        else
        {
            _status = "Failed to send invite.";
        }
        _joining = false;
    }

    private async Task JoinP2PAsync(string hostPuid, string hostName)
    {
        var eos = EnsureEosClient();
        if (eos == null) { _status = "EOS not ready."; _joining = false; return; }

        var result = await EosP2PClientSession.ConnectAsync(_log, eos, hostPuid, _profile.GetDisplayUsername(), TimeSpan.FromSeconds(10));
        if (!result.Success || result.Session == null)
        {
            _status = $"P2P Failed: {result.Error}";
            _joining = false;
            return;
        }

        var info = result.WorldInfo;
        var worldDir = JoinedWorldCache.PrepareJoinedWorldPath(info, _log);
        var metaPath = Path.Combine(worldDir, "world.json");
        var meta = WorldMeta.CreateFlat(info.WorldName, info.GameMode, info.Width, info.Height, info.Depth, info.Seed);
        meta.PlayerCollision = info.PlayerCollision;
        meta.WorldId = JoinedWorldCache.ResolveWorldId(info);
        meta.Save(metaPath, _log);

        _status = $"Connected to {hostName}!";
        _joining = false;

        _menus.Push(new GameWorldScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, worldDir, metaPath, result.Session), _viewport);
    }

    private Task SyncIdentityStateAsync()
    {
        return Task.CompletedTask;
    }

    private void RefreshIdentityStateFromEos(EosClient? eos)
    {
        // This would refresh identity state from EOS services
        // Implementation would go here
    }

    private EosClient? EnsureEosClient()
    {
        if (_eosClient != null)
            return _eosClient;

        _eosClient = EosClientProvider.GetOrCreate(_log, "deviceid", allowRetry: true);
        if (_eosClient == null)
            _log.Warn("MultiplayerScreen: EOS client not available.");
        return _eosClient;
    }

    private void RefreshIdentityState(EosClient? eos)
    {
        if (eos == null || string.IsNullOrWhiteSpace(eos.LocalProductUserId))
            return;

        var normalizedName = EosIdentityStore.NormalizeDisplayName(_profile.GetDisplayUsername());
        if (string.IsNullOrWhiteSpace(normalizedName))
            normalizedName = "Player";

        if (!string.Equals(_identityStore.ProductUserId, eos.LocalProductUserId, StringComparison.Ordinal))
        {
            _identityStore.ProductUserId = eos.LocalProductUserId;
            _identityStore.DisplayName = normalizedName;
            _identityStore.Save(_log);
            return;
        }

        if (!string.Equals(_identityStore.DisplayName, normalizedName, StringComparison.Ordinal))
        {
            _identityStore.DisplayName = normalizedName;
            _identityStore.Save(_log);
        }
    }

    private async Task SyncIdentityWithGateAsync()
    {
        if (_identitySyncBusy)
            return;

        _lastIdentitySyncAttempt = _now;
        var eos = EnsureEosClient();
        var snapshot = EosRuntimeStatus.Evaluate(eos);
        if (snapshot.Reason != EosRuntimeReason.Ready || eos == null)
            return;

        var productUserId = (eos.LocalProductUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(productUserId))
            return;

        if (!_onlineGate.CanUseOfficialOnline(_log, out _))
            return;

        _identitySyncBusy = true;
        try
        {
            var me = await _onlineGate.GetMyIdentityAsync();
            if (me.Ok && me.Found && me.User != null)
            {
                _reservedUsername = me.User.Username;
                if (!string.Equals(_identityStore.ReservedUsername, me.User.Username, StringComparison.Ordinal))
                {
                    _identityStore.ReservedUsername = me.User.Username;
                    _identityStore.Save(_log);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Identity sync failed: {ex.Message}");
        }
        finally
        {
            _identitySyncBusy = false;
        }
    }

    private async Task PollInvitesAsync()
    {
        var result = await _onlineGate.GetMyWorldInvitesAsync();
        if (result.Ok)
        {
            var serverInvites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var invite in result.Outgoing)
            {
                serverInvites[invite.TargetProductUserId] = invite.Status;
            }

            // Sync server state to local, but keep local "pending" if it's missing from server (just sent)
            foreach (var kvp in serverInvites)
            {
                _pendingInvitesByHostPuid[kvp.Key] = kvp.Value;
            }

            // If an invite is in our local list but NOT on the server, and it's NOT "pending", clear it.
            // If it IS "pending" locally but missing on server, we assume it's still being processed by backend.
            var toRemove = new List<string>();
            foreach (var localPuid in _pendingInvitesByHostPuid.Keys)
            {
                if (!serverInvites.ContainsKey(localPuid) && _pendingInvitesByHostPuid[localPuid] != "pending")
                {
                    toRemove.Add(localPuid);
                }
            }
            foreach (var puid in toRemove) _pendingInvitesByHostPuid.Remove(puid);

            RefreshSessions();
        }
    }

    private void RefreshSessions()
    {
        var newSessions = new List<SessionEntry>();

        // 1. LAN
        var lan = _lanDiscovery.GetServers();
        foreach (var s in lan)
        {
            newSessions.Add(new SessionEntry
            {
                Type = SessionType.Lan,
                Title = s.ServerName,
                HostName = "Local Network",
                Status = "Online",
                JoinTarget = DescribeEndpoint(s),
                WorldName = "LAN World",
                GameMode = s.GameMode
            });
        }

        // 2. Friends
        var social = _social.GetSnapshot();
        foreach (var friend in social.Friends)
        {
            if (social.PresenceByUserId.TryGetValue(friend.ProductUserId, out var presence) && presence.IsHosting)
            {
                newSessions.Add(new SessionEntry
                {
                    Type = SessionType.Friend,
                    Title = string.IsNullOrWhiteSpace(presence.WorldName) ? $"{friend.DisplayName}'s World" : presence.WorldName,
                    HostName = friend.DisplayName,
                    Status = presence.Status,
                    JoinTarget = presence.JoinTarget,
                    WorldName = presence.WorldName,
                    GameMode = presence.GameMode
                });
            }
        }

        // 3. Servers (Placeholder)
        // (Empty for now)

        _sessions = newSessions.OrderByDescending(s => s.Type == SessionType.Friend && _pendingInvitesByHostPuid.TryGetValue(s.JoinTarget, out var status) && status == "accepted")
                               .ThenBy(s => s.Type)
                               .ToList();

        // Clean up pending invites for hosts that are no longer in the list
        var activeHostPuids = new HashSet<string>(newSessions.Where(s => s.Type == SessionType.Friend).Select(s => s.JoinTarget), StringComparer.OrdinalIgnoreCase);
        var toRemove = _pendingInvitesByHostPuid.Keys.Where(puid => !activeHostPuids.Contains(puid)).ToList();
        foreach (var puid in toRemove)
        {
            _pendingInvitesByHostPuid.Remove(puid);
        }

        if (_selectedIndex >= _sessions.Count)
            _selectedIndex = _sessions.Count - 1;
        if (_selectedIndex < 0 && _sessions.Count > 0)
            _selectedIndex = 0;
    }

    private void UpdateLanDiscovery(GameTime gameTime)
    {
        var now = gameTime.TotalGameTime.TotalSeconds;
        if (now - _lastLanRefresh < LanRefreshIntervalSeconds)
            return;

        _lastLanRefresh = now;
        RefreshSessions();
    }

    private async Task JoinLanAsync(string host, int port)
    {
        var result = await LanClientSession.ConnectAsync(_log, host, port, _profile.GetDisplayUsername(), TimeSpan.FromSeconds(4));
        if (!result.Success || result.Session == null)
        {
            _status = $"Connect failed: {result.Error}";
            _log.Warn($"LAN join failed: {result.Error}");
            _joining = false;
            return;
        }

        var info = result.WorldInfo;
        var worldDir = JoinedWorldCache.PrepareJoinedWorldPath(info, _log);
        var metaPath = Path.Combine(worldDir, "world.json");
        var meta = WorldMeta.CreateFlat(info.WorldName, info.GameMode, info.Width, info.Height, info.Depth, info.Seed);
        meta.PlayerCollision = info.PlayerCollision;
        meta.WorldId = JoinedWorldCache.ResolveWorldId(info);
        meta.Save(metaPath, _log);

        _status = $"Connected to {host}.";
        _joining = false;

        // GO DIRECTLY TO GAME WORLD - NO GENERATION SCREEN
        _menus.Push(
            new GameWorldScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, worldDir, metaPath, result.Session),
            _viewport);
    }

    private void Cleanup()
    {
        _lanDiscovery.StopListening();
    }

    private static string DescribeEndpoint(LanServerEntry entry)
    {
        if (entry.Endpoint == null)
            return "";
        return $"{entry.Endpoint.Address}:{entry.ServerPort}";
    }

    private void DrawTextBold(SpriteBatch sb, string text, Vector2 pos, Color color)
    {
        // Cheap bold effect (shadow)
        _font.DrawString(sb, text, pos + new Vector2(1, 1), Color.Black);
        _font.DrawString(sb, text, pos, color);
    }

    private void DrawBorder(SpriteBatch sb, Rectangle r, Color color)
    {
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), color);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, 2, r.Height), color);
        sb.Draw(_pixel, new Rectangle(r.Right - 2, r.Y, 2, r.Height), color);
    }
}
