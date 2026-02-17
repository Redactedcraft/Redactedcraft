using System;
using System.Threading.Tasks;
using WinClipboard = System.Windows.Forms.Clipboard;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.Online.Eos;
using LatticeVeilMonoGame.Online.Gate;
using LatticeVeilMonoGame.UI;

namespace LatticeVeilMonoGame.UI.Screens;

public sealed class ProfileScreen : IScreen
{
public enum ProfileScreenStartTab
    {
        Identity,
        Friends
    }

    public enum ProfileScreenFriendsMode
    {
        Friends,
        Requests,
        Blocked
    }

    private readonly MenuStack _menus;
    private readonly AssetLoader _assets;
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Logger _log;
    private readonly PlayerProfile _profile;
    private readonly global::Microsoft.Xna.Framework.GraphicsDeviceManager _graphics;
    private readonly EosClient? _eos;
    private readonly EosIdentityStore _identityStore;
    private readonly OnlineGateClient _gate;

    private readonly Button _tabIdentityBtn;
    private readonly Button _tabFriendsBtn;
    private readonly Button _friendsModeFriendsBtn;
    private readonly Button _friendsModeRequestsBtn;
    private readonly Button _friendsModeBlockedBtn;
    private readonly Button _addFriendBtn;
    private readonly Button _removeFriendBtn;
    private readonly Button _acceptRequestBtn;
    private readonly Button _denyRequestBtn;
    private readonly Button _blockUserBtn;
    private readonly Button _unblockUserBtn;
    private readonly Button _copyIdBtn;
    private readonly Button _backBtn;

    private Texture2D? _bg;
    private Texture2D? _panel;

    private Rectangle _viewport;
    private Rectangle _panelRect;
    private Rectangle _tabIdentityRect;
    private Rectangle _tabFriendsRect;
    private Rectangle _usernameRect;
    private Rectangle _iconRect;
    private Rectangle _friendsModeFriendsRect;
    private Rectangle _friendsModeRequestsRect;
    private Rectangle _friendsModeBlockedRect;
    private Rectangle _friendsRect;

    private ProfileScreenStartTab _activeTab = ProfileScreenStartTab.Identity;
    private ProfileScreenFriendsMode _friendsListMode = ProfileScreenFriendsMode.Friends;
    private int _selectedFriend = -1;
    private int _selectedRequest = -1;
    private int _selectedBlocked = -1;
    private bool _friendsSyncInProgress;
    private bool _friendsSeedAttempted;
    private bool _friendsEndpointUnavailable;
    private DateTime _nextFriendsSyncUtc = DateTime.MinValue;
    private readonly List<GateFriendRequest> _incomingRequests = new();
    private readonly List<GateFriendRequest> _outgoingRequests = new();
    private readonly List<GateIdentityUser> _blockedUsers = new();

    private string _status = string.Empty;
    private DateTime _statusExpiryUtc = DateTime.MinValue;

    public ProfileScreen(MenuStack menus, AssetLoader assets, PixelFont font, Texture2D pixel, Logger log, PlayerProfile profile,
        global::Microsoft.Xna.Framework.GraphicsDeviceManager graphics, EosClient? eosClient,
        ProfileScreenStartTab startTab = ProfileScreenStartTab.Identity,
        ProfileScreenFriendsMode startFriendsMode = ProfileScreenFriendsMode.Friends)
    {
        _menus = menus;
        _assets = assets;
        _font = font;
        _pixel = pixel;
        _log = log;
        _profile = profile;
        _graphics = graphics;
        _eos = eosClient;
        _identityStore = EosIdentityStore.LoadOrCreate(_log);
        _gate = OnlineGateClient.GetOrCreate();

        _activeTab = startTab;
        _friendsListMode = startFriendsMode;

        _tabIdentityBtn = new Button("PROFILE", () => _activeTab = ProfileScreenStartTab.Identity) { BoldText = true };
        _tabFriendsBtn = new Button("FRIENDS", () => _activeTab = ProfileScreenStartTab.Friends) { BoldText = true };
        _friendsModeFriendsBtn = new Button("FRIENDS", () => _friendsListMode = ProfileScreenFriendsMode.Friends) { BoldText = true };
        _friendsModeRequestsBtn = new Button("REQUESTS", () => _friendsListMode = ProfileScreenFriendsMode.Requests) { BoldText = true };
        _friendsModeBlockedBtn = new Button("BLOCKED", () => _friendsListMode = ProfileScreenFriendsMode.Blocked) { BoldText = true };
        _addFriendBtn = new Button("ADD FRIEND", OpenAddFriend) { BoldText = true };
        _removeFriendBtn = new Button("REMOVE FRIEND", () => _ = RemoveFriendAsync()) { BoldText = true };
        _acceptRequestBtn = new Button("ACCEPT", () => _ = AcceptRequestAsync()) { BoldText = true };
        _denyRequestBtn = new Button("DENY", () => _ = DenyRequestAsync()) { BoldText = true };
        _blockUserBtn = new Button("BLOCK", () => _ = BlockSelectedUserAsync()) { BoldText = true };
        _unblockUserBtn = new Button("UNBLOCK", () => _ = UnblockSelectedUserAsync()) { BoldText = true };
        _copyIdBtn = new Button("COPY MY ID", CopyLocalId) { BoldText = true };
        _backBtn = new Button("BACK", () => _menus.Pop()) { BoldText = true };

        try
        {
            _bg = _assets.LoadTexture("textures/menu/backgrounds/Profile_bg.png");
            _panel = _assets.LoadTexture("textures/menu/GUIS/Profile_GUI.png");
            _backBtn.Texture = _assets.LoadTexture("textures/menu/buttons/Back.png");
        }
        catch
        {
            // optional
        }
    }

