namespace TreizeInvoice.Domain.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Ligne de facture. Le total de ligne est arrondi commercialement à 2 décimales
/// (MidpointRounding.AwayFromZero). Montants en decimal, jamais double.
/// </summary>
public class InvoiceItem
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    [Required(ErrorMessage = "Bitte geben Sie eine Beschreibung an.")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0.001, double.MaxValue, ErrorMessage = "Die Menge muss größer als 0 sein.")]
    public decimal Quantity { get; set; } = 1;

    [Range(0.01, double.MaxValue, ErrorMessage = "Der Einzelpreis muss größer als 0 sein.")]
    public decimal UnitPrice { get; set; }

    /// <summary>Total de la ligne (quantité × prix unitaire), arrondi à 2 décimales.</summary>
    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
