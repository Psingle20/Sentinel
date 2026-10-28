using System.Reflection;

namespace Sentinel.Ingestion.Domain;

public static class AssemblyReference
{
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
