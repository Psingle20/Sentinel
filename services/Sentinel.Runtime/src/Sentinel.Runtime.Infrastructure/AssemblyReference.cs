using System.Reflection;

namespace Sentinel.Runtime.Infrastructure;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
