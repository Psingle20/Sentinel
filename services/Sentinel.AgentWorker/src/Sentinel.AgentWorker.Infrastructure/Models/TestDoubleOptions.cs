using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sentinel.AgentWorker.Infrastructure.Models;

public sealed class TestDoubleOptions
{
    public TestDoubleMode Mode { get; set; } = TestDoubleMode.Deterministic;
}
