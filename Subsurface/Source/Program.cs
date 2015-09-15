#region Using Statements

using System;
using System.IO;
using System.Reflection;
using System.Text;

#if WINDOWS
using System.Management;
using System.Windows.Forms;
#endif

#endregion

namespace Subsurface
{
#if WINDOWS || LINUX
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using (var game = new Game1())
            {
#if !DEBUG
                try
                {
#endif

                    game.Run();
#if !DEBUG
                }
                catch (Exception e)
                {
                    CrashDump(game, "crashreport.txt", e);
                }
#endif
            }
        }

        static void CrashDump(Game1 game, string filePath, Exception exception)
        {
            StreamWriter sw = new StreamWriter(filePath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Subsurface crash report (generated on " + DateTime.Now + ")");
            sb.AppendLine("\n");
            sb.AppendLine("Subsurface seems to have crashed. Sorry for the inconvenience! ");
            sb.AppendLine("If you'd like to help fix the bug that caused the crash, please send this file to the developers on the Undertow Games forums.");
            sb.AppendLine("\n");
            sb.AppendLine("Subsurface version " + Game1.Version);
            sb.AppendLine("Selected content package: "+Game1.SelectedPackage.Name);
            sb.AppendLine("Level seed: "+ ((Level.Loaded == null) ? "no level loaded" : Level.Loaded.Seed));
            sb.AppendLine("Loaded submarine: " + ((Submarine.Loaded == null) ? "none" : Submarine.Loaded.Name +" ("+Submarine.Loaded.MD5Hash+")"));
            sb.AppendLine("Selected screen: " + Screen.Selected.ToString());

            if (Game1.Server != null)
            {
                sb.AppendLine("Server (" +(Game1.Server.GameStarted ? "Round had started)" : "Round hand't been started)"));
            }
            else if (Game1.Client != null)
            {
                sb.AppendLine("Client (" +(Game1.Client.GameStarted ? "Round had started)" : "Round hand't been started)"));
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

            sb.AppendLine("Last debug messages:");
            for (int i = DebugConsole.messages.Count - 1; i > 0 && i > DebugConsole.messages.Count - 10; i-- )
            {
                sb.AppendLine("   "+DebugConsole.messages[i].Time+" - "+DebugConsole.messages[i].Text);
            }


            sw.WriteLine(sb.ToString());
            sw.Close(); 

    #if WINDOWS
            MessageBox.Show( "A crash report (''crashreport.txt'') was saved in the root folder of the game."+
                " If you'd like to help fix this bug, please post the report on the Undertow Games forums.",
                "Oops! Subsurface just crashed.", MessageBoxButtons.OK, MessageBoxIcon.Error);
    #endif

                       
        }
    }
#endif
}
