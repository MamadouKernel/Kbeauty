using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Administration.Domain;

/// <summary>Global parameter edited from the back-office (PRD ADM-05); the value is validated by its key.</summary>
public sealed class AppSetting
{
    public required string Key { get; init; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Admin override of a notification message (PRD NOT-02); without a row the built-in text is used.</summary>
public sealed class NotificationTemplate : Entity
{
    public required string Code { get; init; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
