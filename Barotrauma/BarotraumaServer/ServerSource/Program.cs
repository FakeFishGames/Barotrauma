#region Using Statements

using Barotrauma.Steam;
using GameAnalyticsSDK.Net;
using System;
using Barotrauma.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using NLog;

#endregion

namespace Barotrauma
{
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        private readonly static Logger Log = LogManager.GetCurrentClassLogger();

#if LINUX
        /// <summary>
        /// Sets the required environment variables for the game to initialize Steamworks correctly.
        /// </summary>
        [DllImport("linux_steam_env", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void setLinuxEnv();
#endif

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            LoggingUtils.InitializeLogging();

            if(!LoggingUtils.IsNLogConfigLoaded())
            {
                Console.WriteLine("Failed to load NLog.config; logging for this session will be disabled. Check for syntax errors in the XML.");
            }
#if !DEBUG
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
#endif

#if LINUX
            setLinuxEnv();
#endif
            Log.Info("Barotrauma Dedicated Server " + GameMain.Version +
                " (" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")");

            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            Directory.SetCurrentDirectory(executableDir);
            Game = new GameMain(args);

            Game.Run();
            if (GameSettings.SendUserStatistics) { GameAnalytics.OnQuit(); }
            SteamManager.ShutDown();

            LoggingUtils.ShutdownLogging();
        }

        static GameMain Game;

        private static void CrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            try
            {
                Log.Fatal(args.ExceptionObject as Exception, "Encountered unhandled exception!");

                Game?.Exit();
                CrashDump("servercrashreport.log", (Exception)args.ExceptionObject);
                GameMain.Server?.NotifyCrash();
            }
            catch (Exception e)
            {
                Log.Fatal(e, "Encountered an exception when handling the crash dump.");
                //exception handler is broken, we have a serious problem here!!
                return;
            }
            finally
            {
                LoggingUtils.ShutdownLogging();
            }
        }

        static void CrashDump(string filePath, Exception exception)
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
            if (GameMain.Config != null)
            {
                sb.AppendLine("Language: " + (GameMain.Config.Language ?? "none"));
            }
            if (GameMain.Config.AllEnabledPackages != null)
            {
                sb.AppendLine("Selected content packages: " + (!GameMain.Config.AllEnabledPackages.Any() ? "None" : string.Join(", ", GameMain.Config.AllEnabledPackages.Select(c => c.Name))));
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

            sb.AppendLine("Last debug messages:");
            LoggingUtils.AppendLastLogMessages(sb);

            string crashReport = sb.ToString();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(crashReport);

            File.WriteAllText(filePath,sb.ToString());

            if (GameSettings.SendUserStatistics)
            {
                GameAnalytics.AddErrorEvent(EGAErrorSeverity.Critical, crashReport);
                GameAnalytics.OnQuit();
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
