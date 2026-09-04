using TreizeInvoice.Services.Auth;

namespace TreizeInvoice.Tests;

/// <summary>
/// Sur une application mono-utilisateur, le nom d'utilisateur est connu d'avance :
/// seul le mot de passe protège toute la comptabilité. Sans limitation, un
/// formulaire exposé accepte un nombre illimité d'essais.
/// </summary>
public class LoginThrottleTests
{
    [Fact]
    public void AucuneTentative_NeBloquePas()
    {
        Assert.Null(new LoginThrottle().RemainingLockout("admin"));
    }

    [Fact]
    public void QuatreEchecs_NeBloquentPasEncore()
    {
        var throttle = new LoginThrottle();

        for (var i = 0; i < 4; i++) throttle.RegisterFailure("admin");

        Assert.Null(throttle.RemainingLockout("admin"));
    }

    [Fact]
    public void CinqEchecs_BloquentLeCompte()
    {
        var throttle = new LoginThrottle();

        for (var i = 0; i < 5; i++) throttle.RegisterFailure("admin");

        var remaining = throttle.RemainingLockout("admin");
        Assert.NotNull(remaining);
        Assert.InRange(remaining!.Value.TotalMinutes, 4, 5);
    }

    [Fact]
    public void UneConnexionReussie_RemetLeCompteurAZero()
    {
        var throttle = new LoginThrottle();
        for (var i = 0; i < 4; i++) throttle.RegisterFailure("admin");

        throttle.RegisterSuccess("admin");
        for (var i = 0; i < 4; i++) throttle.RegisterFailure("admin");

        Assert.Null(throttle.RemainingLockout("admin"));
    }

    [Fact]
    public void LeBlocageNeDependPasDeLaCasse()
    {
        var throttle = new LoginThrottle();

        for (var i = 0; i < 5; i++) throttle.RegisterFailure("Admin");

        Assert.NotNull(throttle.RemainingLockout("admin"));
    }

    [Fact]
    public void UnAutreCompte_NEstPasAffecte()
    {
        var throttle = new LoginThrottle();

        for (var i = 0; i < 5; i++) throttle.RegisterFailure("admin");

        Assert.Null(throttle.RemainingLockout("mimi"));
    }
}
