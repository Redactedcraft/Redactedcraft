using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LatticeVeilMonoGame.Core;

#if EOS_SDK
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Friends;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.Presence;
using Epic.OnlineServices.UserInfo;
using EosAuth = Epic.OnlineServices.Auth;
#endif

namespace LatticeVeilMonoGame.Online.Eos;

public sealed class EosClient : IDisposable
{
    private readonly Logger _log;
    private bool _disposed;
    private const string PresenceKeyHosting = "rc_hosting";
    private const string PresenceKeyWorld = "rc_world";

#if EOS_SDK
    private readonly EosConfig _config;
    private PlatformInterface? _platform;
    private ConnectInterface? _connect;
    private EosAuth.AuthInterface? _auth;
    private UserInfoInterface? _userInfo;
    private FriendsInterface? _friends;
    private PresenceInterface? _presence;
    private P2PInterface? _p2p;
    private ProductUserId? _localUserId;
    private EpicAccountId? _epicAccountId;
    private string? _epicDisplayName;
    private bool _deviceLoginStarted;
    private bool _epicLoginStarted;
    private bool _allowDeviceFallback;
    private bool _silentLoginOnly;
    private readonly object _sdkLock = new();
#endif

    private EosClient(Logger log, EosConfig config) { _log = log; _config = config; }

    public static EosClient? TryCreate(Logger log, string? loginModeOverride = null, bool autoLogin = true)
    {
        EosConfig? config;
        try
        {
            config = EosConfig.Load(log);
        }
        catch (Exception ex)
        {
            log.Error($"EOS config validation failed: {ex.Message}");
            return null;
        }

        if (config == null) return null;
#if EOS_SDK
        if (!string.IsNullOrWhiteSpace(loginModeOverride)) config.LoginMode = loginModeOverride;
        var client = new EosClient(log, config);
        if (!client.Initialize()) return null;
        if (autoLogin) client.BeginLogin();
        return client;
#else
        return null;
#endif
    }

    public string? LocalProductUserId => _localUserId?.ToString();
    public string? EpicAccountId => _epicAccountId?.ToString();
    public string? EpicDisplayName => _epicDisplayName;
    public string? DeviceId => _localUserId?.ToString();

#if EOS_SDK
    public ProductUserId? LocalProductUserIdHandle => _localUserId;
    public EpicAccountId? EpicAccountIdHandle => _epicAccountId;
    public P2PInterface? P2PInterface => _p2p;
#endif

    public bool IsLoggedIn { get { lock (_sdkLock) return _localUserId != null && _localUserId.IsValid(); } }
    public bool IsEpicLoggedIn { get { lock (_sdkLock) return _epicAccountId != null && _epicAccountId.IsValid(); } }

    public void Tick() { lock (_sdkLock) { if (!_disposed) _platform?.Tick(); } }

    public void StartLogin() { if (IsLoggedIn || _deviceLoginStarted) return; BeginLogin(); }

    public void StartSilentLogin() { if (IsLoggedIn || _deviceLoginStarted) return; BeginLogin(); }

    private bool Initialize()
    {
#if EOS_SDK
        try
        {
            InitializeOptions initOptions = new InitializeOptions { ProductName = _config.ProductName, ProductVersion = _config.ProductVersion };
            Result initResult; lock (_sdkLock) initResult = PlatformInterface.Initialize(ref initOptions);
            if (initResult != Result.Success && initResult != Result.AlreadyConfigured) return false;
            Options options = new Options { ProductId = _config.ProductId, SandboxId = _config.SandboxId, DeploymentId = _config.DeploymentId, ClientCredentials = new ClientCredentials { ClientId = _config.ClientId, ClientSecret = _config.ClientSecret ?? string.Empty }, IsServer = false };
            lock (_sdkLock) _platform = PlatformInterface.Create(ref options);
            if (_platform == null) return false;
            _connect = _platform.GetConnectInterface();
            _auth = _platform.GetAuthInterface();
            _userInfo = _platform.GetUserInfoInterface();
            _friends = _platform.GetFriendsInterface();
            _presence = _platform.GetPresenceInterface();
            _p2p = _platform.GetP2PInterface();
            ConfigureRelayControl();
            return true;
        }
        catch { return false; }
#else
        return false;
#endif
    }

    private void BeginLogin()
    {
#if EOS_SDK
        var mode = (_config.LoginMode ?? "deviceid").Trim().ToLowerInvariant();
        if (mode == "epic")
        {
            BeginDeviceIdLogin();
            return;
        }

        BeginDeviceIdLogin();
#endif
    }

