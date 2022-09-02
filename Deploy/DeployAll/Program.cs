using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DeployAll;

while (!Directory.GetFiles(".").Any(f => f.EndsWith(".sln")))
{
    Directory.SetCurrentDirectory("..");
}

const string windowsClientProj = "Barotrauma/BarotraumaClient/WindowsClient.csproj";

Version gameVersion = Version.Parse(
    XDocument.Load(windowsClientProj).Root?
        .Element("PropertyGroup")?
        .Element("Version")?
        .Value ?? throw new Exception($"Version not found in {windowsClientProj}"));

string gitRevision = GitCmd.GetRevision();
string gitBranch = GitCmd.GetBranch();

Console.WriteLine($"DEPLOYALL - Barotrauma v{gameVersion}, branch {gitBranch}, revision {gitRevision}");

if (GitCmd.HasUncommittedChanges())
{
    if (Util.AskQuestion("The repo currently has some uncommitted changes. Do you still wish to proceed? [y/n]")
        .AnsweredNo()) { return; }
}
else if (GitCmd.IsRepoOutOfSync())
{
    if (Util.AskQuestion("The repo is currently out of sync. Do you still wish to proceed? [y/n]")
        .AnsweredNo()) { return; }
}

var sdkVersion = DotnetCmd.GetSdkVersion();
Console.WriteLine($"Using .NET SDK {sdkVersion}");

string configuration = Util.AskQuestion("Type 1 for Release, 2 for Unstable, enter nothing to cancel") switch
{
    "1" => "Release",
    "2" => "Unstable",
    _ => ""
};
if (string.IsNullOrWhiteSpace(configuration)) { return; }

Deployables.Generate(configuration, gameVersion, gitBranch, gitRevision);

if (Util.AskQuestion("Would you like to upload the generated builds to Steam? [y/n]")
    .AnsweredNo()) { return; }

SteamPipeAssistant.PrepareSteamCmd();
SteamPipeAssistant.PrepareScripts(configuration, gameVersion, gitBranch, gitRevision);

string userName = Util.AskQuestion("Type your Steam username to upload to Steamworks, enter nothing to skip uploading");
if (string.IsNullOrWhiteSpace(userName)) { return; }

SteamPipeAssistant.Upload(userName, configuration);
