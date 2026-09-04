using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Invoicing;

public interface IInvoiceService
{
    /// <summary>Liste des factures (client inclus), les plus récentes d'abord.</summary>
    Task<List<Invoice>> GetAllAsync();

    /// <summary>Charge une facture avec ses lignes et son client.</summary>
    Task<Invoice?> GetAsync(int id);

    Task<Invoice> CreateDraftAsync(Invoice draft);

    /// <summary>Met à jour un brouillon. Lève InvoiceLockedException si la facture est émise.</summary>
    Task UpdateDraftAsync(Invoice draft);

    /// <summary>Supprime un brouillon. Lève InvoiceLockedException si la facture est émise.</summary>
    Task DeleteDraftAsync(int id);

    /// <summary>Duplique une facture existante en nouveau brouillon (sans numéro ni dates d'émission).</summary>
    Task<Invoice> DuplicateAsDraftAsync(int id);

    /// <summary>
    /// Émet la facture : attribue le numéro de façon atomique, verrouille la facture,
    /// génère et archive le PDF allemand. Irréversible (GoBD).
    /// </summary>
    Task<Invoice> IssueAsync(int id);

    /// <summary>Marque une facture émise comme payée et crée la recette au journal.</summary>
    Task MarkAsPaidAsync(int id, DateOnly? paidOn = null);

    /// <summary>
    /// Annule une facture émise en générant une Stornorechnung (montants négatifs,
    /// numéro propre, PDF archivé). Retourne la facture d'annulation créée.
    /// </summary>
    Task<Invoice> CancelAsync(int id);

    /// <summary>Relit le PDF archivé d'une facture émise. Le fichier n'est jamais régénéré.</summary>
    Task<(byte[] Content, string FileName)?> GetArchivedPdfAsync(int id);
}
