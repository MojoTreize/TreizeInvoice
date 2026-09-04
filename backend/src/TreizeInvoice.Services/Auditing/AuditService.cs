using System.Text.Json;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Auditing;

public interface IAuditService
{
    /// <summary>Écrit une ligne au journal d'audit. Aucune écriture n'est jamais modifiée ni supprimée.</summary>
    Task LogAsync(string entity, int entityId, string action, object? detail = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public async Task LogAsync(string entity, int entityId, string action, object? detail = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            Entity = entity,
            EntityId = entityId,
            Action = action,
            DetailJson = detail is null ? null : JsonSerializer.Serialize(detail)
        });
        await _db.SaveChangesAsync();
    }
}
