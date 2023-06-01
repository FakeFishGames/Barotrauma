﻿#region Using Statements

using Barotrauma.Steam;
using System;
using Barotrauma.IO;
using System.Linq;
using System.Text;
using Barotrauma.Networking;
#if LINUX
using System.Runtime.InteropServices;
#endif

#endregion

namespace Barotrauma
{
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
#if LINUX
        /// <summary>
        /// Sets the required environment variables for the game to initialize Steamworks correctly.
        /// </summary>
        [DllImport("linux_steam_env", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void setLinuxEnv();
#endif

        public static bool TryStartChildServerRelay(string[] commandLineArgs)
        {            
            for (int i = 0; i < commandLineArgs.Length; i++)
            {
                switch (commandLineArgs[i].Trim())
                {
                    case "-pipes":
                        ChildServerRelay.Start(commandLineArgs[i + 2], commandLineArgs[i + 1]);
                        return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
#if !DEBUG
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
#endif
            TryStartChildServerRelay(args);

#if LINUX
            setLinuxEnv();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => 
            {
                GameMain.ShouldRun = false;
            };
#endif
            Console.WriteLine("Barotrauma Dedicated Server " + GameMain.Version +
                " (" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")");
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine("Output redirection detected; colored text and command input will be disabled.");
            }
            if (Console.IsInputRedirected)
            {
                Console.WriteLine("Redirected input is detected but is not supported by this application. Input will be ignored.");
            }

            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            Directory.SetCurrentDirectory(executableDir);
            Game = new GameMain(args);

            Game.Run();
            if (GameAnalyticsManager.SendUserStatistics) { GameAnalyticsManager.ShutDown(); }
            SteamManager.ShutDown();
        }

        static GameMain Game;

        private static void NotifyCrash(string reportFilePath, Exception e)
        {
            string errorMsg = $"{reportFilePath}||\n{e.Message} ({e.GetType().Name}) {e.StackTrace}";
            if (e.InnerException != null)
            {
                var innerMost = e.GetInnermost();
                errorMsg += $"\nInner exception: {innerMost.Message} ({innerMost.GetType().Name}) {e.StackTrace}";
            }
            if (errorMsg.Length > ushort.MaxValue) { errorMsg = errorMsg[..ushort.MaxValue]; }
            ChildServerRelay.NotifyCrash(errorMsg);
            GameMain.Server?.NotifyCrash();
        }
        
        private static void CrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            static void swallowExceptions(Action action)
            {
                try
                {
                    action();
                }
                catch
                {
                    //discard exceptions and keep going
                }
            }

            string reportFilePath = "";
            try
            {
                reportFilePath = "servercrashreport.log";
                CrashDump(ref reportFilePath, (Exception)args.ExceptionObject);
            }
            catch
            {
                //fuck
                reportFilePath = "";
            }
            swallowExceptions(() => NotifyCrash(reportFilePath, (Exception)args.ExceptionObject));
            swallowExceptions(() => Game?.Exit());
        }

        static void CrashDump(ref string filePath, Exception exception)
        {
            try
            {
                GameMain.Server?.ServerSettings?.SaveSettings();
                GameMain.Server?.ServerSettings?.BanList.Save();
                if (GameMain.Server?.ServerSettings?.KarmaPreset == "custom")
                {
                    GameMain.Server?.KarmaManager?.SaveCustomPreset();
                    GameMain.Server?.KarmaManager?.Save();
                }
            }
            catch (Exception e)
            {
                //couldn't save, whatever
            }

            int existingFiles = 0;
            string originalFilePath = filePath;
            while (File.Exists(filePath))
            {
                existingFiles++;
                filePath = Path.GetFileNameWithoutExtension(originalFilePath) + " (" + (existingFiles + 1) + ")" + Path.GetExtension(originalFilePath);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Barotrauma Dedicated Server crash report (generated on " + DateTime.Now + ")");
            sb.AppendLine("\n");
            sb.AppendLine("Barotrauma seems to have crashed. Sorry for the inconvenience! ");
            sb.AppendLine("\n");
            sb.AppendLine("Game version " + GameMain.Version + " (" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")");
            sb.AppendLine("Language: " + GameSettings.CurrentConfig.Language);
            if (ContentPackageManager.EnabledPackages.All != null)
            {
                sb.AppendLine("Selected content packages: " +
                    (!ContentPackageManager.EnabledPackages.All.Any() ?
                        "None" :
                        string.Join(", ", ContentPackageManager.EnabledPackages.All.Select(c => $"{c.Name} ({c.Hash?.ShortRepresentation ?? "unknown"})"))));
            }
            sb.AppendLine("Level seed: " + ((Level.Loaded == null) ? "no level loaded" : Level.Loaded.Seed));
            sb.AppendLine("Loaded submarine: " + ((Submarine.MainSub == null) ? "None" : Submarine.MainSub.Info.Name + " (" + Submarine.MainSub.Info.MD5Hash + ")"));
            sb.AppendLine("Selected screen: " + (Screen.Selected == null ? "None" : Screen.Selected.ToString()));

            if (GameMain.Server != null)
            {
                sb.AppendLine("Server (" + (GameMain.Server.GameStarted ? "Round had started)" : "Round hadn't been started)"));
            }

            sb.AppendLine("\n");
            sb.AppendLine("System info:");
            sb.AppendLine("    Operating system: " + System.Environment.OSVersion + (System.Environment.Is64BitOperatingSystem ? " 64 bit" : " x86"));
            sb.AppendLine("\n");
            sb.AppendLine("Exception: " + exception.Message + " (" + exception.GetType().ToString() + ")");
            sb.AppendLine("Target site: " +exception.TargetSite.ToString());
            if (exception.StackTrace != null)
            {
                sb.AppendLine("Stack trace: ");
                sb.AppendLine(exception.StackTrace.CleanupStackTrace());
                sb.AppendLine("\n");
            }

            if (exception.InnerException != null)
            {
                sb.AppendLine("InnerException: " + exception.InnerException.Message);
                if (exception.InnerException.TargetSite != null)
                {
                    sb.AppendLine("Target site: " + exception.InnerException.TargetSite.ToString());
                }
                if (exception.InnerException.StackTrace != null)
                {
                    sb.AppendLine("Stack trace: ");
                    sb.AppendLine(exception.InnerException.StackTrace.CleanupStackTrace());
                }
            }

            if (GameAnalyticsManager.SendUserStatistics)
            {
                //send crash report before appending debug console messages (which may contain non-anonymous information)
                GameAnalyticsManager.AddErrorEvent(GameAnalyticsManager.ErrorSeverity.Critical, sb.ToString());
                GameAnalyticsManager.ShutDown();
            }

            sb.AppendLine("Last debug messages:");
            DebugConsole.Clear();
            for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 15; i--)
            {
                sb.AppendLine("   " + DebugConsole.Messages[i].Time + " - " + DebugConsole.Messages[i].Text);
            }

            string crashReport = sb.ToString();

            if (!Console.IsOutputRedirected)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            Console.Write(crashReport);

            File.WriteAllText(filePath, sb.ToString());

            if (GameSettings.CurrentConfig.SaveDebugConsoleLogs
                || GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.SaveLogs(); }

            if (GameAnalyticsManager.SendUserStatistics)
            {
                Console.Write("A crash report (\"servercrashreport.log\") was saved in the root folder of the game and sent to the developers.");
            }
            else
            {
                Console.Write("A crash report(\"servercrashreport.log\") was saved in the root folder of the game. The error was not sent to the developers because user statistics have been disabled, but" +
                    " if you'd like to help fix this bug, you may post it on Barotrauma's GitHub issue tracker: https://github.com/Regalis11/Barotrauma/issues/");
            }
            SteamManager.ShutDown();
        }
    }
}
