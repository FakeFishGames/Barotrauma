using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace DeployAll;

public static class Deployables
{
    public const string ResultPath = "Deploy/bin/content";

    private const string clientProjFmt = "Barotrauma/BarotraumaClient/{0}Client.csproj";
    private const string serverProjFmt = "Barotrauma/BarotraumaServer/{0}Server.csproj";

    private static readonly ImmutableArray<(string Project, string Runtime)> platforms = new[]
    {
        ("Windows", "win-x64"),
        ("Mac", "osx-x64"),
        ("Linux", "linux-x64")
    }.ToImmutableArray();

    public static void Generate(string configuration, Version version, string gitBranch, string gitRevision)
    {
        Util.RecreateDirectory(ResultPath);
        
        File.WriteAllText(
            Path.Combine(ResultPath, "readme.txt"),
            $"This is Barotrauma {configuration} v{version} ({gitBranch}, {gitRevision}) built on {DateTime.Now}");
        
        foreach (var (project, runtime) in platforms)
        {
            string serverPath = Path.Combine(ResultPath, project, "Server");

            void checkVersion(string projPath)
            {
                Version projVersion = Version.Parse(
                    XDocument.Load(projPath).Root?
                        .Element("PropertyGroup")?
                        .Element("Version")?
                        .Value ?? throw new Exception($"Version not found in {projPath}"));
                if (projVersion != version)
                {
                    throw new Exception($"Version mismatch in {projPath}: {projVersion} != {version}");
                }
            }

            string serverProj = string.Format(serverProjFmt, project);
            string clientProj = string.Format(clientProjFmt, project);

            checkVersion(serverProj);
            checkVersion(clientProj);

            Console.WriteLine(
                $"*** Building Barotrauma {configuration}{project} v{version} ({gitBranch}, {gitRevision}) to \"{Path.Combine(ResultPath, project)}\" ***");

            DotnetCmd.Publish(
                projPath: serverProj,
                configuration: configuration,
                runtime: runtime,
                resultPath: serverPath);
            Util.DeleteFiles(serverPath,
                "*.png", "*.ogg", "*.webm",
                "*.mp4", "*.otf", "*.ttf");

            string clientPath = Path.Combine(ResultPath, project, "Client");
            string clientBundlePath = clientPath;

            if (project == "Mac")
            {
                clientPath = Path.Combine(clientPath, "Barotrauma.app", "Contents", "MacOS");
                Util.CopyDirectory("Deploy/DeployAll/macSkeleton", clientBundlePath);

                string infoPlistPath = Path.Combine(clientBundlePath, "Barotrauma.app", "Contents", "info.plist");
                string infoPlist = File.ReadAllText(infoPlistPath, Encoding.UTF8)
                    .Replace("{short_version_string}", $"{version.Major}.{version.Minor}.{version.Build}")
                    .Replace("{version}", version.ToString())
                    .Replace("{current_year}", DateTime.Now.Year.ToString());
                File.WriteAllText(infoPlistPath, infoPlist, Encoding.UTF8);
            }

            DotnetCmd.Publish(
                projPath: serverProj,
                configuration: configuration,
                runtime: runtime,
                resultPath: clientPath);
            DotnetCmd.Publish(
                projPath: clientProj,
                configuration: configuration,
                runtime: runtime,
                resultPath: clientPath);

            if (!File.Exists(Path.Combine(clientPath, "GameAnalytics.NetStandard.dll")))
            {
                throw new Exception($"GameAnalytics was not found in \"{clientPath}\"");
            }

            if (project == "Mac")
            {
                Util.CopyDirectory(Path.Combine(clientPath, "Content", "Effects"),
                    Path.Combine(
                        clientBundlePath, "Barotrauma.app", "Contents", "Resources", "Content", "Effects"));
                Util.CopyDirectory(Path.Combine(clientPath, "Content", "Lights"),
                    Path.Combine(
                        clientBundlePath, "Barotrauma.app", "Contents", "Resources", "Content", "Lights"));
            }

            Console.WriteLine("");
        }
    }
}