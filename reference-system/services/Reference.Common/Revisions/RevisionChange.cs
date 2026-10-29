namespace Reference.Common.Revisions;

/// <summary>A revision switch: what was serving, what serves now, and when it changed.</summary>
public sealed record RevisionChange(string PreviousRevision, string Revision, DateTimeOffset ChangedAt);
