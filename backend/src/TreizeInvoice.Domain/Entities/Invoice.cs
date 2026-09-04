using TreizeInvoice.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace TreizeInvoice.Domain.Entities;

/// <summary>
/// Facture. Le numéro n'est attribué qu'au passage à Issued (jamais en Draft),
/// dans une séquence continue par année. Une facture Issued est immuable (GoBD) ;
/// toute correction passe par une Stornorechnung.
/// </summary>
public class Invoice
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Le client est requis.")]
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    /// <summary>Numéro légal, null tant que la facture est en brouillon.</summary>
    public string? InvoiceNumber { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateOnly InvoiceDate { get; set; }

    /// <summary>Date de prestation (Leistungsdatum) — §14 UStG.</summary>
    public DateOnly? ServiceDate { get; set; }

    /// <summary>Période de prestation en texte libre (alternative à ServiceDate).</summary>
    public string? ServicePeriod { get; set; }

    /// <summary>Délai de paiement en jours (défaut 14).</summary>
    [Range(1, 365, ErrorMessage = "Le délai de paiement doit être compris entre 1 et 365 jours.")]
    public int PaymentTermDays { get; set; } = 14;

    public string? Notes { get; set; }

    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Chemin du PDF archivé (jamais régénéré après émission).</summary>
    public string? PdfPath { get; set; }

    /// <summary>Si cette facture est un storno : la facture qu'elle annule.</summary>
    public int? CancelsInvoiceId { get; set; }
    public Invoice? CancelsInvoice { get; set; }

    /// <summary>Si cette facture a été annulée : le storno qui l'annule.</summary>
    public int? CancelledByInvoiceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();

    /// <summary>Total de la facture (somme des lignes).</summary>
    public decimal Total => Items.Sum(i => i.LineTotal);
}
