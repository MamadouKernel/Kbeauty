using KekeBeauty.Api.Auth;
using KekeBeauty.Api.Controllers;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Api.Tests;
public sealed class AdminSecurityTests
{
    private static AdminSessionTokenService Tokens(string key="test-secret")=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Admin:SigningKey",key}}).Build());
    [Fact] public void TokenValide_RestitueIdentite(){var s=Tokens();var id=Guid.NewGuid();var token=s.Create(new(id,"Awa","KYC"));Assert.True(s.TryValidate(token,out var actual));Assert.Equal(id,actual!.Id);Assert.Equal("KYC",actual.Role);}
    [Fact] public void TokenModifie_EstRefuse(){var s=Tokens();var token=s.Create(new(Guid.NewGuid(),"Awa","SUPPORT"));token=token[..^2]+"AA";Assert.False(s.TryValidate(token,out _));}
    [Fact] public void TokenAutreCle_EstRefuse(){var token=Tokens("key-one").Create(new(Guid.NewGuid(),"Awa","COMPTABLE"));Assert.False(Tokens("key-two").TryValidate(token,out _));}
    [Fact] public void MotDePasse_HashSaleEtVerification(){var hash=AdminAuthController.Hash("Fort!234");Assert.True(AdminAuthController.Verify("Fort!234",hash));Assert.False(AdminAuthController.Verify("mauvais",hash));Assert.DoesNotContain("Fort!234",hash);}
    [Fact] public void SignatureSansSecret_EstRefusee()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        Assert.Throws<InvalidOperationException>(() => new AdminSessionTokenService(configuration));
    }

    [Fact] public void SecretBootstrap_ProduitDesJetonsPersistantsEntreInstances()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Admin:BootstrapPassword", "bootstrap-secret" }
        }).Build();
        var token = new AdminSessionTokenService(configuration).Create(new(Guid.NewGuid(), "Awa", "SUPPORT"));
        Assert.True(new AdminSessionTokenService(configuration).TryValidate(token, out _));
    }}
