using System.Security.Cryptography;

namespace TreizeInvoice.Services.Security;

/// <summary>
/// Hachage PBKDF2 (SHA-256, 100 000 itérations, sel de 16 octets).
/// Format stocké : {iterations}.{selBase64}.{hashBase64}.
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        string[] parts = hash.Split('.', 3);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int iterations)) return false;

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expected = Convert.FromBase64String(parts[2]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
