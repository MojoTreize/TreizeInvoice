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
}
