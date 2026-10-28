using System.Reflection;

namespace Sentinel.Runtime.Application;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
