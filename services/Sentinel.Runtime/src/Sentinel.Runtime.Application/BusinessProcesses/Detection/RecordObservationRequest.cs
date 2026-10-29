using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Observations;

namespace Sentinel.Runtime.Application.BusinessProcesses.Detection;

/// <summary>Ingestion recorded an observation, which detection and correlation consider.</summary>
public sealed record RecordObservationRequest(Observation Observation) : IRequest;