    public void OnResize(Rectangle viewport)
    {
        _viewport = viewport;

        var panelW = Math.Min(1300, viewport.Width - 20);
        var panelH = Math.Min(700, viewport.Height - 30);
        var panelX = viewport.X + (viewport.Width - panelW) / 2;
        var panelY = viewport.Y + (viewport.Height - panelH) / 2;
        _panelRect = new Rectangle(panelX, panelY, panelW, panelH);

        // 1 inch inset (approx 96 pixels)
        var margin = 96;
        var contentRect = new Rectangle(_panelRect.X + margin, _panelRect.Y + margin, _panelRect.Width - margin * 2, _panelRect.Height - margin * 2);

        var tabY = contentRect.Y;
        var tabW = 200; 
        var tabH = _font.LineHeight + 12;
        _tabIdentityRect = new Rectangle(contentRect.X, tabY, tabW, tabH);
        _tabFriendsRect = new Rectangle(_tabIdentityRect.Right + 8, tabY, tabW, tabH);
        _tabIdentityBtn.Bounds = _tabIdentityRect;
        _tabFriendsBtn.Bounds = _tabFriendsRect;

        _usernameRect = new Rectangle(contentRect.X, _tabIdentityRect.Bottom + 16, contentRect.Width, _font.LineHeight + 18);
        _iconRect = new Rectangle(contentRect.Right - 98, _usernameRect.Bottom + 12, 98, 98);
        
        var modeY = _tabIdentityRect.Bottom + 12;
        var modeH = _font.LineHeight + 10;
        var modeGap = 8;
        var modeW = 150; 
        _friendsModeFriendsRect = new Rectangle(contentRect.X, modeY, modeW, modeH);
        _friendsModeRequestsRect = new Rectangle(_friendsModeFriendsRect.Right + modeGap, modeY, modeW, modeH);
        _friendsModeBlockedRect = new Rectangle(_friendsModeRequestsRect.Right + modeGap, modeY, modeW, modeH);
        _friendsModeFriendsBtn.Bounds = _friendsModeFriendsRect;
        _friendsModeRequestsBtn.Bounds = _friendsModeRequestsRect;
        _friendsModeBlockedBtn.Bounds = _friendsModeBlockedRect;

        var friendsY = _friendsModeFriendsRect.Bottom + 12;
        var actionAreaH = 60;
        _friendsRect = new Rectangle(
            contentRect.X,
            friendsY,
            contentRect.Width,
            contentRect.Bottom - friendsY - actionAreaH - 12);

        var buttonY = contentRect.Bottom - actionAreaH;
        var gap = 8;
        var buttonH = Math.Max(44, _font.LineHeight * 2);
        
        // Identity view actions
        _copyIdBtn.Bounds = new Rectangle(contentRect.X, buttonY, contentRect.Width, buttonH);
        
        // Friends view actions
        var friendsButtonW = (contentRect.Width - gap * 2) / 3;
        _addFriendBtn.Bounds = new Rectangle(contentRect.X, buttonY, friendsButtonW, buttonH);
        _removeFriendBtn.Bounds = new Rectangle(_addFriendBtn.Bounds.Right + gap, buttonY, friendsButtonW, buttonH);
        _blockUserBtn.Bounds = new Rectangle(_removeFriendBtn.Bounds.Right + gap, buttonY, friendsButtonW, buttonH);
        
        _acceptRequestBtn.Bounds = _addFriendBtn.Bounds;
        _denyRequestBtn.Bounds = _removeFriendBtn.Bounds;
        _unblockUserBtn.Bounds = _addFriendBtn.Bounds;

        // Back button matches SingleplayerScreen position (outside the main panel, bottom-left of viewport)
        var backBtnMargin = 20;
        var backBtnBaseW = Math.Max(_backBtn.Texture?.Width ?? 0, 320);
        var backBtnBaseH = Math.Max(_backBtn.Texture?.Height ?? 0, (int)(backBtnBaseW * 0.28f));
        var backBtnScale = Math.Min(1f, Math.Min(240f / backBtnBaseW, 240f / backBtnBaseH));
        var backBtnW = Math.Max(1, (int)Math.Round(backBtnBaseW * backBtnScale));
        var backBtnH = Math.Max(1, (int)Math.Round(backBtnBaseH * backBtnScale));
        _backBtn.Bounds = new Rectangle(viewport.X + backBtnMargin, viewport.Bottom - backBtnMargin - backBtnH, backBtnW, backBtnH);
    }

