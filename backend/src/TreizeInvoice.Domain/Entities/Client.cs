namespace TreizeInvoice.Domain.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Client (PME allemande). Soft delete uniquement : un client rattaché à des
/// factures n'est jamais supprimé physiquement (traçabilité GoBD).
/// </summary>
public class Client
{
    public int Id { get; set; }

    /// <summary>Nom ou raison sociale.</summary>
    [Required(ErrorMessage = "Le nom est requis.")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ContactPerson { get; set; }

    /// <summary>Adresse complète, multi-lignes.</summary>
    public string Address { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email invalide.")]
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
