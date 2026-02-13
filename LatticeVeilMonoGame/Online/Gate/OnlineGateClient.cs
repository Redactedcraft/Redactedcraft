using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LatticeVeilMonoGame.Core;
using LatticeVeilMonoGame.Online.Eos;

namespace LatticeVeilMonoGame.Online.Gate;

public sealed class OnlineGateClient
{
    private static readonly object Sync = new();
    private static readonly HttpClient Http = CreateHttpClient();
    private static OnlineGateClient? _instance;
    private const string DefaultGateUrl = "https://eos-service.onrender.com";
    private static readonly TimeSpan DefaultTicketTimeout = TimeSpan.FromSeconds(20);

    private readonly string _gateUrl;
    private readonly string _allowlistUrl;
    private readonly bool _gateRequired;
    private readonly string _proofPath;
    private readonly string _buildFlavor;
    private readonly string _devOnlineKey;
    private readonly string _allowlistMode;

    private DateTime _ticketExpiresUtc = DateTime.MinValue;
    private string _ticket = string.Empty;
    private string _ticketProductUserId = string.Empty;
    private string _status = "UNVERIFIED";
    private string _denialReason = string.Empty;
    private string _lastComputedHash = string.Empty;
    private string _lastHashPath = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private OnlineGateClient()
    {
        _gateUrl = ResolveGateUrl();
        _allowlistUrl = ResolveAllowlistUrl();
        var requiredValue = Environment.GetEnvironmentVariable("LV_GATE_REQUIRED");
        _gateRequired = string.IsNullOrWhiteSpace(requiredValue) || ParseBool(requiredValue);
        _proofPath = ResolveProofPath();
        _buildFlavor = ResolveBuildFlavor(_proofPath);
        _devOnlineKey = (Environment.GetEnvironmentVariable("LV_DEV_ONLINE_KEY") ?? string.Empty).Trim();
        _allowlistMode = (Environment.GetEnvironmentVariable("LV_ALLOWLIST_MODE") ?? string.Empty).Trim();
        TryRestorePreAuthorizedTicketFromEnvironment();
    }

    public static OnlineGateClient GetOrCreate()
    {
        lock (Sync)
        {
            _instance ??= new OnlineGateClient();
            return _instance;
        }
    }

    public bool IsGateRequired => _gateRequired;
    public bool IsGateConfigured => !string.IsNullOrWhiteSpace(_gateUrl) || !string.IsNullOrWhiteSpace(_allowlistUrl);
    public bool HasValidTicket => !string.IsNullOrWhiteSpace(_ticket) && DateTime.UtcNow < _ticketExpiresUtc;
    public string StatusText => _status;
    public string DenialReason => _denialReason;

    public bool TryGetValidTicketForChildProcess(out string ticket, out DateTime expiresUtc)
    {
        ticket = string.Empty;
        expiresUtc = DateTime.MinValue;

        if (!HasValidTicket)
            return false;

        ticket = _ticket;
        expiresUtc = _ticketExpiresUtc;
        return true;
    }

    public bool CanUseOfficialOnline(Logger log, out string denialMessage)
    {
        denialMessage = string.Empty;

        // Reuse an existing valid ticket to avoid transient network blips
        // causing false OFFLINE/LAN transitions mid-session.
        if (HasUsableTicketForCurrentIdentity())
            return true;

        if (!IsGateConfigured)
        {
            if (IsGateRequired)
            {
                _status = "DENIED";
                _denialReason = "No online verification source configured.";
                denialMessage = BuildOfficialOnlineDeniedMessage();
                return false;
            }

            _status = "BYPASS (NO GATE)";
            return true;
        }

        var ok = EnsureTicket(log);
        if (ok)
            return true;

        if (IsGateRequired)
        {
            if (string.IsNullOrWhiteSpace(_denialReason))
                _denialReason = "Gate verification did not complete.";
            log.Warn($"Official online denied: {_denialReason}");
            denialMessage = BuildOfficialOnlineDeniedMessage();
            return false;
        }

        // Optional gate mode in dev/community builds.
        return true;
    }

    public bool ValidatePeerTicket(string? ticket, Logger log, out string reason, TimeSpan? timeout = null)
    {
        reason = string.Empty;

        var trimmedTicket = (ticket ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedTicket))
        {
            reason = "Missing peer online gate ticket.";
            return false;
        }

