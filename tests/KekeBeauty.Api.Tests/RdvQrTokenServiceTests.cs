using KekeBeauty.Api.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace KekeBeauty.Api.Tests;
public sealed class RdvQrTokenServiceTests
{
 static RdvQrTokenService Create()=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Authentication:SessionSigningKey","test-signing-key-at-least-thirty-two-characters-long"}}).Build());
 [Fact] public void RoundTrip_PreservesRdvAndClient(){var service=Create();var rdv=Guid.NewGuid();var client=Guid.NewGuid();var token=service.Create(rdv,client);Assert.True(service.TryValidate(token,out var actualRdv,out var actualClient));Assert.Equal(rdv,actualRdv);Assert.Equal(client,actualClient);}
 [Fact] public void TamperedToken_IsRejected(){var service=Create();var token=service.Create(Guid.NewGuid(),Guid.NewGuid());var tampered=token[..^1]+(token[^1]=='A'?'B':'A');Assert.False(service.TryValidate(tampered,out _,out _));}
}