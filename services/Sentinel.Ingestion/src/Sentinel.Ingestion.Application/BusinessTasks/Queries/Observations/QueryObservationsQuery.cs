using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Observations;

public sealed record QueryObservationsQuery(ObservationQuery Query) : IQuery<ObservationQueryResult>;
