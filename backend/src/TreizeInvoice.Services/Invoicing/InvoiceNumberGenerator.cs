using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Invoicing;

public interface IInvoiceNumberGenerator
{
    /// <summary>
    /// Réserve et retourne le prochain numéro de l'année. Doit être appelé à
    /// l'intérieur de la transaction d'émission : le compteur n'est consommé
    /// que si l'émission est validée (pas de trou dans la séquence).
    /// </summary>
    Task<string> NextAsync(int year, string format);
}

public class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    private static readonly Regex CounterToken =
        new(@"\{counter(?::(?<fmt>[^}]+))?\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly AppDbContext _db;

    public InvoiceNumberGenerator(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(int year, string format)
    {
        var sequence = await _db.InvoiceNumberSequences.FirstOrDefaultAsync(s => s.Year == year);
        if (sequence is null)
        {
            sequence = new InvoiceNumberSequence { Year = year, LastNumber = 0 };
            _db.InvoiceNumberSequences.Add(sequence);
        }

        sequence.LastNumber++;
        await _db.SaveChangesAsync();

        return Format(format, year, sequence.LastNumber);
    }

    /// <summary>Rend un gabarit du type "{year}-{counter:0000}" → "2026-0001".</summary>
    public static string Format(string format, int year, int counter)
    {
        if (string.IsNullOrWhiteSpace(format))
            format = "{year}-{counter:0000}";

        var result = format.Replace("{year}", year.ToString(), StringComparison.OrdinalIgnoreCase);

        return CounterToken.Replace(result, m =>
        {
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : "0";
            return counter.ToString(fmt);
        });
    }
}
