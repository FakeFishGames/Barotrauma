using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace DeployAll;

public static class SteamPipeAssistant
{
    private abstract record ScriptItem(string Name)
    {
        public abstract override string ToString();
    }

    private record SingleItem(string Name, string Value) : ScriptItem(Name)
    {
        public override string ToString() => $"\"{Name}\" \"{Value}\"";
    }

    private record AggregateItem(string Name, params ScriptItem[] SubItems) : ScriptItem(Name)
    {
        public override string ToString()
        {
            return $"\"{Name}\"\n"
                + "{\n"
                + string.Join("\n",
                    SubItems.Select(it => it.ToString())
                        .SelectMany(s => s.Split("\n"))
                        .Select(s => $"\t{s}"))
                + "\n}";
        }
    }
    
    private static string steamCmdUrl
        => true switch
        {
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip",
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                => "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz",
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz",
            _ => throw new Exception($"Unsupported host platform: {RuntimeInformation.OSDescription}")
        };

    private static string[] steamCmdFilenames
        => true switch
        {
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => new[] { "steamcmd.exe" },
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                => new[] { "steamcmd.sh", "linux32/steamcmd", "linux32/steamerrorreporter" },
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => new[] { "steamcmd.sh", "steamcmd" },
            _ => throw new Exception($"Unsupported host platform: {RuntimeInformation.OSDescription}")
        };

    private const string SteamCmdPath = "Deploy/bin/steamcmd";
    
    public static void PrepareSteamCmd()
    {
        if (Directory.Exists(SteamCmdPath))
        {
            Console.WriteLine($"SteamCMD found at {SteamCmdPath}, skipping download");
            return;
        }
        Console.WriteLine($"Downloading SteamCMD to {SteamCmdPath}");
        
        Util.RecreateDirectory(SteamCmdPath);
        
        var steamCmdPkg = Util.DownloadFile(steamCmdUrl).ToArray();
        
        if (Path.GetExtension(steamCmdUrl) == ".zip")
        {
            using var memStream = new MemoryStream(steamCmdPkg);
            using ZipArchive archive = new ZipArchive(memStream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(SteamCmdPath);
        }
        else
        {
            string downloadResultPath = Path.Combine(SteamCmdPath, Path.GetFileName(steamCmdUrl));
            File.WriteAllBytes(downloadResultPath, steamCmdPkg);

            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                ArgumentList =
                {
                    "-xf",
                    downloadResultPath,
                    "-C",
                    SteamCmdPath
                },
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            var process = Util.StartProcess(psi);
            process.WaitForExit();

            File.Delete(downloadResultPath);

            foreach (var filename in steamCmdFilenames)
            {
                psi = new ProcessStartInfo
                {
                    FileName = "chmod",
                    ArgumentList =
                    {
                        "+x",
                        Path.Combine(SteamCmdPath, filename)
                    },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                process = Util.StartProcess(psi);
                process.WaitForExit();
            }
        }
        
        Console.WriteLine("SteamCMD downloaded and extracted");
    }

    private const string ScriptPath = "Deploy/bin/scripts";
    private const string BuildOutput = "Deploy/bin/output";

    private const string appIdScriptFileFmt = "app_{0}.vdf";

    private const ulong ClientAppId = 602960;
    private const ulong ClientWindowsDepotId = 602961;
    private const ulong ClientLinuxDepotId = 602962;
    private const ulong ClientMacDepotId = 602963;
    
    private const ulong ServerAppId = 1026340;
    private const ulong ServerWindowsDepotId = 1026341;
    private const ulong ServerLinuxDepotId = 1026342;

    private static ScriptItem PrepareDepotScript(ulong depotId, string contentPath)
    {
        var childItems = new List<ScriptItem>
        {
            new SingleItem("DepotID", depotId.ToString()),
            new SingleItem("contentroot", contentPath),
            new AggregateItem("FileMapping",
                new SingleItem("LocalPath", "*"),
                new SingleItem("DepotPath", "."),
                new SingleItem("recursive", "1")),
            new SingleItem("FileExclusion", "config_player.xml"),
            new SingleItem("FileExclusion", "Thumbs.db"),
            new SingleItem("FileExclusion", ".DS_Store"),
            new SingleItem("FileExclusion", "__MACOSX"),
        };

        if (depotId == ClientMacDepotId)
        {
            childItems.Add(new SingleItem("InstallScript", "Barotrauma.app/installscript.vdf"));
        }

        var script = new AggregateItem("DepotBuildConfig", childItems.ToArray());
        var scriptFileName = Path.Combine(ScriptPath, $"depot_{depotId}.vdf");
        File.WriteAllText(scriptFileName, script.ToString());
        return new SingleItem(depotId.ToString(), Path.GetFullPath(scriptFileName));
    }

    private static void PrepareAppScript(ulong appId, string configuration, Version version, string gitBranch, string gitRevision)
    {
        var depotScripts = new AggregateItem("depots", appId switch
        {
            ClientAppId => new[]
            {
                PrepareDepotScript(ClientWindowsDepotId,
                    Path.Combine("Windows", "Client")),
                PrepareDepotScript(ClientMacDepotId,
                    Path.Combine("Mac", "Client")),
                PrepareDepotScript(ClientLinuxDepotId,
                    Path.Combine("Linux", "Client"))
            },
            ServerAppId => new[]
            {
                PrepareDepotScript(ServerWindowsDepotId,
                    Path.Combine("Windows", "Server")),
                PrepareDepotScript(ServerLinuxDepotId,
                    Path.Combine("Linux", "Server"))
            },
            _ => throw new InvalidOperationException()
        });

        var script = new AggregateItem("appbuild",
            new SingleItem("appid", appId.ToString()),
            new SingleItem("desc", $"{configuration} v{version} ({gitBranch}, {gitRevision})"),
            new SingleItem("buildoutput", Path.GetFullPath(BuildOutput)),
            new SingleItem("contentroot", Path.GetFullPath(Deployables.ResultPath)),
            new SingleItem("setlive", appId switch
            {
                ClientAppId => "experimental",
                ServerAppId => "development",
                _ => throw new InvalidOperationException()
            }),
            new SingleItem("preview", "0"),
            depotScripts);
        
        var scriptFileName = Path.Combine(ScriptPath, string.Format(appIdScriptFileFmt, appId));
        File.WriteAllText(scriptFileName, script.ToString());
    }
    
    public static void PrepareScripts(string configuration, Version version, string gitBranch, string gitRevision)
    {
        Console.WriteLine($"Preparing SteamPipe scripts for {configuration} v{version} ({gitBranch}, {gitRevision})");
        
        Util.RecreateDirectory(ScriptPath);
        
        PrepareAppScript(ClientAppId, configuration, version, gitBranch, gitRevision);
        PrepareAppScript(ServerAppId, configuration, version, gitBranch, gitRevision);

        Console.WriteLine("");
    }
    
    public static void Upload(string userName, string configuration)
    {
        Util.RecreateDirectory(BuildOutput);
        
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = Path.Combine(SteamCmdPath, steamCmdFilenames.First()),
            ArgumentList =
            {
                "+login",
                userName
            },
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        void addScriptCmd(ulong appId)
        {
            psi.ArgumentList.Add("+run_app_build");
            psi.ArgumentList.Add(Path.GetFullPath(Path.Combine(ScriptPath, string.Format(appIdScriptFileFmt, appId))));
        }
        addScriptCmd(ClientAppId);
        if (configuration == "Release") { addScriptCmd(ServerAppId); }
        
        psi.ArgumentList.Add("+quit");
        var process = Util.StartProcess(psi);
        process.WaitForExit();
    }
}