namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>A configuration document that names its own version.</summary>
public interface IVersionedDocument
{
    string Version { get; }
}
