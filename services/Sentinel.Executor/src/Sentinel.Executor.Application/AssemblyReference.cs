using System.Reflection;

namespace Sentinel.Executor.Application;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
