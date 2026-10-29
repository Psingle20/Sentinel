namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>Why a publish was refused.</summary>
public static class VersionedDocumentErrors
{
    public static readonly Error VersionConflict = Error.Conflict("Configuration.VersionConflict", "The configuration changed since it was read; reload it and apply the change again.");

    public static readonly Error VersionUnchanged = Error.Problem("Configuration.VersionUnchanged", "A published configuration needs a new version.");

    public static readonly Error DuplicateVersion = Error.Conflict("Configuration.DuplicateVersion", "That version was published before; versions are never reused.");

    public static Error Invalid(IReadOnlyList<string> problems) => Error.Problem("Configuration.Invalid", string.Join(" ", problems));
}