        if (!IsGateConfigured)
        {
            if (IsGateRequired)
            {
                reason = "No online verification source configured.";
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(_gateUrl))
        {
            reason = "Gate URL missing.";
            return false;
        }

        var requiredChannel = _buildFlavor.Equals("dev", StringComparison.OrdinalIgnoreCase)
            ? "dev"
            : "release";

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(4));
        try
        {
            var result = ValidatePeerTicketAsync(trimmedTicket, requiredChannel, cts.Token)
                .GetAwaiter()
                .GetResult();
            reason = result.Reason;
            return result.Ok;
        }
        catch (Exception ex)
        {
            reason = $"Gate validation request failed: {ex.Message}";
            log.Warn(reason);
            return false;
        }
    }

    public async Task<GateIdentityClaimResult> ClaimUsernameAsync(string desiredUsername, string? localPuid, string? displayName, CancellationToken ct = default)
    {
        var username = (desiredUsername ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return new GateIdentityClaimResult
            {
                Ok = false,
                Code = "invalid_username",
                Message = "Username is required."
            };
        }

        var endpoint = BuildIdentityClaimUrl(_gateUrl);
        var request = new GateIdentityClaimRequest
        {
            ProductUserId = string.IsNullOrWhiteSpace(localPuid) ? null : localPuid.Trim(),
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim()
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateIdentityClaimRequest, GateIdentityClaimResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateIdentityClaimResult
            {
                Ok = false,
                Code = "request_failed",
                Message = string.IsNullOrWhiteSpace(error) ? "Identity claim failed." : error!
            };
        }

        return new GateIdentityClaimResult
        {
            Ok = response.Ok,
            Code = response.Code ?? (response.Ok ? "ok" : "failed"),
            Message = response.Message ?? (response.Ok ? "Username reserved." : "Username claim failed."),
            User = response.User == null ? null : MapIdentityUser(response.User),
            Rules = response.Rules == null ? null : new GateIdentityRules
            {
                MinLength = response.Rules.MinLength,
                MaxLength = response.Rules.MaxLength,
                Regex = response.Rules.Regex ?? string.Empty
            }
        };
    }

    public async Task<GateIdentityResolveResult> ResolveIdentityAsync(string query, CancellationToken ct = default)
    {
        var value = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new GateIdentityResolveResult
            {
                Found = false,
                Reason = "Query is required."
            };
        }

        var endpoint = BuildIdentityResolveUrl(_gateUrl);
        var request = new GateIdentityResolveRequest { Query = value };
        var (ok, response, error) = await PostAuthorizedAsync<GateIdentityResolveRequest, GateIdentityResolveResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateIdentityResolveResult
            {
                Found = false,
                Reason = string.IsNullOrWhiteSpace(error) ? "Identity resolve failed." : error!
            };
        }

        return new GateIdentityResolveResult
        {
            Found = response.Found,
            Reason = response.Reason ?? string.Empty,
            User = response.User == null ? null : MapIdentityUser(response.User)
        };
    }

    public async Task<GateIdentityMeResult> GetMyIdentityAsync(CancellationToken ct = default)
    {
        var endpoint = BuildIdentityMeUrl(_gateUrl);
        var (ok, response, error) = await GetAuthorizedAsync<GateIdentityMeResponse>(endpoint, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateIdentityMeResult
            {
                Ok = false,
                Found = false,
                Reason = string.IsNullOrWhiteSpace(error) ? "Identity lookup failed." : error!
            };
        }

        return new GateIdentityMeResult
        {
            Ok = response.Ok,
            Found = response.Found,
            Reason = response.Reason ?? string.Empty,
            User = response.User == null ? null : MapIdentityUser(response.User),
            Rules = response.Rules == null ? null : new GateIdentityRules
            {
                MinLength = response.Rules.MinLength,
                MaxLength = response.Rules.MaxLength,
                Regex = response.Rules.Regex ?? string.Empty
            }
        };
    }

    public async Task<GateFriendsResult> GetFriendsAsync(CancellationToken ct = default)
    {
        var endpoint = BuildFriendsMeUrl(_gateUrl);
        var (ok, response, error) = await GetAuthorizedAsync<GateFriendsResponse>(endpoint, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateFriendsResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Friends lookup failed." : error!,
                Friends = Array.Empty<GateIdentityUser>(),
                IncomingRequests = Array.Empty<GateFriendRequest>(),
                OutgoingRequests = Array.Empty<GateFriendRequest>(),
                BlockedUsers = Array.Empty<GateIdentityUser>()
            };
        }

        var users = new List<GateIdentityUser>();
        if (response.Friends != null)
        {
            for (var i = 0; i < response.Friends.Count; i++)
            {
                var item = response.Friends[i];
                if (item == null)
                    continue;
                users.Add(MapIdentityUser(item));
            }
        }

        var incoming = new List<GateFriendRequest>();
        if (response.IncomingRequests != null)
        {
            for (var i = 0; i < response.IncomingRequests.Count; i++)
            {
                var item = response.IncomingRequests[i];
                if (item == null)
                    continue;
                var mapped = MapFriendRequest(item);
                if (mapped != null)
                    incoming.Add(mapped);
            }
        }

        var outgoing = new List<GateFriendRequest>();
        if (response.OutgoingRequests != null)
        {
            for (var i = 0; i < response.OutgoingRequests.Count; i++)
            {
                var item = response.OutgoingRequests[i];
                if (item == null)
                    continue;
                var mapped = MapFriendRequest(item);
                if (mapped != null)
                    outgoing.Add(mapped);
            }
        }

        var blocked = new List<GateIdentityUser>();
        if (response.BlockedUsers != null)
        {
            for (var i = 0; i < response.BlockedUsers.Count; i++)
            {
                var item = response.BlockedUsers[i];
                if (item == null)
                    continue;
                blocked.Add(MapIdentityUser(item));
            }
        }

        return new GateFriendsResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            Friends = users,
            IncomingRequests = incoming,
            OutgoingRequests = outgoing,
            BlockedUsers = blocked
        };
    }

    public async Task<GateFriendMutationResult> AddFriendAsync(string query, CancellationToken ct = default)
    {
        var value = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = "Friend query is required."
            };
        }

        var endpoint = BuildFriendsAddUrl(_gateUrl);
        var request = new GateFriendAddRequest
        {
            Query = value
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateFriendAddRequest, GateFriendMutationResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Add friend failed." : error!
            };
        }

        return new GateFriendMutationResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            Friend = response.Friend == null ? null : MapIdentityUser(response.Friend),
            Count = response.Count
        };
    }

    public async Task<GateFriendMutationResult> RemoveFriendAsync(string productUserId, CancellationToken ct = default)
    {
        var value = (productUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = "Friend ProductUserId is required."
            };
        }

        var endpoint = BuildFriendsRemoveUrl(_gateUrl);
        var request = new GateFriendRemoveRequest
        {
            ProductUserId = value
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateFriendRemoveRequest, GateFriendMutationResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Remove friend failed." : error!
            };
        }

        return new GateFriendMutationResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            Friend = response.Friend == null ? null : MapIdentityUser(response.Friend),
            Count = response.Count
        };
    }

    public async Task<GateFriendMutationResult> RespondToFriendRequestAsync(
        string requesterProductUserId,
        bool accept,
        bool block = false,
        CancellationToken ct = default)
    {
        var value = (requesterProductUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = "Requester ProductUserId is required."
            };
        }

        var endpoint = BuildFriendsRespondUrl(_gateUrl);
        var request = new GateFriendRespondRequest
        {
            RequesterProductUserId = value,
            Accept = accept,
            Block = block
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateFriendRespondRequest, GateFriendMutationResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Friend request response failed." : error!
            };
        }

        return new GateFriendMutationResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            Friend = response.Friend == null ? null : MapIdentityUser(response.Friend),
            Count = response.Count
        };
    }

    public async Task<GateFriendMutationResult> BlockUserAsync(string queryOrProductUserId, CancellationToken ct = default)
    {
        var value = (queryOrProductUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = "Block query is required."
            };
        }

        var endpoint = BuildFriendsBlockUrl(_gateUrl);
        var request = new GateFriendBlockRequest
        {
            Query = value
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateFriendBlockRequest, GateFriendMutationResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Block user failed." : error!
            };
        }

        return new GateFriendMutationResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            Friend = response.Friend == null ? null : MapIdentityUser(response.Friend),
            Count = response.Count
        };
    }

    public async Task<GateFriendMutationResult> UnblockUserAsync(string productUserId, CancellationToken ct = default)
    {
        var value = (productUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = "Target ProductUserId is required."
            };
        }

        var endpoint = BuildFriendsUnblockUrl(_gateUrl);
        var request = new GateFriendUnblockRequest
        {
            ProductUserId = value
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateFriendUnblockRequest, GateFriendMutationResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateFriendMutationResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Unblock user failed." : error!
            };
        }

        return new GateFriendMutationResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            Friend = response.Friend == null ? null : MapIdentityUser(response.Friend),
            Count = response.Count
        };
    }

    public async Task<GateRecoveryCodeResult> RotateRecoveryCodeAsync(CancellationToken ct = default)
    {
        var endpoint = BuildIdentityRecoveryRotateUrl(_gateUrl);
        var request = new GateRecoveryRotateRequest();
        var (ok, response, error) = await PostAuthorizedAsync<GateRecoveryRotateRequest, GateRecoveryCodeResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateRecoveryCodeResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Recovery code rotation failed." : error!,
                RecoveryCode = string.Empty
            };
        }

        return new GateRecoveryCodeResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            RecoveryCode = response.RecoveryCode ?? string.Empty
        };
    }

    public async Task<GateIdentityTransferResult> TransferIdentityAsync(string query, string recoveryCode, CancellationToken ct = default)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        var normalizedCode = (recoveryCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new GateIdentityTransferResult
            {
                Ok = false,
                Message = "Identity query is required."
            };
        }

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return new GateIdentityTransferResult
            {
                Ok = false,
                Message = "Recovery code is required."
            };
        }

        var endpoint = BuildIdentityRecoveryTransferUrl(_gateUrl);
        var request = new GateIdentityTransferRequest
        {
            Query = normalizedQuery,
            RecoveryCode = normalizedCode
        };

        var (ok, response, error) = await PostAuthorizedAsync<GateIdentityTransferRequest, GateIdentityTransferResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GateIdentityTransferResult
            {
                Ok = false,
                Message = string.IsNullOrWhiteSpace(error) ? "Identity transfer failed." : error!
            };
        }

        return new GateIdentityTransferResult
        {
            Ok = response.Ok,
            Message = response.Message ?? string.Empty,
            User = response.User == null ? null : MapIdentityUser(response.User)
        };
    }

    public async Task<bool> UpsertPresenceAsync(
        string? productUserId,
        string? displayName,
        bool isHosting,
        string? worldName,
        string? joinTarget,
        string? status = null,
        CancellationToken ct = default)
    {
        var endpoint = BuildPresenceUpsertUrl(_gateUrl);
        var request = new GatePresenceUpsertRequest
        {
            ProductUserId = string.IsNullOrWhiteSpace(productUserId) ? null : productUserId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            IsHosting = isHosting,
            WorldName = string.IsNullOrWhiteSpace(worldName) ? null : worldName.Trim(),
            JoinTarget = string.IsNullOrWhiteSpace(joinTarget) ? null : joinTarget.Trim(),
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim()
        };

        var (ok, response, _) = await PostAuthorizedAsync<GatePresenceUpsertRequest, GatePresenceUpsertResponse>(endpoint, request, ct).ConfigureAwait(false);
        return ok && response?.Ok == true;
    }

    public async Task<GatePresenceQueryResult> QueryPresenceAsync(IReadOnlyCollection<string> friendIds, CancellationToken ct = default)
    {
        var ids = new List<string>();
        if (friendIds != null)
        {
            foreach (var id in friendIds)
            {
                var trimmed = (id ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    ids.Add(trimmed);
            }
        }

        var endpoint = BuildPresenceQueryUrl(_gateUrl);
        var request = new GatePresenceQueryRequest
        {
            ProductUserIds = ids
        };

        var (ok, response, error) = await PostAuthorizedAsync<GatePresenceQueryRequest, GatePresenceQueryResponse>(endpoint, request, ct).ConfigureAwait(false);
        if (!ok || response == null)
        {
            return new GatePresenceQueryResult
            {
                Ok = false,
                Reason = string.IsNullOrWhiteSpace(error) ? "Presence query failed." : error!,
                Entries = Array.Empty<GatePresenceEntry>()
            };
        }

        var mapped = new List<GatePresenceEntry>();
        if (response.Entries != null)
        {
            for (var i = 0; i < response.Entries.Count; i++)
            {
                var item = response.Entries[i];
                mapped.Add(new GatePresenceEntry
                {
                    ProductUserId = item.ProductUserId ?? string.Empty,
                    DisplayName = item.DisplayName ?? string.Empty,
                    Username = item.Username ?? string.Empty,
                    Status = item.Status ?? string.Empty,
                    IsHosting = item.IsHosting,
                    WorldName = item.WorldName ?? string.Empty,
                    JoinTarget = item.JoinTarget ?? string.Empty,
                    FriendCode = item.FriendCode ?? string.Empty,
                    UpdatedUtc = item.UpdatedUtc,
                    ExpiresUtc = item.ExpiresUtc
                });
            }
        }

        return new GatePresenceQueryResult
        {
            Ok = response.Ok,
            Reason = response.Reason ?? string.Empty,
            Entries = mapped
        };
    }

    private string BuildOfficialOnlineDeniedMessage()
    {
        var reason = string.IsNullOrWhiteSpace(_denialReason)
            ? "gate reason unavailable"
            : _denialReason;
        return $"Official online disabled (unverified build: {reason}). LAN still available.";
    }

    public bool EnsureTicket(Logger log, TimeSpan? timeout = null, string? hashTargetPath = null)
    {
        if (!IsGateConfigured)
        {
            if (IsGateRequired)
            {
                _status = "DENIED";
                _denialReason = "No online verification source configured.";
            }

            return !IsGateRequired;
        }

        // When allowlist mode is active, always re-check live source.
        // This prevents stale "verified" state if the repo allowlist changes.
        if (!string.IsNullOrWhiteSpace(_allowlistUrl))
            return EnsureAllowlistHash(log, hashTargetPath);

        if (HasUsableTicketForCurrentIdentity())
            return true;

        if (HasValidTicket)
        {
            // EOS ProductUserId became available or changed; request a fresh ticket bound to this identity.
            InvalidateTicket();
        }

        using var cts = new CancellationTokenSource(timeout ?? DefaultTicketTimeout);
        try
        {
            return EnsureTicketAsync(log, cts.Token, hashTargetPath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _status = "ERROR";
            _denialReason = ex.Message;
            log.Warn($"Online gate request failed (attempt 1): {ex.Message}");
        }

        // Render free services can cold-start slowly; retry once before failing hard.
        try
        {
            Thread.Sleep(750);
            using var retryCts = new CancellationTokenSource(timeout ?? DefaultTicketTimeout);
            return EnsureTicketAsync(log, retryCts.Token, hashTargetPath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _status = "ERROR";
            _denialReason = ex.Message;
            log.Warn($"Online gate request failed (attempt 2): {ex.Message}");
            return false;
        }
    }

    private void TryRestorePreAuthorizedTicketFromEnvironment()
    {
        try
        {
            var preAuthTicket = (Environment.GetEnvironmentVariable("LV_GATE_PREAUTH_TICKET") ?? string.Empty).Trim();
            var preAuthExpiry = (Environment.GetEnvironmentVariable("LV_GATE_PREAUTH_EXPIRES_UTC") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(preAuthTicket) || string.IsNullOrWhiteSpace(preAuthExpiry))
                return;

            if (!DateTimeOffset.TryParse(preAuthExpiry, out var parsedExpiry))
                return;

            var expiryUtc = parsedExpiry.UtcDateTime;
            if (expiryUtc <= DateTime.UtcNow)
                return;

            _ticket = preAuthTicket;
            _ticketExpiresUtc = expiryUtc;
            _ticketProductUserId = ExtractProductUserIdFromJwt(preAuthTicket);
            _status = "VERIFIED";
            _denialReason = string.Empty;
        }
        catch
        {
            // Best-effort only.
        }
    }

    private bool EnsureAllowlistHash(Logger log, string? hashTargetPath = null)
    {
        var exeHash = TryComputeExecutableHash(hashTargetPath, true, out var resolvedHashPath);
        if (string.IsNullOrWhiteSpace(exeHash))
        {
            _status = "DENIED";
            InvalidateTicket();
            _denialReason = string.IsNullOrWhiteSpace(resolvedHashPath)
                ? "Could not compute executable SHA256."
                : $"Could not compute executable SHA256 for: {resolvedHashPath}";
            log.Warn("Online allowlist check failed: executable hash unavailable.");
            return false;
        }
        _lastComputedHash = exeHash;
        _lastHashPath = resolvedHashPath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_allowlistUrl))
        {
            _status = "DENIED";
            InvalidateTicket();
            _denialReason = "Allowlist URL missing.";
            return false;
        }

        try
        {
            var raw = FetchAllowlistJson(_allowlistUrl);
            var parsed = JsonSerializer.Deserialize<AllowlistDocument>(raw, JsonOptions);
            if (parsed == null)
            {
                _status = "DENIED";
                InvalidateTicket();
                _denialReason = "Allowlist parse failed.";
                return false;
            }

            var allHashes = NormalizeHashes(parsed.AllowedClientExeSha256);
            var devHashes = NormalizeHashes(parsed.AllowedDevExeSha256);
            var releaseHashes = NormalizeHashes(parsed.AllowedReleaseExeSha256);
            var mergedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            mergedHashes.UnionWith(allHashes);
            mergedHashes.UnionWith(devHashes);
            mergedHashes.UnionWith(releaseHashes);
            if (mergedHashes.Count == 0)
            {
                _status = "DENIED";
                InvalidateTicket();
                _denialReason =
                    $"Remote allowlist is empty. source={_allowlistUrl}, all={allHashes.Count}, dev={devHashes.Count}, release={releaseHashes.Count}";
                log.Warn($"Online allowlist denied: no hashes found. source={_allowlistUrl}, all={allHashes.Count}, dev={devHashes.Count}, release={releaseHashes.Count}");
                return false;
            }

            var isDevBuild = _buildFlavor.Equals("dev", StringComparison.OrdinalIgnoreCase);
            var hasFlavorScopedLists = devHashes.Count > 0 || releaseHashes.Count > 0;
            var useFlavorMode = _allowlistMode.Equals("flavor", StringComparison.OrdinalIgnoreCase);
            var allowed = useFlavorMode && hasFlavorScopedLists
                ? (isDevBuild ? devHashes.Contains(exeHash) : releaseHashes.Contains(exeHash))
                : mergedHashes.Contains(exeHash);

            if (!allowed)
            {
                _status = "DENIED";
                InvalidateTicket();
                if (useFlavorMode && hasFlavorScopedLists)
                {
                    _denialReason = isDevBuild
                        ? $"Build hash not found in allowedDevExeSha256. Hash={exeHash}"
                        : $"Build hash not found in allowedReleaseExeSha256. Hash={exeHash}";
                }
                else
                {
                    _denialReason = $"Build hash not found in remote allowlist. Hash={exeHash}";
                }

                log.Warn(
                    $"Online allowlist denied current build hash. hash={exeHash}, source={_allowlistUrl}, " +
                    $"all={allHashes.Count}, dev={devHashes.Count}, release={releaseHashes.Count}, mode={_allowlistMode}");
                return false;
            }

            _ticket = "allowlist:" + exeHash;
            _ticketExpiresUtc = DateTime.UtcNow.AddMinutes(3);
            _ticketProductUserId = string.Empty;
            _status = "VERIFIED";
            _denialReason = string.Empty;
            log.Info(
                $"Online allowlist verified from remote source. hash={exeHash}, source={_allowlistUrl}, " +
                $"mode={(useFlavorMode ? "flavor" : "merged")}");
            return true;
        }
        catch (Exception ex)
        {
            _status = "ERROR";
            InvalidateTicket();
            _denialReason = $"Allowlist fetch failed: {ex.Message}";
            log.Warn(_denialReason);
            return false;
        }
    }

    private static HashSet<string> NormalizeHashes(string[]? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values == null)
            return set;

        for (var i = 0; i < values.Length; i++)
        {
            var value = (values[i] ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(value))
                set.Add(value);
        }

        return set;
    }

    public async Task<bool> EnsureTicketAsync(Logger log, CancellationToken ct = default, string? hashTargetPath = null)
    {
        if (HasUsableTicketForCurrentIdentity())
            return true;

        if (HasValidTicket)
            InvalidateTicket();

        if (!IsGateConfigured)
            return !IsGateRequired;

        var request = BuildTicketRequest(hashTargetPath);
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var endpoint = BuildTicketUrl(_gateUrl);
        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _status = "DENIED";
            _denialReason = $"Gate HTTP {(int)response.StatusCode}";
            log.Warn($"Online gate denied with HTTP {(int)response.StatusCode}.");
            return false;
        }

        var parsed = JsonSerializer.Deserialize<GateTicketResponse>(body, JsonOptions);
        if (parsed == null)
        {
            _status = "DENIED";
            _denialReason = "Empty gate response.";
            return false;
        }

        if (!parsed.Ok)
        {
            _status = "DENIED";
            _denialReason = string.IsNullOrWhiteSpace(parsed.Reason) ? "Gate denied request." : parsed.Reason!;
            log.Warn($"Online gate denied: {_denialReason}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Ticket)
            || !DateTimeOffset.TryParse(parsed.ExpiresUtc, out var expiresUtc))
        {
            _status = "DENIED";
            _denialReason = "Gate response missing ticket metadata.";
            return false;
        }

        _ticket = parsed.Ticket.Trim();
        _ticketExpiresUtc = expiresUtc.UtcDateTime;
        _ticketProductUserId = string.IsNullOrWhiteSpace(request.ProductUserId)
            ? ExtractProductUserIdFromJwt(_ticket)
            : request.ProductUserId.Trim();
        _status = "VERIFIED";
        _denialReason = string.Empty;
        log.Info($"Online gate ticket acquired; expires {_ticketExpiresUtc:u}.");
        return true;
    }

    private bool HasUsableTicketForCurrentIdentity()
    {
        if (!HasValidTicket)
            return false;

        var currentProductUserId = GetCurrentProductUserId();
        if (string.IsNullOrWhiteSpace(currentProductUserId))
            return true;

        return string.Equals(_ticketProductUserId, currentProductUserId, StringComparison.Ordinal);
    }

    private static string GetCurrentProductUserId()
    {
        var eosClient = EosClientProvider.Current;
        return (eosClient?.LocalProductUserId ?? string.Empty).Trim();
    }

    private void InvalidateTicket()
    {
        _ticket = string.Empty;
        _ticketExpiresUtc = DateTime.MinValue;
        _ticketProductUserId = string.Empty;
    }

    private async Task<(bool Ok, string Reason)> ValidatePeerTicketAsync(
        string ticket,
        string requiredChannel,
        CancellationToken ct)
    {
        var request = new GateTicketValidateRequest
        {
            Ticket = ticket,
            RequiredChannel = requiredChannel
        };

        var endpoint = BuildTicketValidateUrl(_gateUrl);
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return (false, $"Gate validate HTTP {(int)response.StatusCode}.");
        }

        var parsed = JsonSerializer.Deserialize<GateTicketValidateResponse>(body, JsonOptions);
        if (parsed == null)
        {
            return (false, "Empty gate validate response.");
        }

        if (!parsed.Ok)
        {
            var denialReason = string.IsNullOrWhiteSpace(parsed.Reason)
                ? "Peer gate ticket denied."
                : parsed.Reason!;
            return (false, denialReason);
        }

        if (!string.IsNullOrWhiteSpace(parsed.Channel)
            && !parsed.Channel!.Equals(requiredChannel, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Peer ticket channel '{parsed.Channel}' does not match required '{requiredChannel}'.");
        }

        return (true, string.Empty);
    }

    private GateTicketRequest BuildTicketRequest(string? hashTargetPath = null)
    {
        var proofBytes = TryReadProof(_proofPath);
        var proofBase64 = proofBytes != null && proofBytes.Length > 0
            ? Convert.ToBase64String(proofBytes)
            : null;

        var exeHash = TryComputeExecutableHash(hashTargetPath, false, out _);
        EosConfig.TryGetPublicIdentifiers(out var sandboxId, out var deploymentId);
        var eosClient = EosClientProvider.Current;
        var productUserId = (eosClient?.LocalProductUserId ?? string.Empty).Trim();
        var displayName = (eosClient?.EpicDisplayName ?? string.Empty).Trim();

        return new GateTicketRequest
        {
            GameVersion = GetGameVersion(),
            Platform = GetPlatform(),
            BuildFlavor = _buildFlavor,
            Proof = proofBase64,
            ExeHash = exeHash,
            DevKey = string.IsNullOrWhiteSpace(_devOnlineKey) ? null : _devOnlineKey,
            ProductUserId = string.IsNullOrWhiteSpace(productUserId) ? null : productUserId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            PublicConfigIds = new PublicConfigIds
            {
                SandboxId = sandboxId,
                DeploymentId = deploymentId
            }
        };
    }

    private async Task<(bool Ok, TResponse? Response, string? Error)> PostAuthorizedAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct)
    {
        if (!HasValidTicket)
        {
            return (false, default, "Gate ticket missing or expired.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ticket);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (false, default, $"Gate HTTP {(int)response.StatusCode}");

        var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        return parsed == null
            ? (false, default, "Gate response parse failed.")
            : (true, parsed, null);
    }

    private async Task<(bool Ok, TResponse? Response, string? Error)> GetAuthorizedAsync<TResponse>(string endpoint, CancellationToken ct)
    {
        if (!HasValidTicket)
        {
            return (false, default, "Gate ticket missing or expired.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ticket);

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (false, default, $"Gate HTTP {(int)response.StatusCode}");

        var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        return parsed == null
            ? (false, default, "Gate response parse failed.")
            : (true, parsed, null);
    }

    private static GateIdentityUser MapIdentityUser(GateIdentityUserResponse source)
    {
        return new GateIdentityUser
        {
            ProductUserId = source.ProductUserId ?? string.Empty,
            Username = source.Username ?? string.Empty,
            DisplayName = source.DisplayName ?? string.Empty,
            FriendCode = source.FriendCode ?? string.Empty,
            UpdatedUtc = source.UpdatedUtc
        };
    }

    private static GateFriendRequest? MapFriendRequest(GateFriendRequestResponse source)
    {
        if (source.User == null)
            return null;

        var user = MapIdentityUser(source.User);
        var productUserId = (source.ProductUserId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(productUserId))
            user.ProductUserId = productUserId;

        return new GateFriendRequest
        {
            ProductUserId = user.ProductUserId,
            RequestedUtc = source.RequestedUtc,
            User = user
        };
    }

    private static string ResolveGateUrl()
    {
        var configured = (Environment.GetEnvironmentVariable("LV_GATE_URL") ?? string.Empty).Trim();
        if (configured.Equals("off", StringComparison.OrdinalIgnoreCase)
            || configured.Equals("none", StringComparison.OrdinalIgnoreCase)
            || configured.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var defaultConfigured = (Environment.GetEnvironmentVariable("LV_GATE_DEFAULT_URL") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(defaultConfigured))
            return defaultConfigured;

        return DefaultGateUrl;
    }

    private static string ResolveAllowlistUrl()
    {
        var configured = (Environment.GetEnvironmentVariable("LV_ALLOWLIST_URL") ?? string.Empty).Trim();
        if (configured.Equals("off", StringComparison.OrdinalIgnoreCase)
            || configured.Equals("none", StringComparison.OrdinalIgnoreCase)
            || configured.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        // No default external allowlist source. Gate URL is the primary path.
        return string.Empty;
    }

    private static string ResolveProofPath()
    {
        var path = (Environment.GetEnvironmentVariable("LV_OFFICIAL_PROOF_PATH") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(path))
            return path;
        return Path.Combine(AppContext.BaseDirectory, "official_build.sig");
    }

    private static string ResolveBuildFlavor(string proofPath)
    {
        var configured = (Environment.GetEnvironmentVariable("LV_BUILD_FLAVOR") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

#if DEBUG
        return "dev";
#else
        return File.Exists(proofPath) ? "official" : "community";
#endif
    }

    private static string GetGameVersion()
    {
        return typeof(OnlineGateClient).Assembly.GetName().Version?.ToString() ?? "dev";
    }

    private static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macos";
        return RuntimeInformation.OSDescription;
    }

    private static byte[]? TryReadProof(string proofPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(proofPath) || !File.Exists(proofPath))
                return null;
            return File.ReadAllBytes(proofPath);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryComputeExecutableHash(string? hashTargetPath, bool strictTargetPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        try
        {
            var filePath = (hashTargetPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                if (!File.Exists(filePath))
                {
                    resolvedPath = filePath;
                    return null;
                }
            }
            else
            {
                filePath = (Environment.GetEnvironmentVariable("LV_GATE_HASH_TARGET") ?? string.Empty).Trim();
                if (strictTargetPath && string.IsNullOrWhiteSpace(filePath))
                    return null;
                if (string.IsNullOrWhiteSpace(filePath))
                    filePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (IsDotNetHostPath(filePath))
                {
                    var appExePath = TryResolveAppExecutablePath();
                    if (!string.IsNullOrWhiteSpace(appExePath))
                        filePath = appExePath;
                }
                if (string.IsNullOrWhiteSpace(filePath))
                    filePath = Environment.ProcessPath ?? string.Empty;
                if (IsDotNetHostPath(filePath))
                {
                    var appExePath = TryResolveAppExecutablePath();
                    if (!string.IsNullOrWhiteSpace(appExePath))
                        filePath = appExePath;
                }
            }

            resolvedPath = filePath;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            using var stream = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDotNetHostPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fileName = Path.GetFileName(path);
        return fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolveAppExecutablePath()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                return null;

            var entryName = typeof(OnlineGateClient).Assembly.GetName().Name ?? "LatticeVeilMonoGame";
            var candidates = new[]
            {
                Path.Combine(baseDir, $"{entryName}.exe"),
                Path.Combine(baseDir, "LatticeVeilMonoGame.exe")
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Resolve best-effort only.
        }

        return null;
    }

    private static string BuildTicketUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/ticket";
    }

    private static string BuildTicketValidateUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/ticket/validate";
    }

    private static string BuildAllowlistUrl(string allowlistUrl)
    {
        var separator = allowlistUrl.Contains('?') ? "&" : "?";
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        return $"{allowlistUrl}{separator}t={nonce}";
    }

    private static string BuildIdentityClaimUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/identity/claim";
    }

    private static string BuildIdentityResolveUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/identity/resolve";
    }

    private static string BuildIdentityMeUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/identity/me";
    }

    private static string BuildFriendsMeUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/friends/me";
    }

    private static string BuildFriendsAddUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/friends/add";
    }

    private static string BuildFriendsRemoveUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/friends/remove";
    }

    private static string BuildFriendsRespondUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/friends/respond";
    }

    private static string BuildFriendsBlockUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/friends/block";
    }

    private static string BuildFriendsUnblockUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/friends/unblock";
    }

    private static string BuildIdentityRecoveryRotateUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/identity/recovery/rotate";
    }

    private static string BuildIdentityRecoveryTransferUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/identity/recovery/transfer";
    }

    private static string BuildPresenceUpsertUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/presence/upsert";
    }

    private static string BuildPresenceQueryUrl(string gateUrl)
    {
        var baseUrl = gateUrl.TrimEnd('/');
        return $"{baseUrl}/presence/query";
    }

    private static string ExtractProductUserIdFromJwt(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            return string.Empty;

        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return string.Empty;

        var payloadBytes = TryBase64UrlDecode(parts[1]);
        if (payloadBytes == null || payloadBytes.Length == 0)
            return string.Empty;

        try
        {
            using var json = JsonDocument.Parse(payloadBytes);
            if (TryReadStringProperty(json.RootElement, "puid", out var puid))
                return puid;
            if (TryReadStringProperty(json.RootElement, "productUserId", out puid))
                return puid;
            if (TryReadStringProperty(json.RootElement, "product_user_id", out puid))
                return puid;
        }
        catch
        {
            // Best effort.
        }

        return string.Empty;
    }

    private static bool TryReadStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
            return false;
        if (property.ValueKind != JsonValueKind.String)
            return false;
        value = (property.GetString() ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static byte[]? TryBase64UrlDecode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder == 2)
            normalized += "==";
        else if (remainder == 3)
            normalized += "=";
        else if (remainder != 0)
            return null;

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch
        {
            return null;
        }
    }

    private static string FetchAllowlistJson(string allowlistUrl)
    {
        if (TryBuildGitHubContentsApiUrl(allowlistUrl, out var apiUrl))
        {
            try
            {
                using var apiRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                apiRequest.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.Zero
                };
                apiRequest.Headers.Pragma.ParseAdd("no-cache");
                apiRequest.Headers.UserAgent.ParseAdd("LatticeVeilLauncher/1.0");
                apiRequest.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var apiResponse = Http.Send(apiRequest);
                apiResponse.EnsureSuccessStatusCode();
                var apiBody = apiResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var contents = JsonSerializer.Deserialize<GitHubContentsResponse>(apiBody, JsonOptions);
                if (contents != null
                    && !string.IsNullOrWhiteSpace(contents.Content)
                    && string.Equals(contents.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
                {
                    var base64 = contents.Content.Replace("\n", string.Empty).Replace("\r", string.Empty).Trim();
                    var bytes = Convert.FromBase64String(base64);
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch
            {
                // Fall back to raw URL if API fetch or decode fails.
            }
        }

        var requestUrl = BuildAllowlistUrl(allowlistUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MaxAge = TimeSpan.Zero
        };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.UserAgent.ParseAdd("LatticeVeilLauncher/1.0");
        using var response = Http.Send(request);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static bool TryBuildGitHubContentsApiUrl(string allowlistUrl, out string apiUrl)
    {
        apiUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(allowlistUrl))
            return false;

        if (!Uri.TryCreate(allowlistUrl, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 4)
            return false;

        var owner = parts[0];
        var repo = parts[1];
        var branch = parts[2];
        var path = string.Join("/", parts.Skip(3).Select(Uri.EscapeDataString));
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}&t={nonce}";
        return true;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        return trimmed == "1"
            || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private sealed class GateTicketRequest
    {
        public string GameVersion { get; set; } = "dev";
        public string Platform { get; set; } = "windows";
        public string BuildFlavor { get; set; } = "community";
        public string? Proof { get; set; }
        public string? ExeHash { get; set; }
        public string? DevKey { get; set; }
        public string? ProductUserId { get; set; }
        public string? DisplayName { get; set; }
        public PublicConfigIds PublicConfigIds { get; set; } = new();
    }

    private sealed class PublicConfigIds
    {
        public string SandboxId { get; set; } = string.Empty;
        public string DeploymentId { get; set; } = string.Empty;
    }

    private sealed class GateTicketResponse
    {
        public bool Ok { get; set; }
        public string? Ticket { get; set; }
        public string? ExpiresUtc { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class GateTicketValidateRequest
    {
        public string Ticket { get; set; } = string.Empty;
        public string RequiredChannel { get; set; } = "release";
    }

    private sealed class GateTicketValidateResponse
    {
        public bool Ok { get; set; }
        public string? Reason { get; set; }
        public string? Channel { get; set; }
        public string? ExpiresUtc { get; set; }
    }

    private sealed class AllowlistDocument
    {
        public string[] AllowedClientExeSha256 { get; set; } = Array.Empty<string>();
        public string[] AllowedDevExeSha256 { get; set; } = Array.Empty<string>();
        public string[] AllowedReleaseExeSha256 { get; set; } = Array.Empty<string>();
    }

    private sealed class GitHubContentsResponse
    {
        public string? Content { get; set; }
        public string? Encoding { get; set; }
    }

    private sealed class GateIdentityClaimRequest
    {
        public string? ProductUserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
    }

    private sealed class GateIdentityResolveRequest
    {
        public string Query { get; set; } = string.Empty;
    }

    private sealed class GateFriendAddRequest
    {
        public string Query { get; set; } = string.Empty;
    }

    private sealed class GateFriendRemoveRequest
    {
        public string ProductUserId { get; set; } = string.Empty;
    }

    private sealed class GateFriendRespondRequest
    {
        public string RequesterProductUserId { get; set; } = string.Empty;
        public bool Accept { get; set; }
        public bool Block { get; set; }
    }

    private sealed class GateFriendBlockRequest
    {
        public string? Query { get; set; }
        public string? ProductUserId { get; set; }
    }

    private sealed class GateFriendUnblockRequest
    {
        public string ProductUserId { get; set; } = string.Empty;
    }

    private sealed class GateRecoveryRotateRequest
    {
    }

    private sealed class GateIdentityTransferRequest
    {
        public string Query { get; set; } = string.Empty;
        public string RecoveryCode { get; set; } = string.Empty;
    }

    private sealed class GatePresenceUpsertRequest
    {
        public string? ProductUserId { get; set; }
        public string? DisplayName { get; set; }
        public string? Status { get; set; }
        public bool IsHosting { get; set; }
        public string? WorldName { get; set; }
        public string? JoinTarget { get; set; }
    }

    private sealed class GatePresenceQueryRequest
    {
        public List<string> ProductUserIds { get; set; } = new();
    }

    private sealed class GateIdentityClaimResponse
    {
        public bool Ok { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
        public GateIdentityUserResponse? User { get; set; }
        public GateIdentityRulesResponse? Rules { get; set; }
    }

    private sealed class GateIdentityResolveResponse
    {
        public bool Ok { get; set; }
        public bool Found { get; set; }
        public string? Reason { get; set; }
        public GateIdentityUserResponse? User { get; set; }
    }

    private sealed class GateIdentityMeResponse
    {
        public bool Ok { get; set; }
        public bool Found { get; set; }
        public string? Reason { get; set; }
        public GateIdentityUserResponse? User { get; set; }
        public GateIdentityRulesResponse? Rules { get; set; }
    }

    private sealed class GateFriendsResponse
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public List<GateIdentityUserResponse>? Friends { get; set; }
        public List<GateFriendRequestResponse>? IncomingRequests { get; set; }
        public List<GateFriendRequestResponse>? OutgoingRequests { get; set; }
        public List<GateIdentityUserResponse>? BlockedUsers { get; set; }
    }

    private sealed class GateFriendMutationResponse
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public GateIdentityUserResponse? Friend { get; set; }
        public int Count { get; set; }
    }

    private sealed class GateFriendRequestResponse
    {
        public string? ProductUserId { get; set; }
        public GateIdentityUserResponse? User { get; set; }
        public DateTime RequestedUtc { get; set; }
    }

    private sealed class GateRecoveryCodeResponse
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public string? RecoveryCode { get; set; }
    }

    private sealed class GateIdentityTransferResponse
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public GateIdentityUserResponse? User { get; set; }
    }

    private sealed class GateIdentityUserResponse
    {
        public string? ProductUserId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? FriendCode { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class GateIdentityRulesResponse
    {
        public int MinLength { get; set; }
        public int MaxLength { get; set; }
        public string? Regex { get; set; }
    }

    private sealed class GatePresenceUpsertResponse
    {
        public bool Ok { get; set; }
        public string? Reason { get; set; }
        public GatePresenceEntryResponse? Presence { get; set; }
    }

    private sealed class GatePresenceQueryResponse
    {
        public bool Ok { get; set; }
        public string? Reason { get; set; }
        public List<GatePresenceEntryResponse>? Entries { get; set; }
    }

    private sealed class GatePresenceEntryResponse
    {
        public string? ProductUserId { get; set; }
        public string? DisplayName { get; set; }
        public string? Username { get; set; }
        public string? Status { get; set; }
        public bool IsHosting { get; set; }
        public string? WorldName { get; set; }
        public string? JoinTarget { get; set; }
        public string? FriendCode { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }
}

public sealed class GateIdentityClaimResult
{
    public bool Ok { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public GateIdentityUser? User { get; set; }
    public GateIdentityRules? Rules { get; set; }
}

public sealed class GateIdentityResolveResult
{
    public bool Found { get; set; }
    public string Reason { get; set; } = string.Empty;
    public GateIdentityUser? User { get; set; }
}

public sealed class GateIdentityMeResult
{
    public bool Ok { get; set; }
    public bool Found { get; set; }
    public string Reason { get; set; } = string.Empty;
    public GateIdentityUser? User { get; set; }
    public GateIdentityRules? Rules { get; set; }
}

public sealed class GateIdentityUser
{
    public string ProductUserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FriendCode { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
}

public sealed class GateIdentityRules
{
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public string Regex { get; set; } = string.Empty;
}

public sealed class GatePresenceQueryResult
{
    public bool Ok { get; set; }
    public string Reason { get; set; } = string.Empty;
    public IReadOnlyList<GatePresenceEntry> Entries { get; set; } = Array.Empty<GatePresenceEntry>();
}

public sealed class GatePresenceEntry
{
    public string ProductUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsHosting { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public string JoinTarget { get; set; } = string.Empty;
    public string FriendCode { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
}

public sealed class GateFriendsResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<GateIdentityUser> Friends { get; set; } = Array.Empty<GateIdentityUser>();
    public IReadOnlyList<GateFriendRequest> IncomingRequests { get; set; } = Array.Empty<GateFriendRequest>();
    public IReadOnlyList<GateFriendRequest> OutgoingRequests { get; set; } = Array.Empty<GateFriendRequest>();
    public IReadOnlyList<GateIdentityUser> BlockedUsers { get; set; } = Array.Empty<GateIdentityUser>();
}

public sealed class GateFriendRequest
{
    public string ProductUserId { get; set; } = string.Empty;
    public DateTime RequestedUtc { get; set; }
    public GateIdentityUser User { get; set; } = new();
}

public sealed class GateFriendMutationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public GateIdentityUser? Friend { get; set; }
    public int Count { get; set; }
}

public sealed class GateRecoveryCodeResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
}

public sealed class GateIdentityTransferResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public GateIdentityUser? User { get; set; }
}
