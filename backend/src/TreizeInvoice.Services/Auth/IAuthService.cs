using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Auth;

public interface IAuthService
{
    /// <summary>Retourne l'utilisateur si les identifiants sont valides, sinon null.</summary>
    Task<AppUser?> ValidateCredentialsAsync(string username, string password);

    /// <summary>Change le mot de passe. Retourne false si le mot de passe actuel est faux.</summary>
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
