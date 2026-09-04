namespace TreizeInvoice.Domain.Exceptions;

/// <summary>
/// Levée lorsqu'on tente de modifier ou supprimer une facture émise.
/// Immutabilité GoBD : une facture Issued ne change plus jamais.
/// </summary>
public class InvoiceLockedException : DomainException
{
    public InvoiceLockedException(string message) : base(message) { }
}
