using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Invoicing;

namespace TreizeInvoice.Tests;

/// <summary>
/// Vérifier la simple présence des champs laissait passer une adresse sans code
/// postal ni ville — §14 Abs. 4 Nr. 1 UStG exige l'adresse complète — et un IBAN
/// tronqué, avec lequel personne ne peut payer.
/// </summary>
public class ProfileValidationTests
{
    private static BusinessProfile Complete() => new()
    {
        FullName = "Mimi Sagno",
        Address = "Castroper Hellweg 5\n44805 Bochum",
        Steuernummer = "350/5678/9012",
        Iban = "DE02120300000000202051"
    };

    [Fact]
    public void ProfilComplet_NeSignaleRien()
    {
        Assert.Empty(ProfileValidation.Check(Complete()));
    }

    [Fact]
    public void Adresse_SansCodePostalNiVille_EstSignalee()
    {
        var profile = Complete();
        profile.Address = "Castroper Hellweg 5";

        Assert.Contains("Anschrift ohne Postleitzahl und Ort", ProfileValidation.Check(profile));
    }

    [Fact]
    public void Adresse_SurUneSeuleLigne_EstAcceptee()
    {
        var profile = Complete();
        profile.Address = "Castroper Hellweg 5, 44805 Bochum";

        Assert.Empty(ProfileValidation.Check(profile));
    }

    [Fact]
    public void Adresse_AvecUnNumeroDeRueALeCinqChiffres_ExigeQuandMemeUneVille()
    {
        var profile = Complete();
        profile.Address = "Hellweg 44805";

        Assert.Contains("Anschrift ohne Postleitzahl und Ort", ProfileValidation.Check(profile));
    }

    [Theory]
    [InlineData("DE02120300000000202051")]  // référence Bundesbank
    [InlineData("de02120300000000202051")]  // saisie en minuscules
    [InlineData("DE02 1203 0000 0000 2020 51")] // avec espaces
    [InlineData("AT611904300234573201")]
    public void Iban_Valides_SontAcceptes(string iban)
    {
        Assert.True(ProfileValidation.IsPlausibleIban(iban));
    }

    [Theory]
    [InlineData("De24316581351651")]          // trop court pour un IBAN allemand
    [InlineData("DE0212030000000020205")]     // un chiffre manquant
    [InlineData("DE02120300000000202052")]    // clé de contrôle fausse
    [InlineData("1234567890")]                // pas de code pays
    [InlineData("")]
    [InlineData(null)]
    public void Iban_Invalides_SontRefuses(string? iban)
    {
        Assert.False(ProfileValidation.IsPlausibleIban(iban));
    }

    [Fact]
    public void Iban_TropCourt_EstSignaleDansLeProfil()
    {
        var profile = Complete();
        profile.Iban = "De24316581351651";

        Assert.Contains("IBAN unvollständig oder fehlerhaft", ProfileValidation.Check(profile));
    }

    [Fact]
    public void Normalisation_MetEnMajusculesEtRetireLesEspaces_SansRienInventer()
    {
        Assert.Equal("DE02120300000000202051",
            ProfileValidation.NormaliseIban("de02 1203 0000 0000 2020 51"));
        Assert.Equal(string.Empty, ProfileValidation.NormaliseIban(null));
    }

    [Fact]
    public void ChampsVides_SontListesUnParUn()
    {
        var problems = ProfileValidation.Check(new BusinessProfile());

        Assert.Contains("Name", problems);
        Assert.Contains("Anschrift", problems);
        Assert.Contains("Steuernummer", problems);
        Assert.Contains("IBAN", problems);
    }
}
