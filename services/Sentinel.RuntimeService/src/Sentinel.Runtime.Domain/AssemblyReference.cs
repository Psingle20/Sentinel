using System.Reflection;

namespace Sentinel.Runtime.Domain;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
