namespace TreizeInvoice.Services.Compliance;

/// <summary>Document généré, prêt à être téléchargé.</summary>
public record GeneratedDocument(string FileName, byte[] Content);

/// <summary>Plage de numéros réellement attribuée sur un exercice.</summary>
public record NumberRange(int Year, int IssuedCount, string? First, string? Last);

/// <summary>
/// Faits collectés dans l'installation. Séparés du rendu pour être vérifiables
/// en test sans avoir à ouvrir un PDF.
/// </summary>
public record VerfahrensdokumentationFacts(
    DateTime CreatedAt,
    string OwnerName,
    string OwnerAddress,
    string Steuernummer,
    bool IsKleinunternehmer,
    string NumberFormat,
    string DatabasePath,
    string ArchivePath,
    int DraftCount,
    int IssuedCount,
    int PaidCount,
    int CancelledCount,
    int StornoCount,
    IReadOnlyList<NumberRange> Ranges,
    int JournalEntryCount,
    int AuditEntryCount,
    DateTime? FirstAuditUtc,
    DateTime? LastAuditUtc);

/// <summary>
/// Produit la « Verfahrensdokumentation » exigée par les GoBD (Rz. 151 ss.).
///
/// Tout contribuable qui tient sa comptabilité avec un outil informatique doit
/// pouvoir décrire par écrit, à un vérificateur, comment les pièces naissent,
/// sont numérotées, verrouillées, archivées et sauvegardées. L'absence de ce
/// document est l'un des reproches les plus fréquents lors d'un contrôle.
///
/// Le document n'est pas un texte type : il est rempli avec les données réelles
/// de l'installation (profil, format de numérotation, chemins, volumétrie,
/// dernières écritures d'audit) afin d'être opposable.
/// </summary>
public interface IVerfahrensdokumentationService
{
    Task<VerfahrensdokumentationFacts> CollectAsync(CancellationToken cancellationToken = default);

    Task<GeneratedDocument> CreateAsync(CancellationToken cancellationToken = default);
}
