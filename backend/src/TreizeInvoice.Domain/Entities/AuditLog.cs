namespace TreizeInvoice.Domain.Entities;

/// <summary>
/// Journal d'audit append-only : chaque création/émission/annulation/paiement
/// y écrit une ligne. Aucune modification ni suppression (traçabilité GoBD).
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Entity { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    /// <summary>Détail sérialisé en JSON (avant/après, contexte).</summary>
    public string? DetailJson { get; set; }
}
