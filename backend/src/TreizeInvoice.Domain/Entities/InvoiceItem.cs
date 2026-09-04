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

    [Required(ErrorMessage = "La description est requise.")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0.001, double.MaxValue, ErrorMessage = "La quantité doit être supérieure à 0.")]
    public decimal Quantity { get; set; } = 1;

    [Range(0.01, double.MaxValue, ErrorMessage = "Le prix unitaire doit être supérieur à 0.")]
    public decimal UnitPrice { get; set; }

    /// <summary>Total de la ligne (quantité × prix unitaire), arrondi à 2 décimales.</summary>
    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
