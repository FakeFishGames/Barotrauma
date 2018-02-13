#region Using Statements

using System;
using System.IO;
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
        public static string[] CommandLineArgs = Environment.GetCommandLineArgs();

        static GameMain game = null;
        public static Thread inputThread = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnrecoverableCrashHandler);
            


            
            try
            {
                game = new GameMain();
                inputThread = new Thread(new ThreadStart(game.ProcessInput));
                inputThread.Start();
                game.Run();
                inputThread.Abort(); inputThread.Join();
            }
            catch (Exception e)
            {
                string CrashName = "crashreport" + "_" + DateTime.Now.ToShortDateString() + "_" + DateTime.Now.ToShortTimeString() + ".txt";

                CrashName = CrashName.Replace(":", "");
                CrashName = CrashName.Replace("../", "");
                CrashName = CrashName.Replace("/", "");
                CrashDump(game, CrashName, e);
                //inputThread.Abort(); inputThread.Join();
            }
        }

        static void UnrecoverableCrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            if (GameMain.NilMod != null)
            {
                if (GameMain.NilMod.CrashRestart)
                {
                    Exception e = (Exception)args.ExceptionObject;
                    //DebugConsole.NewMessage("Unhandled Exception Caught (Program has crashed!) : " + e.Message, Microsoft.Xna.Framework.Color.Red);
                    if (GameMain.Server != null)
                    {
                        GameMain.Server.ServerLog.WriteLine("Server Has Suffered a fatal Crash (Autorestarting).", Networking.ServerLog.MessageType.Error);
                        GameMain.Server.ServerLog.Save();
                    }
                    
                    System.Diagnostics.Process.Start(System.Reflection.Assembly.GetEntryAssembly().CodeBase + "\\BarotraumaServer NilEdit.exe");
                    //Kind of flipping the table here after the last one or two didnt work.
                    inputThread.Abort(); inputThread.Join();
                    Environment.Exit(-1);
                }
            }
        }

        static void CrashDump(GameMain game, string filePath, Exception exception)
        {
            StreamWriter sw = new StreamWriter(filePath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Barotrauma Dedicated Server crash report (generated on " + DateTime.Now + ")");
            sb.AppendLine("\n");
            sb.AppendLine("Barotrauma seems to have crashed. Sorry for the inconvenience! ");
            sb.AppendLine("If you'd like to help fix the bug that caused the crash, please send this file to the developers on the Undertow Games forums.");
            sb.AppendLine("\n");
            sb.AppendLine("Game version " + GameMain.Version + " NILMOD SERVER MODIFICATION");
            //sb.AppendLine("Selected content package: " + GameMain.SelectedPackage.Name);
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
            sb.AppendLine("This was running NilMod Code!");
            sb.AppendLine("\n");
            sb.AppendLine("Exception: "+exception.Message);
            sb.AppendLine("Target site: " +exception.TargetSite.ToString());
            sb.AppendLine("Stack trace: ");
            sb.AppendLine(exception.StackTrace);
            sb.AppendLine("\n");

            sb.AppendLine("Last debug messages:");
            for (int i = DebugConsole.Messages.Count - 1; i > 0; i--)
            {
                sb.AppendLine("   " + DebugConsole.Messages[i].Time + " - " + DebugConsole.Messages[i].Text);
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(sb.ToString());

            sw.WriteLine(sb.ToString());
            sw.Close();    
        }
    }
}
