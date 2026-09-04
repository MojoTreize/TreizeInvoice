using TreizeInvoice.Domain.Enums;

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
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }

    /// <summary>Lien optionnel vers la facture à l'origine de la recette.</summary>
    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>Chemin d'une pièce jointe optionnelle (justificatif de dépense).</summary>
    public string? AttachmentPath { get; set; }
}
