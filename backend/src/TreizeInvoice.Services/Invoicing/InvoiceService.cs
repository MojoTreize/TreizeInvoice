using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;

namespace TreizeInvoice.Services.Invoicing;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public InvoiceService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<Invoice>> GetAllAsync() =>
        await _db.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters() // les factures des clients supprimés restent visibles (GoBD)
            .Include(i => i.Client)
            .Include(i => i.Items)
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

    public async Task<Invoice?> GetAsync(int id) =>
        await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Client)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id);

    public async Task<Invoice> CreateDraftAsync(Invoice draft)
    {
        draft.Status = InvoiceStatus.Draft;
        draft.InvoiceNumber = null; // le numéro n'est attribué qu'à l'émission
        draft.IssuedAtUtc = null;
        draft.PaidAtUtc = null;
        draft.CreatedAtUtc = DateTime.UtcNow;

        _db.Invoices.Add(draft);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(nameof(Invoice), draft.Id, "DraftCreated", new { draft.ClientId, Total = draft.Total });
        return draft;
    }

    public async Task UpdateDraftAsync(Invoice draft)
    {
        var existing = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == draft.Id)
            ?? throw new DomainException("Facture introuvable.");

        EnsureDraft(existing);

        existing.ClientId = draft.ClientId;
        existing.InvoiceDate = draft.InvoiceDate;
        existing.ServiceDate = draft.ServiceDate;
        existing.ServicePeriod = draft.ServicePeriod;
        existing.PaymentTermDays = draft.PaymentTermDays;
        existing.Notes = draft.Notes;

        SyncItems(existing, draft.Items);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(nameof(Invoice), existing.Id, "DraftUpdated", new { Total = existing.Total });
    }

    public async Task DeleteDraftAsync(int id)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return;

        EnsureDraft(invoice);

        _db.Invoices.Remove(invoice);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(nameof(Invoice), id, "DraftDeleted");
    }

    public async Task<Invoice> DuplicateAsDraftAsync(int id)
    {
        var source = await _db.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new DomainException("Facture introuvable.");

        var copy = new Invoice
        {
            ClientId = source.ClientId,
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            ServiceDate = source.ServiceDate,
            ServicePeriod = source.ServicePeriod,
            PaymentTermDays = source.PaymentTermDays,
            Notes = source.Notes,
            Items = source.Items
                .Select(it => new InvoiceItem
                {
                    Description = it.Description,
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice
                })
                .ToList()
        };

        return await CreateDraftAsync(copy);
    }

    /// <summary>
    /// Immutabilité GoBD : seules les factures en brouillon peuvent être
    /// modifiées ou supprimées. Une facture émise ne change plus jamais.
    /// </summary>
    private static void EnsureDraft(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvoiceLockedException(
                $"La facture {invoice.InvoiceNumber} est émise : elle ne peut plus être modifiée ni supprimée. " +
                "Utilisez une facture d'annulation (Storno) pour la corriger.");
    }

    private void SyncItems(Invoice existing, ICollection<InvoiceItem> incoming)
    {
        // Instantané : l'appelant peut passer la collection déjà attachée à l'entité.
        var incomingSnapshot = incoming.ToList();

        var removed = existing.Items.Where(e => incomingSnapshot.All(n => n.Id != e.Id)).ToList();
        _db.InvoiceItems.RemoveRange(removed);
        foreach (var item in removed)
            existing.Items.Remove(item);

        foreach (var item in incomingSnapshot)
        {
            var target = item.Id != 0
                ? existing.Items.FirstOrDefault(e => e.Id == item.Id)
                : null;

            if (target is null)
            {
                if (!existing.Items.Contains(item))
                {
                    existing.Items.Add(new InvoiceItem
                    {
                        Description = item.Description,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice
                    });
                }
            }
            else
            {
                target.Description = item.Description;
                target.Quantity = item.Quantity;
                target.UnitPrice = item.UnitPrice;
            }
        }
    }
}
