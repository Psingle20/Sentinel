using System.Reflection;

namespace Sentinel.AgentWorker.Infrastructure;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
