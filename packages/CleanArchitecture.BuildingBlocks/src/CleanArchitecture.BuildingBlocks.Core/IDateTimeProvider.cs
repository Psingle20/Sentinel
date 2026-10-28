namespace CleanArchitecture.BuildingBlocks;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
