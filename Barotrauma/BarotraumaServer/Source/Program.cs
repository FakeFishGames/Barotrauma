#region Using Statements

using Barotrauma.Steam;
using GameAnalyticsSDK.Net;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

#endregion

namespace Barotrauma
{
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            GameMain game = null;
            Thread inputThread = null;

#if !DEBUG
            try
            {
#endif
                game = new GameMain(args);
                inputThread = new Thread(new ThreadStart(DebugConsole.UpdateCommandLine));
                inputThread.IsBackground = true;
                inputThread.Start();
                game.Run();
                inputThread.Abort(); inputThread.Join();
                if (GameSettings.SendUserStatistics) GameAnalytics.OnQuit();
                SteamManager.ShutDown();
#if !DEBUG
            }
            catch (Exception e)
            {
                CrashDump(game, "servercrashreport.log", e);
                inputThread.Abort(); inputThread.Join();
            }
#endif
        }
        
        static void CrashDump(GameMain game, string filePath, Exception exception)
        {
            int existingFiles = 0;
            string originalFilePath = filePath;
            while (File.Exists(filePath))
            {
                existingFiles++;
                filePath = Path.GetFileNameWithoutExtension(originalFilePath) + " (" + (existingFiles + 1) + ")" + Path.GetExtension(originalFilePath);
            }

            StreamWriter sw = new StreamWriter(filePath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Barotrauma Dedicated Server crash report (generated on " + DateTime.Now + ")");
            sb.AppendLine("\n");
            sb.AppendLine("Barotrauma seems to have crashed. Sorry for the inconvenience! ");
            sb.AppendLine("\n");
#if DEBUG
            sb.AppendLine("Game version " + GameMain.Version + " (debug build)");
#else
            sb.AppendLine("Game version " + GameMain.Version);
#endif
            sb.AppendLine("Selected content packages: " + (!GameMain.SelectedPackages.Any() ? "None" : string.Join(", ", GameMain.SelectedPackages.Select(c => c.Name))));
            sb.AppendLine("Level seed: " + ((Level.Loaded == null) ? "no level loaded" : Level.Loaded.Seed));
            sb.AppendLine("Loaded submarine: " + ((Submarine.MainSub == null) ? "None" : Submarine.MainSub.Name + " (" + Submarine.MainSub.MD5Hash + ")"));
            sb.AppendLine("Selected screen: " + (Screen.Selected == null ? "None" : Screen.Selected.ToString()));

            if (GameMain.Server != null)
            {
                sb.AppendLine("Server (" + (GameMain.Server.GameStarted ? "Round had started)" : "Round hadn't been started)"));
            }

            sb.AppendLine("\n");
            sb.AppendLine("System info:");
            sb.AppendLine("    Operating system: " + System.Environment.OSVersion + (System.Environment.Is64BitOperatingSystem ? " 64 bit" : " x86"));
            sb.AppendLine("\n");
            sb.AppendLine("Exception: "+exception.Message);
            sb.AppendLine("Target site: " +exception.TargetSite.ToString());
            sb.AppendLine("Stack trace: ");
            sb.AppendLine(exception.StackTrace);
            sb.AppendLine("\n");

            if (exception.InnerException != null)
            {
                sb.AppendLine("InnerException: " + exception.InnerException.Message);
                if (exception.InnerException.TargetSite != null)
                {
                    sb.AppendLine("Target site: " + exception.InnerException.TargetSite.ToString());
                }
                sb.AppendLine("Stack trace: ");
                sb.AppendLine(exception.InnerException.StackTrace);
            }

            sb.AppendLine("Last debug messages:");
            for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 15; i-- )
            {
                sb.AppendLine("   "+DebugConsole.Messages[i].Time+" - "+DebugConsole.Messages[i].Text);
            }

            string crashReport = sb.ToString();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(crashReport);

            sw.WriteLine(sb.ToString());
            sw.Close();

            if (GameSettings.SendUserStatistics)
            {
                GameAnalytics.AddErrorEvent(EGAErrorSeverity.Error, crashReport);
                GameAnalytics.OnQuit();
                Console.Write("A crash report (\"crashreport.log\") was saved in the root folder of the game and sent to the developers.");
            }
            else
            {
                Console.Write("A crash report(\"crashreport.log\") was saved in the root folder of the game. The error was not sent to the developers because user statistics have been disabled, but" +
                    " if you'd like to help fix this bug, you may post it on Barotrauma's GitHub issue tracker: https://github.com/Regalis11/Barotrauma/issues/");
            }
            SteamManager.ShutDown();
        }
    }
}
