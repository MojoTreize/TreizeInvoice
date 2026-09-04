using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Security;

namespace TreizeInvoice.Services.Setup;

/// <summary>
/// Applique les migrations puis crée les données de départ au premier lancement :
/// l'utilisateur unique et le profil d'entreprise (une ligne vide, à compléter
/// dans Paramètres). Idempotent : ne recrée rien si les données existent déjà.
/// </summary>
public class DatabaseInitializer
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;

    public DatabaseInitializer(AppDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>
    /// Retourne le mot de passe généré si un utilisateur vient d'être créé sans
    /// mot de passe configuré, sinon null. Aucun identifiant n'est codé en dur.
    /// </summary>
    public async Task<string?> InitializeAsync(string username, string? configuredPassword)
    {
        await _db.Database.MigrateAsync();

        string? generatedPassword = null;

        if (!await _db.Users.AnyAsync())
        {
            var password = configuredPassword;
            if (string.IsNullOrWhiteSpace(password))
            {
                password = GeneratePassword();
                generatedPassword = password;
            }

            _db.Users.Add(new AppUser
            {
                Username = username,
                PasswordHash = _hasher.Hash(password),
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (!await _db.BusinessProfiles.AnyAsync())
        {
            _db.BusinessProfiles.Add(new BusinessProfile { IsKleinunternehmer = true });
        }

        await _db.SaveChangesAsync();
        return generatedPassword;
    }

    private static string GeneratePassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace("+", "").Replace("/", "").Replace("=", "");
}