    private void BeginDeviceIdLogin()
    {
#if EOS_SDK
        if (_connect == null || _deviceLoginStarted) return;
        _deviceLoginStarted = true;
        LoginOptions opts = new LoginOptions
        {
            Credentials = new Credentials
            {
                Type = ExternalCredentialType.DeviceidAccessToken,
                Token = ""
            },
            UserLoginInfo = new UserLoginInfo
            {
                DisplayName = Environment.UserName
            }
        };
        lock (_sdkLock) _connect.Login(ref opts, null, OnLoginComplete);
#endif
    }

    private void OnLoginComplete(ref LoginCallbackInfo info)
    {
#if EOS_SDK
        if (info.ResultCode == Result.Success)
        {
            _localUserId = info.LocalUserId;
            _deviceLoginStarted = false;
            _log.Info("EOS login success (deviceid)");
            return;
        }

        if (info.ResultCode == Result.InvalidUser)
        {
            _log.Warn("EOS login returned InvalidUser; attempting CreateDeviceId...");
            CreateDeviceId();
            return;
        }

        _deviceLoginStarted = false;
        _log.Warn($"EOS login failed: {info.ResultCode}");
#endif
    }

    private void CreateDeviceId()
    {
#if EOS_SDK
        if (_connect == null) return;
        CreateDeviceIdOptions opts = new CreateDeviceIdOptions { DeviceModel = Environment.MachineName };
        lock (_sdkLock) _connect.CreateDeviceId(ref opts, null, OnCreateDeviceId);
#endif
    }

    private void OnCreateDeviceId(ref CreateDeviceIdCallbackInfo info)
    {
#if EOS_SDK
        _log.Info($"EOS CreateDeviceId result: {info.ResultCode}");
        if (info.ResultCode == Result.Success || info.ResultCode == Result.DuplicateNotAllowed)
        {
            _log.Info("EOS CreateDeviceId completed; retrying deviceid login...");
            _deviceLoginStarted = false;
            BeginDeviceIdLogin();
            return;
        }
        return;
#endif
    }

    public async Task<bool> SetHostingPresenceAsync(string? world, bool hosting)
    {
#if EOS_SDK
        if (_presence == null || _localUserId == null) return false;
        // Connect-only apps use ProductUserId for presence calls if available, but SDK often expects EpicAccountId for the modification handle
        // If your SDK version requires EpicAccountId, we must skip this or use a dummy if not logged into Epic.
        if (_epicAccountId == null) return false; 

        CreatePresenceModificationOptions mOpts = new CreatePresenceModificationOptions { LocalUserId = _epicAccountId };
        PresenceModification? mod; lock (_sdkLock) { _presence.CreatePresenceModification(ref mOpts, out mod); }
        if (mod == null) return false;
        try {
            PresenceModificationSetStatusOptions sOpts = new PresenceModificationSetStatusOptions { Status = Status.Online };
            mod.SetStatus(ref sOpts);
            PresenceModificationSetDataOptions dOpts = new PresenceModificationSetDataOptions { Records = new[] { 
                new DataRecord { Key = PresenceKeyHosting, Value = hosting.ToString().ToLowerInvariant() }, 
                new DataRecord { Key = PresenceKeyWorld, Value = world ?? "" } 
            } };
            mod.SetData(ref dOpts);
            SetPresenceOptions uOpts = new SetPresenceOptions { LocalUserId = _epicAccountId, PresenceModificationHandle = mod };
            var tcs = new TaskCompletionSource<bool>();
            lock (_sdkLock) _presence.SetPresence(ref uOpts, null, (ref SetPresenceCallbackInfo info) => tcs.TrySetResult(info.ResultCode == Result.Success));
            return await tcs.Task;
        }
        finally { }
#else
        return false;
#endif
    }

    private void ConfigureRelayControl() { if (_p2p == null) return; SetRelayControlOptions opts = new SetRelayControlOptions { RelayControl = RelayControl.NoRelays }; lock (_sdkLock) _p2p.SetRelayControl(ref opts); }

    public Task<(bool Ok, string? Error)> LogoutAsync() { _localUserId = null; return Task.FromResult((true, (string?)null)); }
    public Task<(bool Ok, string? Error)> DeletePersistentAuthAsync() { return Task.FromResult((false, (string?)"Not implemented")); }

    public void Dispose() { lock (_sdkLock) { if (!_disposed) { _platform?.Release(); _platform = null; _disposed = true; } } }
}
