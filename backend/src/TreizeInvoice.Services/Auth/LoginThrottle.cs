using System.Collections.Concurrent;

namespace TreizeInvoice.Services.Auth;

/// <summary>
/// Limite les tentatives de connexion. Sans cela, un formulaire exposé sur
/// Internet accepte un nombre illimité d'essais de mot de passe : sur une
/// application mono-utilisateur, le nom d'utilisateur est connu d'avance et
/// seul le mot de passe protège l'ensemble de la comptabilité.
///
/// État en mémoire : l'application tourne en instance unique, et un redémarrage
/// qui remet les compteurs à zéro reste sans intérêt pour un attaquant.
/// </summary>
public class LoginThrottle
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset? LockedUntil)> _state =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Durée de blocage restante, ou null si la tentative est permise.</summary>
    public TimeSpan? RemainingLockout(string username)
    {
        if (!_state.TryGetValue(Key(username), out var entry) || entry.LockedUntil is null)
            return null;

        var remaining = entry.LockedUntil.Value - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RegisterFailure(string username)
    {
        var key = Key(username);
        _state.AddOrUpdate(
            key,
            _ => (1, null),
            (_, current) =>
            {
                var failures = current.Failures + 1;
                return failures >= MaxAttempts
                    ? (0, DateTimeOffset.UtcNow.Add(LockDuration))
                    : (failures, null);
            });
    }

    public void RegisterSuccess(string username) => _state.TryRemove(Key(username), out _);

    private static string Key(string? username) => username?.Trim() ?? string.Empty;
}
