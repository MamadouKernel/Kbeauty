using KekeBeauty.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Api.Tests;

public sealed class UserSessionTokenServiceTests
{
    private static UserSessionTokenService Service(string key = "a-very-long-user-session-signing-key-for-tests") => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:SessionSigningKey"] = key,
        }).Build());

    [Fact]
    public void Client_token_restores_same_user()
    {
        var id = Guid.NewGuid();
        var service = Service();
        Assert.True(service.TryValidate(service.Create(id, "CLIENT"), "CLIENT", out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void Client_token_cannot_be_used_as_staff_token()
    {
        var service = Service();
        Assert.False(service.TryValidate(service.Create(Guid.NewGuid(), "CLIENT"), "STAFF", out _));
    }

    [Fact]
    public void Modified_token_is_rejected()
    {
        var service = Service();
        var token = service.Create(Guid.NewGuid(), "STAFF");
        token = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        Assert.False(service.TryValidate(token, "STAFF", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("a.b.c")]
    public void Malformed_token_is_rejected(string token) =>
        Assert.False(Service().TryValidate(token, "CLIENT", out _));
}