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

    public async Task<InvoicePage<Invoice>> SearchAsync(InvoiceQuery query)
    {
        var q = _db.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Include(i => i.CancelsInvoice)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            q = q.Where(i =>
                (i.InvoiceNumber != null && EF.Functions.Like(i.InvoiceNumber, $"%{text}%")) ||
                EF.Functions.Like(i.Client.Name, $"%{text}%"));
        }

        if (query.Status is { } status)
            q = q.Where(i => i.Status == status);

        if (query.From is { } from)
            q = q.Where(i => i.InvoiceDate >= from);

        if (query.To is { } to)
            q = q.Where(i => i.InvoiceDate <= to);

        // Le montant est comparé en centimes : la colonne decimal est stockée en TEXT
        // par SQLite, une comparaison numérique y serait alphabétique.
        if (query.MinAmount is { } min)
        {
            var minCents = (long)Math.Round(min * 100m, 0, MidpointRounding.AwayFromZero);
            q = q.Where(i => i.TotalCents >= minCents);
        }

        if (query.MaxAmount is { } max)
        {
            var maxCents = (long)Math.Round(max * 100m, 0, MidpointRounding.AwayFromZero);
            q = q.Where(i => i.TotalCents <= maxCents);
        }

        var total = await q.CountAsync();

        var ordered = (query.Sort, query.Descending) switch
        {
            (InvoiceSort.Number, true) => q.OrderByDescending(i => i.InvoiceNumber),
            (InvoiceSort.Number, false) => q.OrderBy(i => i.InvoiceNumber),
            (InvoiceSort.Client, true) => q.OrderByDescending(i => i.Client.Name),
            (InvoiceSort.Client, false) => q.OrderBy(i => i.Client.Name),
            (InvoiceSort.Status, true) => q.OrderByDescending(i => i.Status),
            (InvoiceSort.Status, false) => q.OrderBy(i => i.Status),
            (InvoiceSort.Amount, true) => q.OrderByDescending(i => i.TotalCents),
            (InvoiceSort.Amount, false) => q.OrderBy(i => i.TotalCents),
            (_, false) => q.OrderBy(i => i.InvoiceDate),
            _ => q.OrderByDescending(i => i.InvoiceDate)
        };

        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        var items = await ordered
            // Départage stable : sans second critère, deux factures du même jour
            // pourraient changer d'ordre d'une page à l'autre et l'une disparaître.
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new InvoicePage<Invoice>(items, total, page, pageSize);
    }

    public async Task<Invoice?> GetAsync(int id) =>        await _db.Invoices
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
        draft.TotalCents = draft.ComputeTotalCents();

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
            ?? throw new DomainException("Rechnung nicht gefunden.");

        EnsureDraft(existing);

        existing.ClientId = draft.ClientId;
        existing.InvoiceDate = draft.InvoiceDate;
        existing.ServiceDate = draft.ServiceDate;
        existing.ServicePeriod = draft.ServicePeriod;
        existing.PaymentTermDays = draft.PaymentTermDays;
        existing.Notes = draft.Notes;

        SyncItems(existing, draft.Items);
        existing.TotalCents = existing.ComputeTotalCents();

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
            ?? throw new DomainException("Rechnung nicht gefunden.");

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
            ?? throw new DomainException("Rechnung nicht gefunden.");

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
            ?? throw new DomainException("Rechnung nicht gefunden.");

        if (invoice.Status != InvoiceStatus.Issued)
            throw new DomainException(
                "Nur eine gestellte Rechnung kann als bezahlt markiert werden.");

        if (invoice.CancelsInvoiceId is not null)
            throw new DomainException(
                "Eine Stornorechnung wird nicht vereinnahmt: sie gleicht die Ursprungsrechnung aus.");

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAtUtc = DateTime.UtcNow;

        // La recette alimente automatiquement le journal (base de l'EÜR).
        _db.JournalEntries.Add(new JournalEntry
        {
            Date = paidOn ?? DateOnly.FromDateTime(DateTime.Today),
            Type = JournalEntryType.Recette,
            Amount = invoice.Total,
            Description = $"Rechnung {invoice.InvoiceNumber} — {invoice.Client.Name}",
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
            ?? throw new DomainException("Rechnung nicht gefunden.");

        if (original.Status == InvoiceStatus.Draft)
            throw new DomainException(
                "Ein Entwurf hat keine rechtliche Wirkung: bitte löschen statt stornieren.");

        if (original.Status == InvoiceStatus.Cancelled)
            throw new DomainException(
                $"Rechnung {original.InvoiceNumber} ist bereits storniert.");

        // Sans ce garde-fou, on pourrait enchainer des storno de storno à l'infini.
        if (original.CancelsInvoiceId is not null)
            throw new DomainException(
                $"Rechnung {original.InvoiceNumber} ist selbst eine Stornorechnung.");

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
            storno.TotalCents = storno.ComputeTotalCents();
            await _db.SaveChangesAsync();

            await IssueCoreAsync(storno, profile);

            // Une facture déjà encaissée a produit une recette au journal. On ne la
            // supprime pas (GoBD) : on la neutralise par une contre-écriture, sinon
            // l'EÜR continuerait de compter un encaissement qui a été remboursé.
            if (original.Status == InvoiceStatus.Paid)
            {
                _db.JournalEntries.Add(new JournalEntry
                {
                    Date = DateOnly.FromDateTime(DateTime.Today),
                    Type = JournalEntryType.Recette,
                    Amount = -original.Total,
                    Description = $"Storno zu Rechnung {original.InvoiceNumber} — {original.Client.Name}",
                    Category = "Umsatz",
                    InvoiceId = storno.Id
                });
            }

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

    public async Task<Invoice> CorrectAsync(int id)
    {
        // Le storno d'abord : s'il échoue, aucun brouillon orphelin n'est créé.
        await CancelAsync(id);

        var draft = await DuplicateAsDraftAsync(id);
        await _audit.LogAsync(nameof(Invoice), draft.Id, "CorrectionDrafted", new { CorrectsInvoiceId = id });
        return draft;
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
            ?? throw new DomainException("Bitte hinterlegen Sie zuerst Ihre Unternehmensangaben in den Einstellungen.");
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
            throw new DomainException("Bitte wählen Sie einen Kunden aus.");

        if (invoice.Items.Count == 0)
            throw new DomainException("Die Rechnung muss mindestens eine Position enthalten.");

        if (invoice.Items.Any(i => string.IsNullOrWhiteSpace(i.Description)))
            throw new DomainException("Jede Position braucht eine Beschreibung.");

        // Les montants négatifs sont réservés aux factures d'annulation (Storno).
        if (invoice.CancelsInvoiceId is null && invoice.Items.Any(i => i.Quantity <= 0 || i.UnitPrice <= 0))
            throw new DomainException("Menge und Einzelpreis müssen größer als 0 sein.");
    }

    /// <summary>Mentions obligatoires §14 UStG côté émettrice.</summary>
    private static void EnsureProfileComplete(BusinessProfile profile)
    {
        var missing = ProfileValidation.Check(profile);

        if (missing.Count > 0)
            throw new DomainException(
                "Ihre Unternehmensangaben sind unvollständig (" + string.Join(", ", missing) +
                "). Bitte ergänzen Sie sie in den Einstellungen, bevor Sie eine Rechnung stellen.");
    }

    /// <summary>
    /// Immutabilité GoBD : seules les factures en brouillon peuvent être
    /// modifiées ou supprimées. Une facture émise ne change plus jamais.
    /// </summary>
    private static void EnsureDraft(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvoiceLockedException(
                $"Rechnung {invoice.InvoiceNumber} ist bereits gestellt und kann nicht mehr " +
                "geändert oder gelöscht werden. Korrekturen erfolgen über eine Stornorechnung.");
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
