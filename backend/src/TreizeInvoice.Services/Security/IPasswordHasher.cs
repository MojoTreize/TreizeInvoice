namespace TreizeInvoice.Services.Security;

/// <summary>
/// Hachage de mots de passe. Interface pour pouvoir changer d'algorithme
/// sans toucher au reste de l'application.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
