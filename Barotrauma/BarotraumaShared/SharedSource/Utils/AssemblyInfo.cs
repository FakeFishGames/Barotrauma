using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("WindowsTest"),
          InternalsVisibleTo("MacTest"),
          InternalsVisibleTo("LinuxTest")]

public static class AssemblyInfo
{
    public static readonly string GitRevision;
    public static readonly string GitBranch;
    public static readonly string ProjectDir;
    public static readonly string BuildString;

    public enum Platform
    {
        Windows,
        MacOS,
        Linux
    }

    public enum Configuration
    {
        Release,
        Unstable,
        Debug
    }

#if WINDOWS
    public const Platform CurrentPlatform = Platform.Windows;
#elif OSX
    public const Platform CurrentPlatform = Platform.MacOS;
#elif LINUX
    public const Platform CurrentPlatform = Platform.Linux;
#else
    #error Unknown platform
#endif

#if DEBUG
    public const Configuration CurrentConfiguration = Configuration.Debug;
#elif UNSTABLE
    public const Configuration CurrentConfiguration = Configuration.Unstable;
#else
    public const Configuration CurrentConfiguration = Configuration.Release;
#endif

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

        BuildString = $"{CurrentConfiguration}{CurrentPlatform}";
    }

    public static string CleanupStackTrace(this string stackTrace)
    {
        return stackTrace.Replace(ProjectDir, "<DEV>");
    }
}