    public void Update(GameTime gameTime, InputState input)
    {
        if (_statusExpiryUtc != DateTime.MinValue && DateTime.UtcNow >= _statusExpiryUtc)
        {
            _status = string.Empty;
            _statusExpiryUtc = DateTime.MinValue;
        }

        if (input.IsNewKeyPress(Keys.Escape))
        {
            _menus.Pop();
            return;
        }

        _tabIdentityBtn.Update(input);
        _tabFriendsBtn.Update(input);
        _backBtn.Update(input);

        if (_activeTab == ProfileScreenStartTab.Identity)
        {
            _copyIdBtn.Update(input);
        }
        else
        {
            _friendsModeFriendsBtn.Update(input);
            _friendsModeRequestsBtn.Update(input);
            _friendsModeBlockedBtn.Update(input);

            _addFriendBtn.Enabled = _friendsListMode == ProfileScreenFriendsMode.Friends;
            _removeFriendBtn.Enabled = _friendsListMode == ProfileScreenFriendsMode.Friends
                && _selectedFriend >= 0
                && _selectedFriend < _profile.Friends.Count;
            _blockUserBtn.Enabled = (_friendsListMode == ProfileScreenFriendsMode.Friends
                    && _selectedFriend >= 0
                    && _selectedFriend < _profile.Friends.Count)
                || (_friendsListMode == ProfileScreenFriendsMode.Requests
                    && _selectedRequest >= 0
                    && _selectedRequest < _incomingRequests.Count);
            _acceptRequestBtn.Enabled = _friendsListMode == ProfileScreenFriendsMode.Requests
                && _selectedRequest >= 0
                && _selectedRequest < _incomingRequests.Count;
            _denyRequestBtn.Enabled = _friendsListMode == ProfileScreenFriendsMode.Requests
                && _selectedRequest >= 0
                && _selectedRequest < _incomingRequests.Count;
            _unblockUserBtn.Enabled = _friendsListMode == ProfileScreenFriendsMode.Blocked
                && _selectedBlocked >= 0
                && _selectedBlocked < _blockedUsers.Count;

            _addFriendBtn.Update(input);
            _removeFriendBtn.Update(input);
            _blockUserBtn.Update(input);
            _acceptRequestBtn.Update(input);
            _denyRequestBtn.Update(input);
            _unblockUserBtn.Update(input);

            HandleListSelection(input);
            if (!_friendsSyncInProgress
                && (!_friendsEndpointUnavailable || DateTime.UtcNow >= _nextFriendsSyncUtc)
                && DateTime.UtcNow >= _nextFriendsSyncUtc)
                _ = SyncCanonicalFriendsAsync(seedFromLocal: !_friendsSeedAttempted);
        }
    }

    public void Draw(SpriteBatch sb, Rectangle viewport)
    {
        if (viewport != _viewport)
            OnResize(viewport);

        sb.Begin(samplerState: SamplerState.PointClamp);
        if (_bg is not null) sb.Draw(_bg, UiLayout.WindowViewport, Color.White);
        else sb.Draw(_pixel, UiLayout.WindowViewport, new Color(0, 0, 0));
        sb.End();

        sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiLayout.Transform);

        if (_panel is not null) sb.Draw(_panel, _panelRect, Color.White);
        else sb.Draw(_pixel, _panelRect, new Color(0, 0, 0, 200));

        DrawTabs(sb);

        if (_activeTab == ProfileScreenStartTab.Identity)
            DrawIdentityTab(sb);
        else
            DrawFriendsTab(sb);

        if (!string.IsNullOrWhiteSpace(_status))
        {
            var statusPos = new Vector2(_panelRect.X + 18, _panelRect.Bottom - _font.LineHeight - 8);
            _font.DrawString(sb, _status, statusPos, Color.White);
        }

