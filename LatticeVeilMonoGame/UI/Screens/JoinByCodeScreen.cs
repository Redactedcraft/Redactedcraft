using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinClipboard = System.Windows.Forms.Clipboard;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.Online.Lan;
using LatticeVeilMonoGame.Online.Eos;
using LatticeVeilMonoGame.Online.Gate;
using LatticeVeilMonoGame.UI;

namespace LatticeVeilMonoGame.UI.Screens;

/// <summary>
/// Join an online-hosted game by host code, friend code, or reserved username.
/// </summary>
public sealed class JoinByCodeScreen : IScreen
{
    private const int MaxCodeLength = 96;
    private const double PresenceRefreshIntervalSeconds = 5.0;
    private const double CanonicalSyncRetrySeconds = 8.0;
    private const double MissingEndpointRetrySeconds = 45.0;

    private readonly MenuStack _menus;
    private readonly AssetLoader _assets;
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Logger _log;
    private readonly PlayerProfile _profile;
    private readonly global::Microsoft.Xna.Framework.GraphicsDeviceManager _graphics;
    private readonly EosIdentityStore _identityStore;
    private EosClient? _eos;

    private readonly Button _joinBtn;
    private readonly Button _pasteBtn;
	private readonly Button _saveFriendBtn;
    private readonly Button _backBtn;

	private readonly List<Button> _friendJoinBtns = new();
	private readonly List<Button> _friendRemoveBtns = new();
    private readonly object _friendButtonsLock = new();
    private readonly object _presenceApplyLock = new();
    private Dictionary<string, GatePresenceEntry>? _pendingPresenceByJoinCode;

    private Texture2D? _bg;
    private Texture2D? _panel;

    private Rectangle _viewport;
    private Rectangle _panelRect;
    private Rectangle _codeRect;
	private Rectangle _friendsRect;
    private Rectangle _buttonRowRect;

    private bool _codeActive = true;
    private string _code = string.Empty;

    private bool _busy;
    private bool _canonicalSyncInProgress;
    private bool _canonicalSeedAttempted;
    private bool _friendsEndpointUnavailable;
    private double _nextCanonicalSyncAttempt;
    private bool _presenceBusy;
    private double _lastPresenceRefresh = -100;
    private string _localUsername = string.Empty;
    private string _localFriendCode = string.Empty;
    private double _now;
    private string _status = string.Empty;
    private double _statusUntil;
    private readonly Dictionary<string, GatePresenceEntry> _friendPresenceByJoinCode = new(StringComparer.OrdinalIgnoreCase);

