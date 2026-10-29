using Reference.Common.Control;
using Reference.Common.Revisions;
using Reference.DeployController.Application.Abstractions;

namespace Reference.DeployController.Api.Clients;

/// <summary>Reads and switches a reference service's revision through its control endpoint.</summary>
public sealed class RevisionClient(HttpClient http, IConfiguration configuration) : IRevisionSwitch
{
    public async Task<string?> ServingAsync(string service, CancellationToken cancellationToken)
    {
        RevisionView? view = await http.GetFromJsonAsync<RevisionView>(new Uri(BaseAddress(service), "/_reference/revision"), cancellationToken);
        return view?.Revision;
    }

    public async Task SwitchAsync(string service, string revision, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(BaseAddress(service), "/_reference/revision"))
        {
            Content = JsonContent.Create(new SwitchRevisionRequest(revision))
        };
        request.Headers.Add(FailureControl.TokenHeader, configuration["Reference:ControlToken"]);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Uri BaseAddress(string service) =>
        new(configuration[$"services:{service}:http:0"]
            ?? throw new InvalidOperationException($"The address of {service} is not configured."));
}
