using System.Reflection;

namespace Sentinel.AgentWorker.Application;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
