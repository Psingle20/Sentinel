using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Observations;

/// <summary>A read-only observation query from a peer (ADR-007): AgentWorker tools and Runtime recovery checks.</summary>
public sealed record QueryObservationsRequest(ObservationQuery Query) : IRequest<ObservationQueryResult>;
