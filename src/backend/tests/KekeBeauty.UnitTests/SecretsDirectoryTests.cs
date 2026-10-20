using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.UnitTests;

/// <summary>Secrets mounted as files (Docker/Kubernetes secrets) are how a deployment keeps them out of the repository.</summary>
public sealed class SecretsDirectoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keke-secrets-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_file_named_like_the_setting_provides_it_and_a_trailing_newline_is_not_part_of_the_value()
    {
        File.WriteAllText(Path.Combine(_directory, "Jwt__SigningKey"), "value-from-the-keystore\n");
        File.WriteAllText(Path.Combine(_directory, "Payments__Winipay__Token"), "another-value");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = "from-the-environment" })
            .AddKeyPerFile(_directory, optional: true)
            .Build();

        configuration["Jwt:SigningKey"].Should().Be("value-from-the-keystore", "a mounted secret is read last and wins");
        configuration["Payments:Winipay:Token"].Should().Be("another-value");
    }

    [Fact]
    public void A_missing_folder_is_fine_so_that_development_needs_no_keystore()
    {
        var configuration = new ConfigurationBuilder().AddKeyPerFile(Path.Combine(_directory, "absent"), optional: true).Build();

        configuration["Jwt:SigningKey"].Should().BeNull();
    }
}
