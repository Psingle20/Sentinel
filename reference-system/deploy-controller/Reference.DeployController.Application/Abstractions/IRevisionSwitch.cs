namespace Reference.DeployController.Application.Abstractions;

/// <summary>Reads and switches a reference service's revision through its control endpoint; an unreachable service throws <see cref="HttpRequestException"/>.</summary>
public interface IRevisionSwitch
{
    Task<string?> ServingAsync(string service, CancellationToken cancellationToken);

    Task SwitchAsync(string service, string revision, CancellationToken cancellationToken);
}
