using System;
using System.Linq;
using System.Reflection;

public static class AssemblyInfo
{
    public static readonly string GitRevision;
    public static readonly string GitBranch;
    public static readonly string ProjectDir;
    public static readonly string BuildString;

    static AssemblyInfo()
    {
        var asm = typeof(AssemblyInfo).Assembly;
        var attrs = asm.GetCustomAttributes<AssemblyMetadataAttribute>();

        GitRevision = attrs.FirstOrDefault(a => a.Key == "GitRevision")?.Value;

        GitBranch = attrs.FirstOrDefault(a => a.Key == "GitBranch")?.Value;

        ProjectDir = attrs.FirstOrDefault(a => a.Key == "ProjectDir")?.Value;
        if (ProjectDir.Last() == '/' || ProjectDir.Last() == '\\') { ProjectDir = ProjectDir.Substring(0, ProjectDir.Length - 1); }
        string[] dirSplit = ProjectDir.Split('/', '\\');
        ProjectDir = string.Join(ProjectDir.Contains('/') ? '/' : '\\', dirSplit.Take(dirSplit.Length - 2));

        BuildString = "Unknown";
#if WINDOWS
        BuildString = "Windows";
#elif OSX
        BuildString = "Mac";
#elif LINUX
        BuildString = "Linux";
#endif

#if DEBUG
        BuildString = "Debug" + BuildString;
#elif UNSTABLE
        BuildString = "Unstable" + BuildString;
#else
        BuildString = "Release" + BuildString;
#endif
    }

    public static string CleanupStackTrace(this string stackTrace)
    {
        return stackTrace.Replace(ProjectDir, "<DEV>");
    }
}