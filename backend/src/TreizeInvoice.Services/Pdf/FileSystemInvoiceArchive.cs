namespace TreizeInvoice.Services.Pdf;

public interface IInvoiceArchive
{
    /// <summary>
    /// Écrit le PDF dans archive/{année}/{numéro}.pdf et retourne le chemin.
    /// Un fichier déjà archivé n'est jamais écrasé (GoBD).
    /// </summary>
    string Store(int year, string invoiceNumber, byte[] pdf);

    /// <summary>Relit le PDF archivé. Retourne null si le fichier n'existe plus.</summary>
    byte[]? Read(string path);
}

public class FileSystemInvoiceArchive : IInvoiceArchive
{
    private readonly string _rootPath;

    public FileSystemInvoiceArchive(string rootPath) => _rootPath = rootPath;

    public string Store(int year, string invoiceNumber, byte[] pdf)
    {
        var directory = Path.Combine(_rootPath, year.ToString());
        Directory.CreateDirectory(directory);

        var safeName = string.Concat(invoiceNumber.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(directory, $"{safeName}.pdf");

        // CreateNew : lève si le fichier existe déjà, l'archive est immuable.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(pdf);

        return path;
    }

    public byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
}
