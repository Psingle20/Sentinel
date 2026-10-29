using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public sealed record ProcessingResult(ProcessingOutcome Outcome, string? Reason = null);
