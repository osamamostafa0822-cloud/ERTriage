using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ERTriage.Infrastructure.Persistence.Converters;

/// <summary>
/// The Domain stores UTC <see cref="DateTime"/> values. SQL Server <c>datetime2</c> has no time-zone info, so values
/// read back would have <see cref="DateTimeKind.Unspecified"/>. This converter restores <see cref="DateTimeKind.Utc"/>
/// on read and writes the value unchanged (the Domain already supplies UTC).
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value)
    {
    }
}
