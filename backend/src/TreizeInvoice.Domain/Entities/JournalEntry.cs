using TreizeInvoice.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace TreizeInvoice.Domain.Entities;

/// <summary>
/// Écriture du journal recettes/dépenses (base de l'EÜR).
/// Les recettes issues de factures payées y sont créées automatiquement.
/// </summary>
public class JournalEntry
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public JournalEntryType Type { get; set; }

    /// <summary>Montant toujours positif ; c'est <see cref="Type"/> qui donne le sens.</summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Le montant doit être supérieur à 0.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "La description est requise.")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Lien optionnel vers la facture à l'origine de la recette.</summary>
    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>Chemin d'une pièce jointe optionnelle (justificatif de dépense).</summary>
    public string? AttachmentPath { get; set; }

    /// <summary>Une écriture générée par une facture ne se modifie pas à la main (GoBD).</summary>
    public bool IsGenerated => InvoiceId is not null;

    /// <summary>Montant signé : positif pour une recette, négatif pour une dépense.</summary>
    public decimal SignedAmount => Type == JournalEntryType.Depense ? -Amount : Amount;
}
