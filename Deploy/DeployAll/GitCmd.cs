using System;
using System.Diagnostics;

namespace DeployAll;

public static class GitCmd
{
    private const string gitCmdName = "git";

    private static ProcessStartInfo MakePsi(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = gitCmdName,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private static void ExecCmd(out string stdOut, out string stdErr, params string[] args)
    {
        var process = Util.StartProcess(MakePsi(args));
        process.WaitForExit();
        stdOut = process.StandardOutput.ReadToEnd();
        stdErr = process.StandardError.ReadToEnd();
    }
    
    public static string GetRevision()
    {
        ExecCmd(out string stdOut, out _,
            "rev-parse",
            "--short",
            "HEAD");
        
        return stdOut.Trim();
    }

    public static string GetBranch()
    {
        ExecCmd(out string stdOut, out _,
            "branch",
            "--show-current");

        return stdOut.Trim();
    }

    public static bool HasUncommittedChanges()
    {
        ExecCmd(out string stdOut, out _,
            "status",
            "--porcelain=1");

        return !string.IsNullOrWhiteSpace(stdOut);
    }

    public static bool IsRepoOutOfSync()
    {
        ExecCmd(out _, out _,
            "fetch");
        
        ExecCmd(out string remoteBranch, out _,
            "status",
            "-sb");
        
        if (!remoteBranch.StartsWith("##")) { return true; } 
        if (!remoteBranch.Contains("...")) { return true; }

        remoteBranch = remoteBranch[(remoteBranch.IndexOf("...", StringComparison.InvariantCulture) + 3)..];
        remoteBranch = remoteBranch[..remoteBranch.IndexOf("\n", StringComparison.InvariantCulture)];

        string localRevision = GetRevision();
        ExecCmd(out string remoteRevision, out _,
            "rev-parse",
            "--short",
            remoteBranch);
        remoteRevision = remoteRevision.Trim();

        return localRevision != remoteRevision;
    }
}