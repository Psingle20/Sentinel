namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>What happened to a publish request.</summary>
public enum AppendOutcome
{
    /// <summary>The version is now the current one.</summary>
    Appended,

    /// <summary>Another version was published after the one this change was based on.</summary>
    Conflict,

    /// <summary>A version with this name was published before; versions are never reused.</summary>
    DuplicateVersion
}
