using System.Reflection;

namespace Sentinel.Ingestion.Application;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