    public JoinByCodeScreen(MenuStack menus, AssetLoader assets, PixelFont font, Texture2D pixel, Logger log, PlayerProfile profile,
        global::Microsoft.Xna.Framework.GraphicsDeviceManager graphics, EosClient? eos, string? initialCode)
    {
        _menus = menus;
        _assets = assets;
        _font = font;
        _pixel = pixel;
        _log = log;
        _profile = profile;
        _graphics = graphics;
        _eos = eos;
        _identityStore = EosIdentityStore.LoadOrCreate(_log);
        RefreshLocalIdentity();

        _code = (initialCode ?? string.Empty).Trim();
        if (_code.Length > MaxCodeLength)
            _code = _code.Substring(0, MaxCodeLength);

        _joinBtn = new Button("JOIN", () => _ = JoinAsync()) { BoldText = true };
        _pasteBtn = new Button("PASTE", PasteFromClipboard) { BoldText = true };
		_saveFriendBtn = new Button("SAVE FRIEND", () => _ = SaveFriendAsync()) { BoldText = true };
        _backBtn = new Button("BACK", () => _menus.Pop()) { BoldText = true };

        try
        {
            _bg = _assets.LoadTexture("textures/menu/backgrounds/JoinByCode_bg.png");
            _panel = _assets.LoadTexture("textures/menu/GUIS/Multiplayer_GUI.png");
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

        var panelW = Math.Min(980, viewport.Width - 90);
		var panelH = Math.Min(650, viewport.Height - 120);
        var panelX = viewport.X + (viewport.Width - panelW) / 2;
        var panelY = viewport.Y + (viewport.Height - panelH) / 2;
        _panelRect = new Rectangle(panelX, panelY, panelW, panelH);

        var pad = 20;
        var titleH = _font.LineHeight + 10;
        var labelH = _font.LineHeight + 6;
        var inputH = _font.LineHeight + 14;
        var codeY = panelY + pad + titleH + labelH + 6;
        _codeRect = new Rectangle(panelX + pad, codeY, panelW - pad * 2, inputH);

		// Reserve a stable block for identity/status lines to avoid crowding.
        var infoBlockY = _codeRect.Bottom + 8;
        var infoBlockH = (_font.LineHeight + 2) * 3 + 8;
		var friendsY = infoBlockY + infoBlockH + 8;
        var buttonRowH = _font.LineHeight + 14;
		var friendsH = Math.Max(140, _panelRect.Bottom - pad - buttonRowH - 12 - friendsY);
		_friendsRect = new Rectangle(panelX + pad, friendsY, panelW - pad * 2, friendsH);

        _buttonRowRect = new Rectangle(panelX + pad, _panelRect.Bottom - pad - buttonRowH, panelW - pad * 2, buttonRowH);

        var gap = 10;
		// Position back button in bottom-left corner of full screen with proper aspect ratio
		var backBtnMargin = 20;
		var backBtnBaseW = Math.Max(_backBtn.Texture?.Width ?? 0, 320);
		var backBtnBaseH = Math.Max(_backBtn.Texture?.Height ?? 0, (int)(backBtnBaseW * 0.28f));
		var backBtnScale = Math.Min(1f, Math.Min(240f / backBtnBaseW, 240f / backBtnBaseH));
		var backBtnW = Math.Max(1, (int)Math.Round(backBtnBaseW * backBtnScale));
		var backBtnH = Math.Max(1, (int)Math.Round(backBtnBaseH * backBtnScale));
		_backBtn.Bounds = new Rectangle(
			viewport.X + backBtnMargin, 
			viewport.Bottom - backBtnMargin - backBtnH, 
			backBtnW, 
			backBtnH
		);
		
		// Adjust other buttons to account for back button position
		var buttonW = (_buttonRowRect.Width - gap * 3) / 3;
		_joinBtn.Bounds = new Rectangle(_buttonRowRect.X, _buttonRowRect.Y, buttonW, buttonRowH);
		_pasteBtn.Bounds = new Rectangle(_joinBtn.Bounds.Right + gap, _buttonRowRect.Y, buttonW, buttonRowH);
		_saveFriendBtn.Bounds = new Rectangle(_pasteBtn.Bounds.Right + gap, _buttonRowRect.Y, buttonW, buttonRowH);

		RebuildFriendButtons();
    }

    public void Update(GameTime gameTime, InputState input)
    {
        _now = gameTime.TotalGameTime.TotalSeconds;

        if (input.IsNewKeyPress(Keys.Escape))
        {
            _menus.Pop();
            return;
        }

        if (input.IsNewLeftClick())
        {
            var p = input.MousePosition;
            _codeActive = _codeRect.Contains(p);
        }

        if (_codeActive && !_busy)
        {
            HandleTextInput(input, ref _code, MaxCodeLength);
            if (input.IsNewKeyPress(Keys.Enter))
                _ = JoinAsync();
        }

        RefreshLocalIdentity();
        ApplyPendingPresenceRefresh();
        if (!_busy && !_canonicalSyncInProgress && _now >= _nextCanonicalSyncAttempt)
            _ = SyncCanonicalFriendsAsync(seedFromLocal: !_canonicalSeedAttempted);
        if (!_busy && !_presenceBusy && _now - _lastPresenceRefresh >= PresenceRefreshIntervalSeconds)
            _ = RefreshFriendPresenceAsync();

		// Enabled/disabled state
        var snapshot = EosRuntimeStatus.Evaluate(_eos);
		_joinBtn.Enabled = !_busy && snapshot.Reason == EosRuntimeReason.Ready && !string.IsNullOrWhiteSpace(_code);
		_pasteBtn.Enabled = !_busy;
		_saveFriendBtn.Enabled = !_busy && !string.IsNullOrWhiteSpace(_code);
		_backBtn.Enabled = true;

		_joinBtn.Update(input);
		_pasteBtn.Update(input);
		_saveFriendBtn.Update(input);
		_backBtn.Update(input);

        GetFriendButtonSnapshots(out var friendJoinButtons, out var friendRemoveButtons);
		foreach (var b in friendJoinButtons) b.Update(input);
		foreach (var b in friendRemoveButtons) b.Update(input);

        if (_statusUntil > 0 && _now > _statusUntil)
        {
            _statusUntil = 0;
            _status = string.Empty;
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

        if (_panel is not null)
        {
            sb.Draw(_panel, _panelRect, Color.White);
        }
        else
        {
            sb.Draw(_pixel, _panelRect, new Color(0, 0, 0, 180));
            DrawBorder(sb, _panelRect, Color.White);
        }

        var title = "JOIN ONLINE";
        var tSize = _font.MeasureString(title);
        var tPos = new Vector2(_panelRect.Center.X - tSize.X / 2f, _panelRect.Y + 12);
        DrawTextBold(sb, title, tPos, Color.White);

        var label = "HOST CODE / FRIEND CODE / USERNAME";
        _font.DrawString(sb, label, new Vector2(_codeRect.X, _codeRect.Y - _font.LineHeight - 6), Color.White);

        sb.Draw(_pixel, _codeRect, _codeActive ? new Color(35, 35, 35, 230) : new Color(20, 20, 20, 230));
        DrawBorder(sb, _codeRect, Color.White);

        var codeText = string.IsNullOrWhiteSpace(_code) ? "(paste host code, RC-code, or username)" : _code;
        var cColor = string.IsNullOrWhiteSpace(_code) ? new Color(180, 180, 180) : Color.White;
        _font.DrawString(sb, codeText, new Vector2(_codeRect.X + 6, _codeRect.Y + 6), cColor);

        var infoY = _codeRect.Bottom + 8;
        _font.DrawString(sb, $"YOU: {_localUsername}", new Vector2(_codeRect.X, infoY), new Color(220, 220, 220));
        infoY += _font.LineHeight + 2;
        _font.DrawString(sb, $"FRIEND CODE: {(string.IsNullOrWhiteSpace(_localFriendCode) ? "(waiting for EOS login...)" : _localFriendCode)}", new Vector2(_codeRect.X, infoY), new Color(220, 220, 220));
        infoY += _font.LineHeight + 2;
        _font.DrawString(sb, EosRuntimeStatus.Evaluate(_eos).StatusText, new Vector2(_codeRect.X, infoY), new Color(220, 180, 80));

		// Saved friends list
		_font.DrawString(sb, "SAVED FRIENDS", new Vector2(_friendsRect.X, _friendsRect.Y - _font.LineHeight - 6), new Color(220, 220, 220));
        sb.Draw(_pixel, _friendsRect, new Color(20, 20, 20, 190));
        DrawBorder(sb, _friendsRect, new Color(180, 180, 180));
		if (_profile.Friends.Count == 0)
		{
			_font.DrawString(sb, "(none yet - paste an ID above and press SAVE FRIEND)", new Vector2(_friendsRect.X + 6, _friendsRect.Y + 6), new Color(180, 180, 180));
		}
		else
		{
            GetFriendButtonSnapshots(out var friendJoinButtons, out var friendRemoveButtons);
			foreach (var b in friendJoinButtons) b.Draw(sb, _pixel, _font);
			foreach (var b in friendRemoveButtons) b.Draw(sb, _pixel, _font);
		}

        _joinBtn.Draw(sb, _pixel, _font);
        _pasteBtn.Draw(sb, _pixel, _font);
		_saveFriendBtn.Draw(sb, _pixel, _font);
        _backBtn.Draw(sb, _pixel, _font);

        if (!string.IsNullOrWhiteSpace(_status))
        {
            var pos = new Vector2(_panelRect.X + 14, _panelRect.Bottom - _font.LineHeight - 8);
            _font.DrawString(sb, _status, pos, Color.White);
        }

        sb.End();
    }

    private async Task JoinAsync()
    {
        if (_busy)
            return;

        var rawCode = (_code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            SetStatus("Enter a host code.");
            return;
        }

        if (!TryNormalizeHostCode(rawCode, out var code, out var normalizeMessage))
        {
            SetStatus(normalizeMessage);
            return;
        }

        var snapshot = EosRuntimeStatus.Evaluate(_eos);
        if (snapshot.Reason != EosRuntimeReason.Ready)
        {
            SetStatus(snapshot.StatusText);
            return;
        }

        var gate = OnlineGateClient.GetOrCreate();
        if (!gate.CanUseOfficialOnline(_log, out var gateDenied))
        {
            SetStatus(gateDenied);
            return;
        }

        var resolvedCode = await ResolveHostCodeAsync(code, gate);
        if (!resolvedCode.Ok)
        {
            SetStatus(resolvedCode.Error);
            return;
        }
        code = resolvedCode.HostCode;
        _code = code;

        _busy = true;
        SetStatus("Connecting...");

        try
        {
            if (_eos == null)
            {
                SetStatus("EOS CLIENT UNAVAILABLE");
                return;
            }

			// Relay connections can take longer (especially on first connect).
			var result = await EosP2PClientSession.ConnectAsync(_log, _eos, code, _profile.GetDisplayUsername(), TimeSpan.FromSeconds(25));
            if (!result.Success || result.Session == null)
            {
                SetStatus($"Connect failed: {result.Error}");
                _log.Warn($"EOS join-by-code failed: {result.Error}");
                return;
            }

            var info = result.WorldInfo;
            _profile.AddOrUpdateFriend(code);
            _profile.Save(_log);
            var worldDir = JoinedWorldCache.PrepareJoinedWorldPath(info, _log);
            var metaPath = Path.Combine(worldDir, "world.json");
            var meta = WorldMeta.CreateFlat(info.WorldName, info.GameMode, info.Width, info.Height, info.Depth, info.Seed);
            meta.PlayerCollision = info.PlayerCollision;
            meta.WorldId = JoinedWorldCache.ResolveWorldId(info);
            meta.Save(metaPath, _log);

            _menus.Push(
                new GameWorldScreen(_menus, _assets, _font, _pixel, _log, _profile, _graphics, worldDir, metaPath, result.Session),
                _viewport);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetStatus(string msg, double seconds = 3.0)
    {
        _status = msg;
        _statusUntil = seconds <= 0 ? 0 : _now + seconds;
    }

	private void PasteFromClipboard()
	{
		try
		{
			var text = WinClipboard.GetText();
			if (string.IsNullOrWhiteSpace(text))
			{
				SetStatus("Clipboard is empty.");
				return;
			}

			// Keep it simple: trim and cap to the same limit the input box uses.
			text = text.Trim();
			if (text.Length > MaxCodeLength)
				text = text.Substring(0, MaxCodeLength);

			_code = text;
			if (TryNormalizeHostCode(_code, out var normalizedCode, out _))
				_code = normalizedCode;
			SetStatus("Pasted.", 2);
		}
		catch (Exception ex)
		{
			_log.Warn($"PasteFromClipboard failed: {ex.Message}");
			SetStatus("Could not read clipboard.", 3);
		}
	}

	private async Task SaveFriendAsync()
	{
		if (_busy)
			return;

		if (!TryNormalizeHostCode(_code, out var id, out var normalizeMessage))
		{
			SetStatus(normalizeMessage, 2);
			return;
		}

		var gate = OnlineGateClient.GetOrCreate();
		if (!gate.CanUseOfficialOnline(_log, out var gateDenied))
		{
			SetStatus(gateDenied);
			return;
		}

		var resolved = await ResolveHostCodeAsync(id, gate);
		if (!resolved.Ok)
		{
			SetStatus(resolved.Error, 2.5);
			return;
		}

		id = resolved.HostCode;
		_code = id;
		var label = resolved.DisplayName;
		if (string.IsNullOrWhiteSpace(label))
			label = PlayerProfile.ShortId(id);

        var add = await gate.AddFriendAsync(id);
        if (!add.Ok)
        {
            if (IsMissingFriendsEndpointError(add.Message))
            {
                _friendsEndpointUnavailable = true;
                _nextCanonicalSyncAttempt = _now + MissingEndpointRetrySeconds;
                _profile.AddOrUpdateFriend(id, label);
                _profile.Save(_log);
                SetStatus("Saved locally. Server friends endpoint unavailable on this build.", 4);
                RebuildFriendButtons();
                return;
            }

            SetStatus(string.IsNullOrWhiteSpace(add.Message) ? "Could not save friend." : add.Message, 3);
            _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;
            return;
        }

        await SyncCanonicalFriendsAsync(seedFromLocal: false);
		SetStatus(string.IsNullOrWhiteSpace(add.Message) ? $"Saved friend {label}" : add.Message, 3);
		RebuildFriendButtons();
		_ = RefreshFriendPresenceAsync();
	}

    private bool TryNormalizeHostCode(string rawInput, out string hostCode, out string error)
    {
        hostCode = string.Empty;
        error = string.Empty;

        var value = (rawInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Host code is empty.";
            return false;
        }

        // Allow lines copied from in-game chat/status text.
        var hostCodeMarker = "HOST CODE:";
        var markerIndex = value.IndexOf(hostCodeMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            value = value.Substring(markerIndex + hostCodeMarker.Length).Trim();
        var hostLinkMarker = "HOST LINK:";
        markerIndex = value.IndexOf(hostLinkMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            value = value.Substring(markerIndex + hostLinkMarker.Length).Trim();

        // Support the share-link format: latticeveil://join/<hostCode>
        var embeddedLinkIndex = value.IndexOf("latticeveil://join/", StringComparison.OrdinalIgnoreCase);
        if (embeddedLinkIndex >= 0)
            value = value.Substring(embeddedLinkIndex);
        if (value.StartsWith("latticeveil://join/", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("latticeveil://join/".Length).Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, "latticeveil", StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, "join", StringComparison.OrdinalIgnoreCase))
            {
                var path = uri.AbsolutePath?.Trim('/') ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(path))
                    value = path;
                else
                    value = (uri.Query ?? string.Empty).Replace("?code=", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
            else
            {
                // Support web links like ...?code=<hostCode>
                var query = uri.Query ?? string.Empty;
                var codeIndex = query.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
                if (codeIndex >= 0)
                {
                    var code = query.Substring(codeIndex + 5);
                    var amp = code.IndexOf('&');
                    if (amp >= 0)
                        code = code.Substring(0, amp);
                    value = Uri.UnescapeDataString(code).Trim();
                }
            }
        }

        if (value.StartsWith("puid=", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(5).Trim();

        // Allow friend-code input (RC-XXXXXXXX) by resolving against saved friends.
        if (value.StartsWith("RC-", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = _profile.Friends
                .FirstOrDefault(f => string.Equals(EosIdentityStore.GenerateFriendCode(f.UserId), value, StringComparison.OrdinalIgnoreCase));
            if (resolved != null)
                value = resolved.UserId;
        }

        if (value.Length > MaxCodeLength)
            value = value.Substring(0, MaxCodeLength);

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Host code is empty.";
            return false;
        }

        hostCode = value;
        return true;
    }

    private async Task<(bool Ok, string HostCode, string DisplayName, string Error)> ResolveHostCodeAsync(string normalizedCode, OnlineGateClient gate)
    {
        var value = (normalizedCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return (false, string.Empty, string.Empty, "Host code is empty.");

        var resolve = await gate.ResolveIdentityAsync(value);
        if (resolve.Found && resolve.User != null)
        {
            var user = resolve.User;
            var display = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
            return (true, user.ProductUserId, display, string.Empty);
        }

        if (value.StartsWith("RC-", StringComparison.OrdinalIgnoreCase))
        {
            var reason = string.IsNullOrWhiteSpace(resolve.Reason) ? "Friend code not found." : resolve.Reason;
            return (false, string.Empty, string.Empty, reason);
        }

        if (LooksLikeReservedUsername(value))
        {
            var reason = string.IsNullOrWhiteSpace(resolve.Reason) ? "Username not found." : resolve.Reason;
            return (false, string.Empty, string.Empty, reason);
        }

        return (true, value, string.Empty, string.Empty);
    }

    private static bool LooksLikeReservedUsername(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        // Reserved usernames are short; long values are likely direct host IDs.
        if (value.Length > 16)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsLetterOrDigit(c) || c == '_')
                continue;
            return false;
        }

        return true;
    }

	private async void RemoveFriend(string userId)
	{
        var gate = OnlineGateClient.GetOrCreate();
        if (gate.CanUseOfficialOnline(_log, out _))
        {
            var remove = await gate.RemoveFriendAsync(userId);
            if (!remove.Ok)
            {
                if (IsMissingFriendsEndpointError(remove.Message))
                {
                    _friendsEndpointUnavailable = true;
                    _nextCanonicalSyncAttempt = _now + MissingEndpointRetrySeconds;
                    _profile.RemoveFriend(userId);
                    _profile.Save(_log);
                    SetStatus($"Removed friend {PlayerProfile.ShortId(userId)} (local fallback)", 3);
                    RebuildFriendButtons();
                    return;
                }
                SetStatus(string.IsNullOrWhiteSpace(remove.Message) ? "Failed to remove friend." : remove.Message, 3);
                _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;
                return;
            }

            await SyncCanonicalFriendsAsync(seedFromLocal: false);
            SetStatus($"Removed friend {PlayerProfile.ShortId(userId)}", 3);
            RebuildFriendButtons();
            return;
        }

        _profile.RemoveFriend(userId);
        _profile.Save(_log);
        SetStatus($"Removed friend {PlayerProfile.ShortId(userId)}", 3);
        RebuildFriendButtons();
	}

	private void RebuildFriendButtons()
	{
		var joinButtons = new List<Button>();
		var removeButtons = new List<Button>();

		var friends = _profile.Friends;
		if (friends == null || friends.Count == 0)
        {
            lock (_friendButtonsLock)
            {
                _friendJoinBtns.Clear();
                _friendRemoveBtns.Clear();
            }
            return;
        }

		var rowH = _font.LineHeight + 14;
		var gap = 6;
        var maxRowsByHeight = Math.Max(1, (_friendsRect.Height - 8) / (rowH + gap));
		var maxRows = Math.Min(Math.Min(8, maxRowsByHeight), friends.Count);
		var joinW = Math.Max(140, _friendsRect.Width - 54);
		for (int i = 0; i < maxRows; i++)
		{
			var f = friends[i];
			var y = _friendsRect.Y + 4 + i * (rowH + gap);
			var joinBounds = new Rectangle(_friendsRect.X, y, joinW, rowH);
			var removeBounds = new Rectangle(_friendsRect.Right - 44, y, 44, rowH);

            var label = TruncateForButton(BuildFriendRowLabel(f), 72);

			var joinBtn = new Button(label, () =>
			{
				_code = f.UserId;
				SetStatus($"Joining {PlayerProfile.ShortId(f.UserId)}...", 2);
				_ = JoinAsync();
			}) { BoldText = true };
			joinBtn.Bounds = joinBounds;
			joinButtons.Add(joinBtn);

			var removeBtn = new Button("X", () => RemoveFriend(f.UserId)) { BoldText = true };
			removeBtn.Bounds = removeBounds;
			removeButtons.Add(removeBtn);
		}

        lock (_friendButtonsLock)
        {
            _friendJoinBtns.Clear();
            _friendJoinBtns.AddRange(joinButtons);
            _friendRemoveBtns.Clear();
            _friendRemoveBtns.AddRange(removeButtons);
        }
	}

    private string BuildFriendRowLabel(PlayerProfile.FriendEntry friend)
    {
        if (_friendPresenceByJoinCode.TryGetValue(friend.UserId, out var snapshot))
        {
            var name = string.IsNullOrWhiteSpace(snapshot.DisplayName)
                ? (!string.IsNullOrWhiteSpace(friend.Label) ? friend.Label : PlayerProfile.ShortId(friend.UserId))
                : snapshot.DisplayName;
            var friendCode = string.IsNullOrWhiteSpace(snapshot.FriendCode)
                ? TryFormatFriendCode(friend.UserId)
                : snapshot.FriendCode;
            if (!string.IsNullOrWhiteSpace(friendCode))
                name = $"{name} ({friendCode})";
            var state = snapshot.IsHosting
                ? $"HOSTING {(string.IsNullOrWhiteSpace(snapshot.WorldName) ? "WORLD" : snapshot.WorldName)}"
                : (string.IsNullOrWhiteSpace(snapshot.Status) ? "ONLINE" : snapshot.Status.ToUpperInvariant());
            return $"{name} | {state}";
        }

        var fallbackName = !string.IsNullOrWhiteSpace(friend.LastKnownDisplayName)
            ? friend.LastKnownDisplayName
            : (!string.IsNullOrWhiteSpace(friend.Label) ? friend.Label : $"FRIEND {PlayerProfile.ShortId(friend.UserId)}");
        var fallbackCode = TryFormatFriendCode(friend.UserId);
        if (!string.IsNullOrWhiteSpace(fallbackCode))
            fallbackName = $"{fallbackName} ({fallbackCode})";
        if (!string.IsNullOrWhiteSpace(friend.LastKnownPresence))
            return $"{fallbackName} | {friend.LastKnownPresence.ToUpperInvariant()}";

        if (!string.IsNullOrWhiteSpace(fallbackCode))
            return fallbackName;

        return $"{fallbackName} ({PlayerProfile.ShortId(friend.UserId)})";
    }

    private void RefreshLocalIdentity()
    {
        _eos ??= EosClientProvider.GetOrCreate(_log, "deviceid", allowRetry: true);
        var dirty = false;
        if (!string.IsNullOrWhiteSpace(_eos?.LocalProductUserId)
            && !string.Equals(_identityStore.ProductUserId, _eos.LocalProductUserId, StringComparison.Ordinal))
        {
            _identityStore.ProductUserId = _eos.LocalProductUserId;
            dirty = true;
        }

        var displayName = EosIdentityStore.NormalizeDisplayName(_identityStore.DisplayName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = EosIdentityStore.NormalizeDisplayName(_profile.GetDisplayUsername());
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "Player";
            _identityStore.DisplayName = displayName;
            dirty = true;
        }

        if (dirty)
            _identityStore.Save(_log);

        _localUsername = displayName;
        _localFriendCode = _identityStore.GetFriendCode();
        if (string.IsNullOrWhiteSpace(_localFriendCode) && !string.IsNullOrWhiteSpace(_eos?.LocalProductUserId))
            _localFriendCode = EosIdentityStore.GenerateFriendCode(_eos.LocalProductUserId);
    }

    private static string TryFormatFriendCode(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;
        if (trimmed.StartsWith("RC-", StringComparison.OrdinalIgnoreCase))
            return trimmed.ToUpperInvariant();
        return EosIdentityStore.GenerateFriendCode(trimmed);
    }

    private async Task RefreshFriendPresenceAsync()
    {
        _lastPresenceRefresh = _now;
        var gate = OnlineGateClient.GetOrCreate();
        if (!gate.CanUseOfficialOnline(_log, out _))
            return;

        _presenceBusy = true;
        try
        {
            var ids = _profile.Friends
                .Select(f => (f.UserId ?? string.Empty).Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ids.Length == 0)
                return;

            var query = await gate.QueryPresenceAsync(ids);
            if (!query.Ok)
                return;

            var refreshed = new Dictionary<string, GatePresenceEntry>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < query.Entries.Count; i++)
            {
                var entry = query.Entries[i];
                if (string.IsNullOrWhiteSpace(entry.ProductUserId))
                    continue;
                refreshed[entry.ProductUserId] = entry;
            }

            lock (_presenceApplyLock)
                _pendingPresenceByJoinCode = refreshed;
        }
        catch (Exception ex)
        {
            _log.Warn($"JoinByCode friend presence refresh failed: {ex.Message}");
        }
        finally
        {
            _presenceBusy = false;
        }
    }

    private void ApplyPendingPresenceRefresh()
    {
        Dictionary<string, GatePresenceEntry>? pending;
        lock (_presenceApplyLock)
        {
            pending = _pendingPresenceByJoinCode;
            _pendingPresenceByJoinCode = null;
        }

        if (pending == null)
            return;

        _friendPresenceByJoinCode.Clear();
        foreach (var pair in pending)
            _friendPresenceByJoinCode[pair.Key] = pair.Value;

        var changed = false;
        foreach (var friend in _profile.Friends)
        {
            if (!_friendPresenceByJoinCode.TryGetValue(friend.UserId, out var snap))
                continue;

            var resolvedName = string.IsNullOrWhiteSpace(snap.DisplayName)
                ? friend.LastKnownDisplayName
                : snap.DisplayName;
            if (!string.IsNullOrWhiteSpace(resolvedName)
                && !string.Equals(friend.LastKnownDisplayName, resolvedName, StringComparison.Ordinal))
            {
                friend.LastKnownDisplayName = resolvedName;
                changed = true;
            }

            var presence = snap.IsHosting
                ? $"hosting {(!string.IsNullOrWhiteSpace(snap.WorldName) ? snap.WorldName : "world")}"
                : (string.IsNullOrWhiteSpace(snap.Status) ? "online" : snap.Status);
            if (!string.Equals(friend.LastKnownPresence, presence, StringComparison.Ordinal))
            {
                friend.LastKnownPresence = presence;
                changed = true;
            }
        }

        if (changed)
            _profile.Save(_log);

        RebuildFriendButtons();
    }

    private async Task<bool> SyncCanonicalFriendsAsync(bool seedFromLocal)
    {
        var gate = OnlineGateClient.GetOrCreate();
        if (_canonicalSyncInProgress || !gate.CanUseOfficialOnline(_log, out _))
            return false;
        if (_friendsEndpointUnavailable && _now < _nextCanonicalSyncAttempt)
            return false;

        _canonicalSyncInProgress = true;
        try
        {
            var serverFriends = await gate.GetFriendsAsync();
            if (!serverFriends.Ok)
            {
                if (IsMissingFriendsEndpointError(serverFriends.Message))
                {
                    if (!_friendsEndpointUnavailable)
                        SetStatus("Server friends endpoint unavailable. Using local friends list.", 4);

                    _friendsEndpointUnavailable = true;
                    _canonicalSeedAttempted = true;
                    _nextCanonicalSyncAttempt = _now + MissingEndpointRetrySeconds;
                    return false;
                }

                _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;
                return false;
            }

            _friendsEndpointUnavailable = false;
            _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;

            if (seedFromLocal && serverFriends.Friends.Count == 0 && _profile.Friends.Count > 0)
            {
                _canonicalSeedAttempted = true;
                _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;
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

            _profile.Save(_log);
            _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"JoinByCode canonical friends sync failed: {ex.Message}");
            _nextCanonicalSyncAttempt = _now + CanonicalSyncRetrySeconds;
            return false;
        }
        finally
        {
            _canonicalSyncInProgress = false;
        }
    }

    private static bool IsMissingFriendsEndpointError(string? message)
    {
        var value = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Not Found", StringComparison.OrdinalIgnoreCase);
    }

    private void GetFriendButtonSnapshots(out Button[] joinButtons, out Button[] removeButtons)
    {
        lock (_friendButtonsLock)
        {
            joinButtons = _friendJoinBtns.ToArray();
            removeButtons = _friendRemoveBtns.ToArray();
        }
    }

    private static string TruncateForButton(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (value.Length <= maxChars)
            return value;
        return value.Substring(0, Math.Max(0, maxChars - 3)) + "...";
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
