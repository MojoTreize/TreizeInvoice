using System.Text.RegularExpressions;
using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Invoicing;

/// <summary>
/// Contrôles sur les coordonnées de l'émettrice.
///
/// Vérifier la simple présence des champs ne suffisait pas : une adresse réduite à
/// « Castroper Hellweg 5 », sans code postal ni ville, passait le contrôle alors que
/// §14 Abs. 4 Nr. 1 UStG exige l'adresse complète. Un IBAN tronqué passait aussi, et
/// la facture partait avec des coordonnées bancaires inutilisables.
/// </summary>
public static class ProfileValidation
{
    // Code postal allemand : cinq chiffres, suivis du nom de la commune.
    private static readonly Regex GermanPostcodeAndCity =
        new(@"\b\d{5}\b\s+\S", RegexOptions.Compiled);

    private static readonly Regex IbanShape =
        new(@"^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$", RegexOptions.Compiled);

    /// <summary>Longueur exacte de l'IBAN par pays, pour les pays courants ici.</summary>
    private static readonly Dictionary<string, int> IbanLengths = new()
    {
        ["DE"] = 22, ["AT"] = 20, ["CH"] = 21, ["FR"] = 27,
        ["BE"] = 16, ["NL"] = 18, ["LU"] = 20, ["IT"] = 27, ["ES"] = 24
    };

    /// <summary>Retourne les manques, en allemand, prêts à être affichés.</summary>
    public static List<string> Check(BusinessProfile profile)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.FullName))
            problems.Add("Name");

        if (string.IsNullOrWhiteSpace(profile.Address))
            problems.Add("Anschrift");
        else if (!GermanPostcodeAndCity.IsMatch(profile.Address))
            problems.Add("Anschrift ohne Postleitzahl und Ort");

        if (string.IsNullOrWhiteSpace(profile.Steuernummer))
            problems.Add("Steuernummer");

        if (string.IsNullOrWhiteSpace(profile.Iban))
            problems.Add("IBAN");
        else if (!IsPlausibleIban(profile.Iban))
            problems.Add("IBAN unvollständig oder fehlerhaft");

        return problems;
    }

    /// <summary>
    /// Forme et longueur, plus le report modulo 97 de la norme ISO 13616 : détecte
    /// une saisie tronquée ou un chiffre inversé, ce qu'une simple longueur laisse passer.
    /// </summary>
    public static bool IsPlausibleIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return false;

        var normalised = iban.Replace(" ", string.Empty).ToUpperInvariant();
        if (!IbanShape.IsMatch(normalised)) return false;

        var country = normalised[..2];
        if (IbanLengths.TryGetValue(country, out var expected) && normalised.Length != expected)
            return false;

        return Modulo97(normalised) == 1;
    }

    /// <summary>Met le pays en majuscules et retire les espaces, sans rien inventer.</summary>
    public static string NormaliseIban(string? iban) =>
        (iban ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static int Modulo97(string iban)
    {
        // Les quatre premiers caractères passent à la fin, puis chaque lettre
        // devient sa position dans l'alphabet + 9 (A = 10).
        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;

        foreach (var c in rearranged)
        {
            var value = char.IsDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value > 9
                ? (remainder * 100 + value) % 97
                : (remainder * 10 + value) % 97;
        }

        return remainder;
    }
}
