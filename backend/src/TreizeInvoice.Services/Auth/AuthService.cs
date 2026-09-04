using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Security;

namespace TreizeInvoice.Services.Auth;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;

    public AuthService(AppDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return null;
        return _hasher.Verify(password, user.PasswordHash) ? user : null;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !_hasher.Verify(currentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = _hasher.Hash(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }
}
