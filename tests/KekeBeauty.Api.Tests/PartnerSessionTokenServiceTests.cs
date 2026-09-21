using KekeBeauty.Api.Auth;
using Microsoft.Extensions.Configuration;
namespace KekeBeauty.Api.Tests;
public sealed class PartnerSessionTokenServiceTests
{
 static PartnerSessionTokenService Service(string key="a-very-long-test-signing-key-for-keke-beauty")=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Authentication:SessionSigningKey",key}}).Build());
 [Fact] public void Created_token_restores_same_partner(){var id=Guid.NewGuid();var service=Service();var token=service.Create(id);Assert.True(service.TryValidate(token,out var parsed));Assert.Equal(id,parsed);}
 [Fact] public void Modified_token_is_rejected(){var service=Service();var token=service.Create(Guid.NewGuid());token=token[..^1]+(token[^1]=='A'?'B':'A');Assert.False(service.TryValidate(token,out _));}
 [Fact] public void Token_signed_with_another_key_is_rejected(){var token=Service("first-signing-key-long-enough-for-tests").Create(Guid.NewGuid());Assert.False(Service("second-signing-key-long-enough-tests").TryValidate(token,out _));}
 [Theory][InlineData("")][InlineData("invalid")][InlineData("a.b.c")] public void Malformed_token_is_rejected(string token)=>Assert.False(Service().TryValidate(token,out _));
}
