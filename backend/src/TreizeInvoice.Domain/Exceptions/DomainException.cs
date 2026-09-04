namespace TreizeInvoice.Domain.Exceptions;

/// <summary>
/// Exception de règle métier. Sert de base à toutes les violations
/// de contraintes légales (verrouillage GoBD, numérotation, storno).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
