namespace TreizeInvoice.Domain.Entities;

/// <summary>
/// Utilisateur unique de l'application (MVP mono-utilisateur).
/// Le mot de passe n'est jamais stocké en clair, uniquement son hash.
/// </summary>
public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