        if (_activeTab == ProfileScreenStartTab.Identity)
        {
            _copyIdBtn.Draw(sb, _pixel, _font);
        }
        else
        {
            _friendsModeFriendsBtn.Draw(sb, _pixel, _font);
            _friendsModeRequestsBtn.Draw(sb, _pixel, _font);
            _friendsModeBlockedBtn.Draw(sb, _pixel, _font);

            if (_friendsListMode == ProfileScreenFriendsMode.Friends)
            {
                _addFriendBtn.Draw(sb, _pixel, _font);
                _removeFriendBtn.Draw(sb, _pixel, _font);
                _blockUserBtn.Draw(sb, _pixel, _font);
            }
            else if (_friendsListMode == ProfileScreenFriendsMode.Requests)
            {
                _acceptRequestBtn.Draw(sb, _pixel, _font);
                _denyRequestBtn.Draw(sb, _pixel, _font);
                _blockUserBtn.Draw(sb, _pixel, _font);
            }
            else
            {
                _unblockUserBtn.Draw(sb, _pixel, _font);
            }
        }

        _tabIdentityBtn.Draw(sb, _pixel, _font);
        _tabFriendsBtn.Draw(sb, _pixel, _font);
        _backBtn.Draw(sb, _pixel, _font);

        sb.End();
    }

    private void DrawTabs(SpriteBatch sb)
    {
        var identityColor = _activeTab == ProfileScreenStartTab.Identity ? new Color(60, 60, 60, 220) : new Color(30, 30, 30, 220);
        var friendsColor = _activeTab == ProfileScreenStartTab.Friends ? new Color(60, 60, 60, 220) : new Color(30, 30, 30, 220);
        sb.Draw(_pixel, _tabIdentityRect, identityColor);
        sb.Draw(_pixel, _tabFriendsRect, friendsColor);
        DrawBorder(sb, _tabIdentityRect, Color.White);
        DrawBorder(sb, _tabFriendsRect, Color.White);
    }

    private void DrawIdentityTab(SpriteBatch sb)
    {
        var x = _panelRect.X + 16;
        var y = _tabIdentityRect.Bottom + 14;
        
        // Offset for identity details (1 inch right)
        var detailX = x + 96;

        sb.Draw(_pixel, _usernameRect, new Color(30, 30, 30, 230));
        DrawBorder(sb, _usernameRect, Color.White);
        var name = (_identityStore.ReservedUsername ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = _profile.GetDisplayUsername();
        if (string.IsNullOrWhiteSpace(name))
            name = "(unclaimed)";
        var npos = new Vector2(_usernameRect.X + 8, _usernameRect.Y + (_usernameRect.Height - _font.LineHeight) / 2f);
        _font.DrawString(sb, name, npos, Color.White);
        _font.DrawString(sb, "Change username in Lattice Launcher (CLAIM/CHANGE).", new Vector2(detailX, _usernameRect.Bottom + 6), new Color(180, 180, 180));

        sb.Draw(_pixel, _iconRect, new Color(26, 26, 26, 220));
        DrawBorder(sb, _iconRect, new Color(160, 160, 160));
        _font.DrawString(sb, "ICON", new Vector2(_iconRect.X + 28, _iconRect.Y + 24), new Color(220, 220, 220));
        _font.DrawString(sb, "SOON", new Vector2(_iconRect.X + 24, _iconRect.Y + 24 + _font.LineHeight), new Color(170, 170, 170));

        var infoY = _iconRect.Bottom + 12;
        var eosSnapshot = EosRuntimeStatus.Evaluate(_eos);
        _font.DrawString(sb, eosSnapshot.StatusText, new Vector2(detailX, infoY), new Color(220, 180, 80));
        infoY += _font.LineHeight + 4;

        var id = (_eos?.LocalProductUserId ?? _identityStore.ProductUserId ?? string.Empty).Trim();
        var friendCode = string.IsNullOrWhiteSpace(id) ? string.Empty : EosIdentityStore.GenerateFriendCode(id);
        var reserved = (_identityStore.ReservedUsername ?? string.Empty).Trim();
        _font.DrawString(sb, $"MY ID: {(string.IsNullOrWhiteSpace(id) ? "(waiting...)" : id)}", new Vector2(detailX, infoY), Color.White);
        infoY += _font.LineHeight + 2;
        _font.DrawString(sb, $"RESERVED USERNAME: {(string.IsNullOrWhiteSpace(reserved) ? "(unclaimed)" : reserved)}", new Vector2(detailX, infoY), Color.White);
        infoY += _font.LineHeight + 2;
        _font.DrawString(sb, $"MY FRIEND CODE: {(string.IsNullOrWhiteSpace(friendCode) ? "(waiting...)" : friendCode)}", new Vector2(detailX, infoY), Color.White);
        infoY += _font.LineHeight + 2;
        _font.DrawString(sb, $"EOS Config: {EosRuntimeStatus.DescribeConfigSource()}", new Vector2(detailX, infoY), new Color(180, 180, 180));
    }

    private void DrawFriendsTab(SpriteBatch sb)
    {
        DrawFriendsModeTabs(sb);

        sb.Draw(_pixel, _friendsRect, new Color(20, 20, 20, 220));
        DrawBorder(sb, _friendsRect, Color.White);

        var title = _friendsListMode switch
        {
            ProfileScreenFriendsMode.Requests => "INCOMING REQUESTS",
            ProfileScreenFriendsMode.Blocked => "BLOCKED USERS",
            _ => "SAVED FRIENDS"
        };
        _font.DrawString(sb, title, new Vector2(_friendsRect.X + 8, _friendsRect.Y + 6), Color.White);

        if (_friendsListMode == ProfileScreenFriendsMode.Requests)
        {
            _font.DrawString(
                sb,
                $"OUTGOING: {_outgoingRequests.Count}",
                new Vector2(_friendsRect.Right - 160, _friendsRect.Y + 6),
                new Color(170, 170, 170));
        }

        var rowH = _font.LineHeight + 8;
        var y = _friendsRect.Y + _font.LineHeight + 12;
        if (_friendsListMode == ProfileScreenFriendsMode.Friends)
        {
            if (_profile.Friends.Count == 0)
            {
                _font.DrawString(sb, "(no friends saved yet)", new Vector2(_friendsRect.X + 8, _friendsRect.Y + _font.LineHeight + 10), new Color(180, 180, 180));
                return;
            }

            for (var i = 0; i < _profile.Friends.Count; i++)
            {
                var row = new Rectangle(_friendsRect.X + 6, y, _friendsRect.Width - 12, rowH);
                var selected = i == _selectedFriend;
                sb.Draw(_pixel, row, selected ? new Color(60, 90, 130, 220) : new Color(26, 26, 26, 200));
                DrawBorder(sb, row, selected ? new Color(200, 230, 255) : new Color(110, 110, 110));

                var f = _profile.Friends[i];
                var friendCode = EosIdentityStore.GenerateFriendCode(f.UserId);
                var displayName = !string.IsNullOrWhiteSpace(f.LastKnownDisplayName)
                    ? f.LastKnownDisplayName
                    : (!string.IsNullOrWhiteSpace(f.Label) ? f.Label : PlayerProfile.ShortId(f.UserId));
                var line = $"{displayName} ({friendCode})";
                _font.DrawString(sb, line, new Vector2(row.X + 8, row.Y + 3), Color.White);

                y += rowH + 4;
                if (y + rowH > _friendsRect.Bottom - 8)
                    break;
            }

            return;
        }

        if (_friendsListMode == ProfileScreenFriendsMode.Requests)
        {
            if (_incomingRequests.Count == 0 && _outgoingRequests.Count == 0)
            {
                _font.DrawString(sb, "(no pending requests)", new Vector2(_friendsRect.X + 8, _friendsRect.Y + _font.LineHeight + 10), new Color(180, 180, 180));
                return;
            }

            // Incoming
            if (_incomingRequests.Count > 0)
            {
                _font.DrawString(sb, "INCOMING:", new Vector2(_friendsRect.X + 8, y - 4), new Color(200, 200, 200));
                y += _font.LineHeight + 4;

                for (var i = 0; i < _incomingRequests.Count; i++)
                {
                    var row = new Rectangle(_friendsRect.X + 6, y, _friendsRect.Width - 12, rowH);
                    var selected = i == _selectedRequest;
                    sb.Draw(_pixel, row, selected ? new Color(90, 72, 40, 220) : new Color(26, 26, 26, 200));
                    DrawBorder(sb, row, selected ? new Color(240, 220, 160) : new Color(110, 110, 110));

                    var req = _incomingRequests[i];
                    var user = req.User;
                    var displayName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : PlayerProfile.ShortId(user.ProductUserId);
                    var line = $"{displayName} ({user.FriendCode})";
                    _font.DrawString(sb, line, new Vector2(row.X + 8, row.Y + 3), Color.White);

                    y += rowH + 4;
                    if (y + rowH > _friendsRect.Bottom - 8)
                        break;
                }
            }

            // Outgoing
            if (_outgoingRequests.Count > 0 && y + rowH + 20 < _friendsRect.Bottom)
            {
                y += 8;
                _font.DrawString(sb, "OUTGOING:", new Vector2(_friendsRect.X + 8, y), new Color(200, 200, 200));
                y += _font.LineHeight + 4;

                for (var i = 0; i < _outgoingRequests.Count; i++)
                {
                    var row = new Rectangle(_friendsRect.X + 6, y, _friendsRect.Width - 12, rowH);
                    sb.Draw(_pixel, row, new Color(20, 20, 20, 180));
                    DrawBorder(sb, row, new Color(80, 80, 80));

                    var req = _outgoingRequests[i];
                    var user = req.User;
                    var displayName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : PlayerProfile.ShortId(user.ProductUserId);
                    var line = $"{displayName} ({user.FriendCode}) [PENDING]";
                    _font.DrawString(sb, line, new Vector2(row.X + 8, row.Y + 3), new Color(180, 180, 180));

                    y += rowH + 4;
                    if (y + rowH > _friendsRect.Bottom - 8)
                        break;
                }
            }

            return;
        }

        if (_blockedUsers.Count == 0)
        {
            _font.DrawString(sb, "(no blocked users)", new Vector2(_friendsRect.X + 8, _friendsRect.Y + _font.LineHeight + 10), new Color(180, 180, 180));
            return;
        }

        for (var i = 0; i < _blockedUsers.Count; i++)
        {
            var row = new Rectangle(_friendsRect.X + 6, y, _friendsRect.Width - 12, rowH);
            var selected = i == _selectedBlocked;
            sb.Draw(_pixel, row, selected ? new Color(110, 52, 52, 220) : new Color(26, 26, 26, 200));
            DrawBorder(sb, row, selected ? new Color(245, 170, 170) : new Color(110, 110, 110));

            var blockedUser = _blockedUsers[i];
            var displayName = !string.IsNullOrWhiteSpace(blockedUser.DisplayName)
                ? blockedUser.DisplayName
                : PlayerProfile.ShortId(blockedUser.ProductUserId);
            var line = $"{displayName} ({blockedUser.FriendCode})";
            _font.DrawString(sb, line, new Vector2(row.X + 8, row.Y + 3), Color.White);

            y += rowH + 4;
            if (y + rowH > _friendsRect.Bottom - 8)
                break;
        }
    }

    private void DrawFriendsModeTabs(SpriteBatch sb)
    {
        var friendsColor = _friendsListMode == ProfileScreenFriendsMode.Friends ? new Color(60, 60, 60, 220) : new Color(30, 30, 30, 220);
        var requestsColor = _friendsListMode == ProfileScreenFriendsMode.Requests ? new Color(60, 60, 60, 220) : new Color(30, 30, 30, 220);
        var blockedColor = _friendsListMode == ProfileScreenFriendsMode.Blocked ? new Color(60, 60, 60, 220) : new Color(30, 30, 30, 220);
        sb.Draw(_pixel, _friendsModeFriendsRect, friendsColor);
        sb.Draw(_pixel, _friendsModeRequestsRect, requestsColor);
        sb.Draw(_pixel, _friendsModeBlockedRect, blockedColor);
        DrawBorder(sb, _friendsModeFriendsRect, Color.White);
        DrawBorder(sb, _friendsModeRequestsRect, Color.White);
        DrawBorder(sb, _friendsModeBlockedRect, Color.White);
    }

    private void HandleListSelection(InputState input)
    {
        if (!input.IsNewLeftClick())
            return;

        if (!_friendsRect.Contains(input.MousePosition))
            return;

        var rowH = _font.LineHeight + 8;
        var startY = _friendsRect.Y + _font.LineHeight + 12;
        var relY = input.MousePosition.Y - startY;
        if (relY < 0)
            return;

        var index = relY / (rowH + 4);
        if (_friendsListMode == ProfileScreenFriendsMode.Friends)
        {
            if (index < 0 || index >= _profile.Friends.Count)
                return;
            _selectedFriend = index;
            return;
        }

        if (_friendsListMode == ProfileScreenFriendsMode.Requests)
        {
            if (index < 0 || index >= _incomingRequests.Count)
                return;
            _selectedRequest = index;
            return;
        }

        if (index < 0 || index >= _blockedUsers.Count)
            return;
        _selectedBlocked = index;
    }

    private void OpenAddFriend()
    {
        _menus.Push(new AddFriendScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, _eos), _viewport);
    }

    private async Task RemoveFriendAsync()
    {
        if (_selectedFriend < 0 || _selectedFriend >= _profile.Friends.Count)
        {
            SetStatus("Select a friend first.");
            return;
        }

        var entry = _profile.Friends[_selectedFriend];
        if (_gate.CanUseOfficialOnline(_log, out _))
        {
            var remove = await _gate.RemoveFriendAsync(entry.UserId);
            if (!remove.Ok)
            {
                if (IsMissingFriendsEndpointError(remove.Message))
                {
                    _friendsEndpointUnavailable = true;
                    _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(45);
                    _profile.Friends.RemoveAt(_selectedFriend);
                    _selectedFriend = Math.Min(_selectedFriend, _profile.Friends.Count - 1);
                    _profile.Save(_log);
                    SetStatus($"Removed {PlayerProfile.ShortId(entry.UserId)} (local fallback).");
                    return;
                }
                SetStatus(string.IsNullOrWhiteSpace(remove.Message) ? "Failed to remove friend." : remove.Message);
                _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(8);
                return;
            }

            await SyncCanonicalFriendsAsync(seedFromLocal: false);
            _selectedFriend = Math.Min(_selectedFriend, _profile.Friends.Count - 1);
            SetStatus($"Removed {PlayerProfile.ShortId(entry.UserId)}.");
            return;
        }

        _profile.Friends.RemoveAt(_selectedFriend);
        _selectedFriend = Math.Min(_selectedFriend, _profile.Friends.Count - 1);
        _profile.Save(_log);
        SetStatus($"Removed {PlayerProfile.ShortId(entry.UserId)}.");
    }

    private async Task AcceptRequestAsync()
    {
        if (_selectedRequest < 0 || _selectedRequest >= _incomingRequests.Count)
        {
            SetStatus("Select a request first.");
            return;
        }

        var req = _incomingRequests[_selectedRequest];
        var result = await _gate.RespondToFriendRequestAsync(requesterProductUserId: req.ProductUserId, accept: true);
        if (!result.Ok)
        {
            SetStatus(result.Message ?? "Failed to accept request.");
            return;
        }

        await SyncCanonicalFriendsAsync(seedFromLocal: false);
        SetStatus($"Accepted {req.User.DisplayName}.");
    }

    private async Task DenyRequestAsync()
    {
        if (_selectedRequest < 0 || _selectedRequest >= _incomingRequests.Count)
        {
            SetStatus("Select a request first.");
            return;
        }

        var req = _incomingRequests[_selectedRequest];
        var result = await _gate.RespondToFriendRequestAsync(requesterProductUserId: req.ProductUserId, accept: false);
        if (!result.Ok)
        {
            SetStatus(result.Message ?? "Failed to deny request.");
            return;
        }

        await SyncCanonicalFriendsAsync(seedFromLocal: false);
        SetStatus($"Denied {req.User.DisplayName}.");
    }

    private async Task BlockSelectedUserAsync()
    {
        string targetId;
        if (_friendsListMode == ProfileScreenFriendsMode.Friends)
        {
            if (_selectedFriend < 0 || _selectedFriend >= _profile.Friends.Count)
            {
                SetStatus("Select a friend to block.");
                return;
            }

            targetId = (_profile.Friends[_selectedFriend].UserId ?? string.Empty).Trim();
        }
        else if (_friendsListMode == ProfileScreenFriendsMode.Requests)
        {
            if (_selectedRequest < 0 || _selectedRequest >= _incomingRequests.Count)
            {
                SetStatus("Select a request to block.");
                return;
            }

            targetId = (_incomingRequests[_selectedRequest].ProductUserId ?? string.Empty).Trim();
        }
        else
        {
            SetStatus("Select a friend or request to block.");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            SetStatus("Target ID unavailable.");
            return;
        }

        if (!_gate.CanUseOfficialOnline(_log, out var gateDenied))
        {
            SetStatus(gateDenied);
            return;
        }

        var block = await _gate.BlockUserAsync(targetId);
        if (!block.Ok)
        {
            if (IsMissingFriendsEndpointError(block.Message))
            {
                _friendsEndpointUnavailable = true;
                _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(45);
            }

            SetStatus(string.IsNullOrWhiteSpace(block.Message) ? "Could not block user." : block.Message);
            return;
        }

        await SyncCanonicalFriendsAsync(seedFromLocal: false);
        SetStatus("User blocked.");
        _friendsListMode = ProfileScreenFriendsMode.Blocked;
    }

    private async Task UnblockSelectedUserAsync()
    {
        if (_selectedBlocked < 0 || _selectedBlocked >= _blockedUsers.Count)
        {
            SetStatus("Select a blocked user first.");
            return;
        }

        if (!_gate.CanUseOfficialOnline(_log, out var gateDenied))
        {
            SetStatus(gateDenied);
            return;
        }

        var targetId = (_blockedUsers[_selectedBlocked].ProductUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetId))
        {
            SetStatus("Target ID unavailable.");
            return;
        }

        var unblock = await _gate.UnblockUserAsync(targetId);
        if (!unblock.Ok)
        {
            if (IsMissingFriendsEndpointError(unblock.Message))
            {
                _friendsEndpointUnavailable = true;
                _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(45);
            }

            SetStatus(string.IsNullOrWhiteSpace(unblock.Message) ? "Could not unblock user." : unblock.Message);
            return;
        }

        await SyncCanonicalFriendsAsync(seedFromLocal: false);
        SetStatus("User unblocked.");
    }

    private async Task<bool> SyncCanonicalFriendsAsync(bool seedFromLocal)
    {
        if (_friendsSyncInProgress || !_gate.CanUseOfficialOnline(_log, out _))
        {
            _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(5);
            return false;
        }

        _friendsSyncInProgress = true;
        try
        {
            var serverFriends = await _gate.GetFriendsAsync();
            if (!serverFriends.Ok)
            {
                if (IsMissingFriendsEndpointError(serverFriends.Message))
                {
                    if (!_friendsEndpointUnavailable)
                        SetStatus(string.IsNullOrWhiteSpace(serverFriends.Message) ? "Server friends endpoint unavailable." : serverFriends.Message);
                    _friendsEndpointUnavailable = true;
                    _friendsSeedAttempted = true;
                    _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(45);
                    return false;
                }

                _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(8);
                return false;
            }

            _friendsEndpointUnavailable = false;

            if (seedFromLocal && serverFriends.Friends.Count == 0 && _profile.Friends.Count > 0)
            {
                _friendsSeedAttempted = true;
                _incomingRequests.Clear();
                foreach (var request in serverFriends.IncomingRequests)
                {
                    if (request?.User == null)
                        continue;
                    _incomingRequests.Add(request);
                }

                _outgoingRequests.Clear();
                foreach (var request in serverFriends.OutgoingRequests)
                {
                    if (request?.User == null)
                        continue;
                    _outgoingRequests.Add(request);
                }

                _blockedUsers.Clear();
                foreach (var blocked in serverFriends.BlockedUsers)
                {
                    if (blocked == null)
                        continue;
                    _blockedUsers.Add(blocked);
                }

                _selectedRequest = Math.Min(_selectedRequest, _incomingRequests.Count - 1);
                _selectedBlocked = Math.Min(_selectedBlocked, _blockedUsers.Count - 1);
                _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(8);
                return true;
            }

            var existingPresence = _profile.Friends
                .Where(f => !string.IsNullOrWhiteSpace(f.UserId))
                .ToDictionary(
                    f => f.UserId,
                    f => f.LastKnownPresence ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            _profile.Friends.Clear();
            foreach (var user in serverFriends.Friends)
            {
                var id = (user.ProductUserId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var label = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
                _profile.AddOrUpdateFriend(id, label);
                var friend = _profile.Friends.FirstOrDefault(f => string.Equals(f.UserId, id, StringComparison.OrdinalIgnoreCase));
                if (friend == null)
                    continue;
                friend.LastKnownDisplayName = label;
                if (existingPresence.TryGetValue(id, out var presence))
                    friend.LastKnownPresence = presence;
            }

            _incomingRequests.Clear();
            foreach (var request in serverFriends.IncomingRequests)
            {
                if (request?.User == null)
                    continue;
                _incomingRequests.Add(request);
            }

            _outgoingRequests.Clear();
            foreach (var request in serverFriends.OutgoingRequests)
            {
                if (request?.User == null)
                    continue;
                _outgoingRequests.Add(request);
            }

            _blockedUsers.Clear();
            foreach (var blocked in serverFriends.BlockedUsers)
            {
                if (blocked == null)
                    continue;
                _blockedUsers.Add(blocked);
            }

            _profile.Save(_log);
            _selectedFriend = Math.Min(_selectedFriend, _profile.Friends.Count - 1);
            _selectedRequest = Math.Min(_selectedRequest, _incomingRequests.Count - 1);
            _selectedBlocked = Math.Min(_selectedBlocked, _blockedUsers.Count - 1);
            _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(8);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"Profile canonical friends sync failed: {ex.Message}");
            _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(8);
            return false;
        }
        finally
        {
            _friendsSyncInProgress = false;
        }
    }

    private void ShowFriendsUnavailableError()
    {
        _friendsEndpointUnavailable = true;
        _nextFriendsSyncUtc = DateTime.UtcNow.AddSeconds(45);
    }

    private async Task RefreshProfileDataAsync()
    {
        await SyncCanonicalFriendsAsync(seedFromLocal: false);
    }

    private static bool IsMissingFriendsEndpointError(string? message)
    {
        var value = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Not Found", StringComparison.OrdinalIgnoreCase);
    }

    private void CopyLocalId()
    {
        var id = (_eos?.LocalProductUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            SetStatus("ID unavailable right now.");
            return;
        }

        try
        {
            WinClipboard.SetText(id);
            SetStatus("Copied ID to clipboard.");
        }
        catch (Exception ex)
        {
            _log.Warn($"ProfileScreen copy ID failed: {ex.Message}");
            SetStatus("Clipboard copy failed.");
        }
    }

    private void SetStatus(string msg, double seconds = 3.0)
    {
        _status = msg;
        _statusExpiryUtc = seconds <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(seconds);
    }

    private void DrawTextBold(SpriteBatch sb, string text, Vector2 pos, Color color)
    {
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
