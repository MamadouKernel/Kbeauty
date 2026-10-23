using FluentAssertions;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KekeBeauty.UnitTests;

public sealed class SmtpEmailSenderTests
{
    private static SmtpEmailSender Sender(string host = "127.0.0.1", int port = 1) =>
        new(Options.Create(new SmtpOptions { Host = host, Port = port, Security = "None", FromAddress = "noreply@keke.test" }), NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async Task An_unreachable_relay_is_reported_as_a_failure_and_never_thrown()
    {
        var accepted = await ((IEmailSender)Sender()).TrySendAsync(new EmailMessage("awa@example.com", "Sujet", "Corps"));

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_swallows_the_same_failure_so_that_sign_up_never_breaks()
    {
        var act = async () => await Sender().SendAsync(new EmailMessage("awa@example.com", "Sujet", "Corps"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_malformed_recipient_is_a_failure_not_a_crash()
    {
        var accepted = await ((IEmailSender)Sender()).TrySendAsync(new EmailMessage("not an address", "Sujet", "Corps"));

        accepted.Should().BeFalse();
    }
}
