// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Extensions.Logging;

namespace Chippi.Audio;

/// <summary>
/// Decides who sets each default role and is the only caller of <see cref="IDeviceSystem.SetDefault"/>
/// (architecture §3). Routing and Processing each claim roles with a <see cref="RoleTarget"/>;
/// Processing wins while it claims, and a role nobody claims is left to Windows. Engine thread only.
/// </summary>
/// <remarks>
/// Every enforce compares the target with the default Windows reports now, so the notification
/// echo of our own write finds nothing to do and is never counted as someone else's change.
/// </remarks>
internal sealed class RoleArbiter(ILogger logger, TimeProvider time, TimeSpan? backoff = null)
{
    // Fights with other software that also enforces defaults: after this many repairs of one role
    // inside the window, stop repairing it for the back-off period (brief §2) and log it.
    internal const int MaxRepairs = 3;
    internal static readonly TimeSpan RepairWindow = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan BackoffPeriod = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _backoff = backoff ?? BackoffPeriod;

    private static readonly AudioRole[] AllRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];

    private sealed class OwnerClaim(RoleTarget target)
    {
        public RoleTarget Target { get; } = target;

        // Set until the first enforce after the claim, so that write is reported as Claim, not Switch.
        public bool Fresh { get; set; } = true;
    }

    private readonly Dictionary<RoleKey, Dictionary<RoleOwner, OwnerClaim>> _claims = [];
    private readonly Dictionary<RoleKey, (RoleOwner Owner, string Target)> _last = [];
    private readonly Dictionary<RoleKey, List<DateTimeOffset>> _repairs = [];
    private readonly Dictionary<RoleKey, DateTimeOffset> _backoffUntil = [];

    /// <summary>When the earliest back-off ends, or null. The engine reconciles then, so a role isn't left wrong until the next device event.</summary>
    public DateTimeOffset? BackoffEnds => _backoffUntil.Count == 0 ? null : _backoffUntil.Values.Min();

    public IReadOnlyDictionary<RoleKey, RoleOwner> Owners =>
        _claims.Where(c => c.Value.Count > 0).ToDictionary(c => c.Key, c => Effective(c.Value).Owner);

    /// <summary>
    /// <paramref name="owner"/> claims <paramref name="roles"/> of <paramref name="flow"/> and drops
    /// any other role of that flow it held, so turning "Switch call audio too" off frees Communications.
    /// </summary>
    public void Claim(RoleOwner owner, AudioFlow flow, IReadOnlyCollection<AudioRole> roles, RoleTarget target)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(owner, RoleOwner.None);
        foreach (var role in AllRoles)
        {
            var key = new RoleKey(flow, role);
            if (roles.Contains(role))
            {
                if (!_claims.TryGetValue(key, out var owners))
                    _claims[key] = owners = [];
                owners[owner] = new OwnerClaim(target);
                _repairs.Remove(key);
                _backoffUntil.Remove(key);
            }
            else
            {
                Drop(owner, key);
            }
        }
    }

    /// <summary>
    /// Drops every role <paramref name="owner"/> holds in <paramref name="flow"/>. Roles another owner
    /// still claims go to it on the next enforce; for roles nobody claims, <paramref name="restore"/>
    /// (what Processing replaced, architecture §3) is applied when that endpoint is active.
    /// </summary>
    public void Release(RoleOwner owner, AudioFlow flow, IReadOnlyDictionary<AudioRole, string>? restore, DeviceCatalog catalog, IDeviceSystem system)
    {
        foreach (var role in AllRoles)
        {
            var key = new RoleKey(flow, role);
            var held = Drop(owner, key);
            if (!held || _claims.ContainsKey(key) || restore is null || !restore.TryGetValue(role, out var id))
                continue;
            if (!catalog.Active(flow).Any(e => PriorityPolicy.Eq(e.Id, id)) || PriorityPolicy.Eq(system.GetDefault(flow, role), id))
                continue;
            Write(id, key, owner, "restore", system);
        }
    }

    /// <summary>Points every claimed role at its owner's target and reports what it changed.</summary>
    public IReadOnlyList<RoleChange> Enforce(DeviceCatalog catalog, IDeviceSystem system)
    {
        // Expire back-offs whether or not their role needs a repair now, so BackoffEnds is never in
        // the past (the engine schedules a reconcile for it).
        var now = time.GetUtcNow();
        foreach (var key in _backoffUntil.Where(b => b.Value <= now).Select(b => b.Key).ToList())
            _backoffUntil.Remove(key);

        var applied = new List<(RoleKey Key, string Target, RoleOwner Owner, RoleChangeKind Kind)>();
        foreach (var (key, owners) in _claims)
        {
            var (owner, claim) = Effective(owners);
            var fresh = claim.Fresh;
            claim.Fresh = false;

            var target = claim.Target(catalog.Active(key.Flow));
            if (target is null)
            {
                // Nothing listed is available: leave Windows' choice alone. Forgetting the last
                // target makes the winner's return a Switch rather than a Repair.
                _last.Remove(key);
                continue;
            }

            var hadLast = _last.TryGetValue(key, out var last);
            _last[key] = (owner, target);
            if (PriorityPolicy.Eq(catalog.Default(key), target))
                continue;

            var kind = fresh || (hadLast && last.Owner != owner) ? RoleChangeKind.Claim
                : !hadLast || !PriorityPolicy.Eq(last.Target, target) ? RoleChangeKind.Switch
                : RoleChangeKind.Repair;
            if (kind == RoleChangeKind.Repair && !AllowRepair(key))
                continue;

            if (Write(target, key, owner, kind.ToString(), system))
                applied.Add((key, target, owner, kind));
        }

        return applied
            .GroupBy(a => (a.Key.Flow, a.Target, a.Owner, a.Kind))
            .Select(g => new RoleChange(g.Key.Flow, g.Select(a => a.Key.Role).Order().ToList(), g.Key.Target, g.Key.Owner, g.Key.Kind))
            .ToList();
    }

    private bool Write(string id, RoleKey key, RoleOwner owner, string why, IDeviceSystem system)
    {
        try
        {
            system.SetDefault(id, key.Role);
            logger.LogInformation("Default {Flow} {Role} -> {Id} ({Owner}, {Why})", key.Flow, key.Role, id, owner, why);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Setting default {Flow} {Role} -> {Id} failed", key.Flow, key.Role, id);
            return false;
        }
    }

    private bool AllowRepair(RoleKey key)
    {
        var now = time.GetUtcNow();
        if (_backoffUntil.TryGetValue(key, out var until))
        {
            if (now < until)
                return false;
            _backoffUntil.Remove(key);
        }

        if (!_repairs.TryGetValue(key, out var times))
            _repairs[key] = times = [];
        times.RemoveAll(t => now - t > RepairWindow);
        if (times.Count >= MaxRepairs)
        {
            _backoffUntil[key] = now + _backoff;
            times.Clear();
            logger.LogWarning("Default {Flow} {Role} repaired {Count} times in {Window}s; something else keeps changing it. Not repairing for {Backoff}s",
                key.Flow, key.Role, MaxRepairs, RepairWindow.TotalSeconds, _backoff.TotalSeconds);
            return false;
        }
        times.Add(now);
        return true;
    }

    private bool Drop(RoleOwner owner, RoleKey key)
    {
        if (!_claims.TryGetValue(key, out var owners) || !owners.Remove(owner))
            return false;
        if (owners.Count == 0)
        {
            _claims.Remove(key);
            _last.Remove(key);
            _repairs.Remove(key);
            _backoffUntil.Remove(key);
        }
        return true;
    }

    private static (RoleOwner Owner, OwnerClaim Claim) Effective(Dictionary<RoleOwner, OwnerClaim> owners) =>
        owners.TryGetValue(RoleOwner.Processing, out var processing)
            ? (RoleOwner.Processing, processing)
            : (RoleOwner.Routing, owners[RoleOwner.Routing]);
}
