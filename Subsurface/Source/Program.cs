#region Using Statements

using System;
using System.IO;
using System.Reflection;
using System.Text;

#if WINDOWS
using System.Management;
using System.Windows.Forms;
using Microsoft.Xna.Framework.Graphics;
#endif

#endregion

namespace Barotrauma
{
#if WINDOWS || LINUX
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        private static int restartAttempts;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using (var game = new GameMain())
            {     
#if DEBUG
                game.Run();
#else
                bool attemptRestart = false;

                do
                {
                    try
                    {
                        game.Run();
                        attemptRestart = false;
                    }
                    catch (Exception e)
                    {                   
                        if (restartAttempts < 5 && CheckException(game, e))
                        {
                            attemptRestart = true;
                            restartAttempts++;
                        }
                        else
                        {
                            CrashDump(game, "crashreport.txt", e);
                        }

                    }
                } while (attemptRestart);
#endif
            }
        }

        private static bool CheckException(GameMain game, Exception e)
        {
            if (e is SharpDX.SharpDXException)
            {
                switch ((uint)((SharpDX.SharpDXException)e).ResultCode.Code)
                {
                    case 0x887A0022: //DXGI_ERROR_NOT_CURRENTLY_AVAILABLE
                        switch (restartAttempts)
                        {
                            case 0:
                                //just wait and try again
                                System.Threading.Thread.Sleep(100);
                                return true;
                            case 1:
                                //force focus to this window
                                var myForm = (System.Windows.Forms.Form)System.Windows.Forms.Form.FromHandle(game.Window.Handle);
                                myForm.Focus();
                                return true;
                            case 2:
                                //try disabling hardware mode switch
                                if (GameMain.Config.WindowMode == WindowMode.Fullscreen)
                                {
                                    DebugConsole.NewMessage("Failed to set fullscreen mode, switching configuration to borderless windowed", Microsoft.Xna.Framework.Color.Red);
                                    GameMain.Config.WindowMode = WindowMode.BorderlessWindowed;
                                    GameMain.Config.Save("config.xml");
                                }
                                return false;
                            default:
                                return false;
                            
                        }
                    case 0x80070057: //E_INVALIDARG/Invalid Arguments
                        DebugConsole.NewMessage("Invalid graphics settings, attempting to fix", Microsoft.Xna.Framework.Color.Red);

                        GameMain.Config.GraphicsWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                        GameMain.Config.GraphicsHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
                        game.ApplyGraphicsSettings();

                        return true;
                    default:
                        return false;
                }
            }
                
            return false;            
        }

        public static void CrashMessageBox(string message)
        {
#if WINDOWS
            MessageBox.Show(message, "Oops! Barotrauma just crashed.", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif

            Sounds.SoundManager.Dispose();           
        }

        static void CrashDump(GameMain game, string filePath, Exception exception)
        {
            StreamWriter sw = new StreamWriter(filePath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Barotrauma crash report (generated on " + DateTime.Now + ")");
            sb.AppendLine("\n");
            sb.AppendLine("Barotrauma seems to have crashed. Sorry for the inconvenience! ");
            sb.AppendLine("If you'd like to help fix the bug that caused the crash, please send this file to the developers on the Undertow Games forums.");
            sb.AppendLine("\n");
            sb.AppendLine("Game version " + GameMain.Version);
            sb.AppendLine("Graphics mode: " + GameMain.Config.GraphicsWidth + "x" + GameMain.Config.GraphicsHeight + " (" + GameMain.Config.WindowMode.ToString() + ")");
            sb.AppendLine("Selected content package: " + GameMain.SelectedPackage.Name);
            sb.AppendLine("Level seed: " + ((Level.Loaded == null) ? "no level loaded" : Level.Loaded.Seed));
            sb.AppendLine("Loaded submarine: " + ((Submarine.MainSub == null) ? "None" : Submarine.MainSub.Name + " (" + Submarine.MainSub.MD5Hash + ")"));
            sb.AppendLine("Selected screen: " + (Screen.Selected == null ? "None" : Screen.Selected.ToString()));

            if (GameMain.Server != null)
            {
                sb.AppendLine("Server (" + (GameMain.Server.GameStarted ? "Round had started)" : "Round hand't been started)"));
            }
            else if (GameMain.Client != null)
            {
                sb.AppendLine("Client (" + (GameMain.Client.GameStarted ? "Round had started)" : "Round hand't been started)"));
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
            for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 10; i-- )
            {
                sb.AppendLine("   "+DebugConsole.Messages[i].Time+" - "+DebugConsole.Messages[i].Text);
            }


            sw.WriteLine(sb.ToString());
            sw.Close(); 

            CrashMessageBox( "A crash report (\"crashreport.txt\") was saved in the root folder of the game."+
                " If you'd like to help fix this bug, please post the report on the Undertow Games forums.");       
        }
    }
#endif
}
