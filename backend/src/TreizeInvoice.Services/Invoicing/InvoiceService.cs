using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Services.Invoicing;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IInvoiceNumberGenerator _numbers;
    private readonly IInvoicePdfRenderer _renderer;
    private readonly IInvoiceArchive _archive;

    public InvoiceService(
        AppDbContext db,
        IAuditService audit,
        IInvoiceNumberGenerator numbers,
        IInvoicePdfRenderer renderer,
        IInvoiceArchive archive)
    {
        _db = db;
        _audit = audit;
        _numbers = numbers;
        _renderer = renderer;
        _archive = archive;
    }

    public async Task<List<Invoice>> GetAllAsync() =>
        await _db.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters() // les factures des clients supprimés restent visibles (GoBD)
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Include(i => i.CancelsInvoice)
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

    public async Task<Invoice?> GetAsync(int id) =>
        await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Include(i => i.CancelsInvoice)
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

    public async Task<Invoice> IssueAsync(int id)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Items)
            .Include(i => i.Client)
            .Include(i => i.CancelsInvoice)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new DomainException("Facture introuvable.");

        EnsureDraft(invoice);
        EnsureIssuable(invoice);

        var profile = await LoadProfileAsync();

        // Numéro et verrouillage dans une seule transaction : si quoi que ce soit
        // échoue, le compteur n'est pas consommé et la séquence reste sans trou.
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await IssueCoreAsync(invoice, profile);

            await _audit.LogAsync(nameof(Invoice), invoice.Id, "Issued",
                new { invoice.InvoiceNumber, Total = invoice.Total, invoice.PdfPath });

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return invoice;
    }

    public async Task MarkAsPaidAsync(int id, DateOnly? paidOn = null)
    {
        var invoice = await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Items)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new DomainException("Facture introuvable.");

        if (invoice.Status != InvoiceStatus.Issued)
            throw new DomainException(
                "Seule une facture émise peut être marquée comme payée.");

        if (invoice.CancelsInvoiceId is not null)
            throw new DomainException(
                "Une facture d'annulation ne s'encaisse pas : elle neutralise la facture d'origine.");

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAtUtc = DateTime.UtcNow;

        // La recette alimente automatiquement le journal (base de l'EÜR).
        _db.JournalEntries.Add(new JournalEntry
        {
            Date = paidOn ?? DateOnly.FromDateTime(DateTime.Today),
            Type = JournalEntryType.Recette,
            Amount = invoice.Total,
            Description = $"Facture {invoice.InvoiceNumber} — {invoice.Client.Name}",
            Category = "Umsatz",
            InvoiceId = invoice.Id
        });

        await _db.SaveChangesAsync();
        await _audit.LogAsync(nameof(Invoice), invoice.Id, "Paid",
            new { invoice.InvoiceNumber, Total = invoice.Total });
    }

    public async Task<Invoice> CancelAsync(int id)
    {
        var original = await _db.Invoices
            .IgnoreQueryFilters()
            .Include(i => i.Items)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new DomainException("Facture introuvable.");

        if (original.Status == InvoiceStatus.Draft)
            throw new DomainException(
                "Un brouillon n'a pas de valeur légale : supprimez-le au lieu de l'annuler.");

        if (original.Status == InvoiceStatus.Cancelled)
            throw new DomainException(
                $"La facture {original.InvoiceNumber} est déjà annulée.");

        if (original.Status == InvoiceStatus.Paid)
            throw new DomainException(
                $"La facture {original.InvoiceNumber} est déjà encaissée. " +
                "Contactez votre conseiller fiscal avant de l'annuler.");

        // Sans ce garde-fou, on pourrait enchainer des storno de storno à l'infini.
        if (original.CancelsInvoiceId is not null)
            throw new DomainException(
                $"La facture {original.InvoiceNumber} est elle-même une facture d'annulation.");

        var profile = await LoadProfileAsync();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // La correction d'une facture émise passe obligatoirement par une
            // Stornorechnung : montants négatifs, numéro propre dans la séquence.
            var storno = new Invoice
            {
                ClientId = original.ClientId,
                Client = original.Client,
                InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
                ServiceDate = original.ServiceDate,
                ServicePeriod = original.ServicePeriod,
                PaymentTermDays = original.PaymentTermDays,
                CancelsInvoiceId = original.Id,
                CancelsInvoice = original,
                Status = InvoiceStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                Items = original.Items
                    .Select(it => new InvoiceItem
                    {
                        Description = it.Description,
                        Quantity = -it.Quantity,
                        UnitPrice = it.UnitPrice
                    })
                    .ToList()
            };

            _db.Invoices.Add(storno);
            await _db.SaveChangesAsync();

            await IssueCoreAsync(storno, profile);

            original.Status = InvoiceStatus.Cancelled;
            original.CancelledByInvoiceId = storno.Id;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(nameof(Invoice), original.Id, "Cancelled",
                new { original.InvoiceNumber, StornoNumber = storno.InvoiceNumber });
            await _audit.LogAsync(nameof(Invoice), storno.Id, "StornoIssued",
                new { storno.InvoiceNumber, CancelsNumber = original.InvoiceNumber });

            await transaction.CommitAsync();
            return storno;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Attribution du numéro, verrouillage et archivage du PDF. À appeler dans une transaction.</summary>
    private async Task IssueCoreAsync(Invoice invoice, BusinessProfile profile)
    {
        var year = invoice.InvoiceDate.Year;

        invoice.InvoiceNumber = await _numbers.NextAsync(year, profile.NumberFormat);
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssuedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var pdf = _renderer.Render(invoice, profile);
        invoice.PdfPath = _archive.Store(year, invoice.InvoiceNumber, pdf);
        await _db.SaveChangesAsync();
    }

    private async Task<BusinessProfile> LoadProfileAsync()
    {
        var profile = await _db.BusinessProfiles.FirstOrDefaultAsync()
            ?? throw new DomainException("Renseignez d'abord vos informations d'entreprise dans Paramètres.");
        EnsureProfileComplete(profile);
        return profile;
    }

    public async Task<(byte[] Content, string FileName)?> GetArchivedPdfAsync(int id)
    {
        var invoice = await _db.Invoices.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice?.PdfPath is null) return null;

        var content = _archive.Read(invoice.PdfPath);
        return content is null ? null : (content, $"{invoice.InvoiceNumber}.pdf");
    }

    /// <summary>Contrôles métier avant émission : une facture émise ne peut plus être corrigée.</summary>
    private static void EnsureIssuable(Invoice invoice)
    {
        if (invoice.ClientId == 0)
            throw new DomainException("Un client est requis pour émettre la facture.");

        if (invoice.Items.Count == 0)
            throw new DomainException("La facture doit contenir au moins une ligne.");

        if (invoice.Items.Any(i => string.IsNullOrWhiteSpace(i.Description)))
            throw new DomainException("Chaque ligne doit avoir une description.");

        // Les montants négatifs sont réservés aux factures d'annulation (Storno).
        if (invoice.CancelsInvoiceId is null && invoice.Items.Any(i => i.Quantity <= 0 || i.UnitPrice <= 0))
            throw new DomainException("Chaque ligne doit avoir une quantité et un prix unitaire supérieurs à 0.");
    }

    /// <summary>Mentions obligatoires §14 UStG côté émettrice.</summary>
    private static void EnsureProfileComplete(BusinessProfile profile)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.FullName)) missing.Add("nom");
        if (string.IsNullOrWhiteSpace(profile.Address)) missing.Add("adresse");
        if (string.IsNullOrWhiteSpace(profile.Steuernummer)) missing.Add("Steuernummer");
        if (string.IsNullOrWhiteSpace(profile.Iban)) missing.Add("IBAN");

        if (missing.Count > 0)
            throw new DomainException(
                "Informations d'entreprise incomplètes (" + string.Join(", ", missing) +
                "). Complétez-les dans Paramètres avant d'émettre une facture.");
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
