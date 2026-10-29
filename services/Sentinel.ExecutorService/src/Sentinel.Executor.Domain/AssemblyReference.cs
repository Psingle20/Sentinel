using System.Reflection;

namespace Sentinel.Executor.Domain;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
