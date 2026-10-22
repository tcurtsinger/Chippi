// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>A module's handle for activation verbs: <c>Chippi.exe --organize "C:\Downloads"</c>, a toast click.</summary>
public interface IModuleActivation
{
    /// <summary>
    /// Handles <c>--<paramref name="verb"/> value...</c> from a later launch, the Explorer menu or a notice
    /// action. Handlers run on the UI thread. Removed when the module stops. A verb belongs to one module.
    /// </summary>
    void Register(string verb, Action<IReadOnlyList<string>> handler);
}

/// <summary>
/// Routes activation arguments to the module that registered the verb. Arguments look like
/// <c>["--organize", "C:\Downloads"]</c>: the first is <c>--verb</c>, the rest are its values. The
/// shell calls <see cref="TryRoute"/> on the UI thread; anything unrouted falls back to the flyout.
/// </summary>
public sealed class ActivationRouter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string ModuleId, Action<IReadOnlyList<string>> Handler)> _verbs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs the registered handler. False when there are no arguments, no verb, or nobody owns it.</summary>
    public bool TryRoute(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || Verb(arguments[0]) is not { } verb)
            return false;
        Action<IReadOnlyList<string>> handler;
        lock (_gate)
        {
            if (!_verbs.TryGetValue(verb, out var registration))
                return false;
            handler = registration.Handler;
        }
        handler([.. arguments.Skip(1)]);
        return true;
    }

    public bool IsRegistered(string verb)
    {
        lock (_gate)
            return _verbs.ContainsKey(verb);
    }

    internal void Register(string moduleId, string verb, Action<IReadOnlyList<string>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(verb) || verb.StartsWith('-') || verb.Any(char.IsWhiteSpace))
            throw new ArgumentException("A verb is a single word without dashes, e.g. \"organize\".", nameof(verb));
        lock (_gate)
        {
            if (_verbs.TryGetValue(verb, out var existing) && existing.ModuleId != moduleId)
                throw new InvalidOperationException($"The verb '{verb}' already belongs to {existing.ModuleId}.");
            _verbs[verb] = (moduleId, handler);
        }
    }

    /// <summary>Drops every verb a module registered. The registry calls this after the module stops.</summary>
    internal void RemoveModule(string moduleId)
    {
        lock (_gate)
        {
            foreach (var verb in _verbs.Where(v => v.Value.ModuleId == moduleId).Select(v => v.Key).ToList())
                _verbs.Remove(verb);
        }
    }

    private static string? Verb(string argument) =>
        argument.StartsWith("--", StringComparison.Ordinal) && argument.Length > 2 ? argument[2..] : null;

    internal sealed class ModuleActivation(ActivationRouter router, string moduleId) : IModuleActivation
    {
        public void Register(string verb, Action<IReadOnlyList<string>> handler) => router.Register(moduleId, verb, handler);
    }
}
