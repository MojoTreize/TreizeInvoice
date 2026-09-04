using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Clients;

public class ClientService : IClientService
{
    private readonly AppDbContext _db;

    public ClientService(AppDbContext db) => _db = db;

    public async Task<List<Client>> SearchAsync(string? term = null)
    {
        var query = _db.Clients.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim();
            query = query.Where(c =>
                c.Name.Contains(term) ||
                (c.ContactPerson != null && c.ContactPerson.Contains(term)) ||
                (c.Email != null && c.Email.Contains(term)));
        }

        return await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Client?> GetAsync(int id) =>
        await _db.Clients.FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Client> CreateAsync(Client client)
    {
        client.CreatedAtUtc = DateTime.UtcNow;
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }

    public async Task UpdateAsync(Client client)
    {
        _db.Clients.Update(client);
        await _db.SaveChangesAsync();
    }

    public async Task SoftDeleteAsync(int id)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return;

        client.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    public async Task<int> CountInvoicesAsync(int id) =>
        await _db.Invoices.CountAsync(i => i.ClientId == id);
}
