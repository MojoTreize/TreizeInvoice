namespace TreizeInvoice.Domain.Entities;

/// <summary>
/// Compteur de numérotation par année. Une ligne par année, incrémentée dans une
/// transaction : c'est ce qui garantit une séquence continue, sans trou ni doublon,
/// même si deux émissions sont lancées en même temps.
/// </summary>
public class InvoiceNumberSequence
{
    /// <summary>Année civile ; sert de clé primaire.</summary>
    public int Year { get; set; }

    /// <summary>Dernier compteur attribué pour cette année (0 = aucune facture émise).</summary>
    public int LastNumber { get; set; }
}
