namespace TreizeInvoice.Domain.Entities;

/// <summary>
/// Données de l'émettrice (moi). Une seule ligne en base, éditable dans Paramètres.
/// Ces informations sont reprises sur chaque facture (§14 UStG).
/// </summary>
public class BusinessProfile
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Adresse complète, multi-lignes.</summary>
    public string Address { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>Numéro fiscal (obligatoire sur la facture, §14 UStG).</summary>
    public string Steuernummer { get; set; } = string.Empty;

    public string Iban { get; set; } = string.Empty;
    public string Bic { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;

    /// <summary>Régime Kleinunternehmer §19 UStG : pas de TVA facturée.</summary>
    public bool IsKleinunternehmer { get; set; } = true;

    /// <summary>
    /// Gabarit du numéro de facture. Défaut : "{year}-{counter:0000}" → 2026-0001.
    /// </summary>
    public string NumberFormat { get; set; } = "{year}-{counter:0000}";
}
