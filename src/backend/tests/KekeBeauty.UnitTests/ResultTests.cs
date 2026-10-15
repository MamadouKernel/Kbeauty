using FluentAssertions;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.UnitTests;

public class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_exposes_error_and_refuses_value_access()
    {
        var error = new Error("slot.taken", "Ce créneau vient d'être réservé");

        var result = Result.Failure<int>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Entity_ids_are_unique_and_time_ordered_uuid_v7()
    {
        var first = new TestEntity();
        Thread.Sleep(5); // v7 embeds a millisecond timestamp; ordering is only guaranteed across milliseconds.
        var second = new TestEntity();

        first.Id.Should().NotBe(second.Id);
        first.Id.Version.Should().Be(7);
        string.CompareOrdinal(first.Id.ToString(), second.Id.ToString()).Should().BeNegative();
    }

    private sealed class TestEntity : Entity;
}
