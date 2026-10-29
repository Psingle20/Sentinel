using System.Reflection;

namespace Sentinel.Gateway.Application;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
