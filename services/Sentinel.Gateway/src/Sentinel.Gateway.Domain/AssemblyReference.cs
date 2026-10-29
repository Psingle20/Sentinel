using System.Reflection;

namespace Sentinel.Gateway.Domain;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
