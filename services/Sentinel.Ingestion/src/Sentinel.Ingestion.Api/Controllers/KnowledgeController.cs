using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.BusinessProcesses.Knowledge;

namespace Sentinel.Ingestion.Api.Controllers;

/// <summary>Runbook and documentation search for AgentWorker's knowledge tool, behind the same bulkhead as observation queries.</summary>
[Route(Routes.Root + "/knowledge")]
public sealed class KnowledgeController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("search")]
    public async Task<IResult> Search(KnowledgeQuery query, CancellationToken cancellationToken) =>
        MatchQuery(await requests.HandleRequestAsync(new SearchKnowledgeRequest(query), cancellationToken));
}
