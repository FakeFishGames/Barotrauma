using System.Linq;
using System.Reflection;

public static class AssemblyInfo
{
    /// <summary> Gets the git hash value from the assembly
    /// or null if it cannot be found. </summary>
    public static string GetGitRevision()
    {
        var asm = typeof(AssemblyInfo).Assembly;
        var attrs = asm.GetCustomAttributes<AssemblyMetadataAttribute>();
        return attrs.FirstOrDefault(a => a.Key == "GitRevision")?.Value;
    }

    /// <summary> Gets the git branch name from the assembly
    /// or null if it cannot be found. </summary>
    public static string GetGitBranch()
    {
        var asm = typeof(AssemblyInfo).Assembly;
        var attrs = asm.GetCustomAttributes<AssemblyMetadataAttribute>();
        return attrs.FirstOrDefault(a => a.Key == "GitBranch")?.Value;
    }

    /// <summary> Gets the build platform and configuration </summary>
    public static string GetBuildString()
    {
        string retVal = "Unknown";
#if WINDOWS
        retVal = "Windows";
#elif OSX
        retVal = "Mac";
#elif LINUX
        retVal = "Linux";
#endif

#if DEBUG
        retVal = "Debug" + retVal;
#else
        retVal = "Release" + retVal;
#endif

        return retVal;
    }
}