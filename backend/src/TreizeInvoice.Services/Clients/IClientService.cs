using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Clients;

public interface IClientService
{
    /// <summary>Liste les clients actifs, filtrés par terme (nom, contact, email).</summary>
    Task<List<Client>> SearchAsync(string? term = null);

    Task<Client?> GetAsync(int id);
    Task<Client> CreateAsync(Client client);
    Task UpdateAsync(Client client);

    /// <summary>Suppression logique (soft delete) : le client n'est jamais effacé physiquement.</summary>
    Task SoftDeleteAsync(int id);

    /// <summary>Nombre de factures rattachées (pour info avant suppression).</summary>
    Task<int> CountInvoicesAsync(int id);
}
