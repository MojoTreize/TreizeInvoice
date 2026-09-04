using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Invoicing;

namespace TreizeInvoice.Services.Settings;

public interface IBusinessProfileService
{
    Task<BusinessProfile> GetAsync();
    Task SaveAsync(BusinessProfile profile);
}

/// <summary>
/// Accès au profil d'entreprise (ligne unique). Garantit qu'une ligne existe toujours.
/// </summary>
public class BusinessProfileService : IBusinessProfileService
{
    private readonly AppDbContext _db;

    public BusinessProfileService(AppDbContext db) => _db = db;

    public async Task<BusinessProfile> GetAsync()
    {
        var profile = await _db.BusinessProfiles.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new BusinessProfile { IsKleinunternehmer = true };
            _db.BusinessProfiles.Add(profile);
            await _db.SaveChangesAsync();
        }
        return profile;
    }

    public async Task SaveAsync(BusinessProfile profile)
    {
        // « De243… » saisi au clavier reste un IBAN valide une fois normalisé.
        profile.Iban = ProfileValidation.NormaliseIban(profile.Iban);

        _db.BusinessProfiles.Update(profile);
        await _db.SaveChangesAsync();
    }
}
