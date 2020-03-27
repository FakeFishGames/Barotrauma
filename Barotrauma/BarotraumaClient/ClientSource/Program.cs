#region Using Statements

using System;
using System.IO;
using System.Linq;
using System.Text;
using GameAnalyticsSDK.Net;
using Barotrauma.Steam;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

#if WINDOWS
using SharpDX;
#endif

#endregion

namespace Barotrauma
{
#if WINDOWS || LINUX || OSX
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

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            string executableDir = "";

#if !DEBUG
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
#endif

#if LINUX
            setLinuxEnv();
#endif

            Game = null;
            executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            Directory.SetCurrentDirectory(executableDir);
            SteamManager.Initialize();
            Game = new GameMain(args);
            Game.Run();
            Game.Dispose();
        }

        private static GameMain Game;

        private static void CrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            try
            {
                Game?.Exit();
                CrashDump(Game, "crashreport.log", (Exception)args.ExceptionObject);
                Game?.Dispose();
            }
            catch
            {
                //exception handler is broken, we have a serious problem here!!
                return;
            }
        }

        public static void CrashMessageBox(string message, string filePath)
        {
            Microsoft.Xna.Framework.MessageBox.ShowWrapped(Microsoft.Xna.Framework.MessageBox.Flags.Error, "Oops! Barotrauma just crashed.", message);

            // Open the crash log.
            if (!string.IsNullOrWhiteSpace(filePath)) { ToolBox.OpenFileWithShell(filePath); }
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

            DebugConsole.DequeueMessages();

            string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
            var md5 = System.Security.Cryptography.MD5.Create();
            Md5Hash exeHash = null;
            using (var stream = File.OpenRead(exePath))
            {
                exeHash = new Md5Hash(stream);
            }

            StreamWriter sw = new StreamWriter(filePath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Barotrauma Client crash report (generated on " + DateTime.Now + ")");
            sb.AppendLine("\n");
            sb.AppendLine("Barotrauma seems to have crashed. Sorry for the inconvenience! ");
            sb.AppendLine("\n");

            try
            {
                if (exception is GameMain.LoadingException)
                {
                    //exception occurred in loading screen:
                    //assume content packages are the culprit and reset them
                    XDocument doc = XMLExtensions.TryLoadXml(GameSettings.PlayerSavePath);
                    XDocument baseDoc = XMLExtensions.TryLoadXml(GameSettings.SavePath);
                    if (doc != null && baseDoc != null)
                    {
                        XElement newElement = new XElement(doc.Root.Name);
                        newElement.Add(doc.Root.Attributes());
                        newElement.Add(doc.Root.Elements().Where(e => !e.Name.LocalName.Equals("contentpackage", StringComparison.InvariantCultureIgnoreCase)));
                        newElement.Add(baseDoc.Root.Elements().Where(e => e.Name.LocalName.Equals("contentpackage", StringComparison.InvariantCultureIgnoreCase)));
                        XDocument newDoc = new XDocument(newElement);
                        newDoc.Save(GameSettings.PlayerSavePath);
                        sb.AppendLine("To prevent further startup errors, installed mods will be disabled the next time you launch the game.");
                        sb.AppendLine("\n");
                    }
                }
            }
            catch
            {
                //welp i guess we couldn't reset the config!
            }

            if (exeHash?.Hash != null)
            {
                sb.AppendLine(exeHash.Hash);
            }
            sb.AppendLine("\n");
            sb.AppendLine("Game version " + GameMain.Version +
            " (" + AssemblyInfo.GetBuildString() + ", branch " + AssemblyInfo.GetGitBranch() + ", revision " + AssemblyInfo.GetGitRevision() + ")");
            if (GameMain.Config != null)
            {
                sb.AppendLine("Graphics mode: " + GameMain.Config.GraphicsWidth + "x" + GameMain.Config.GraphicsHeight + " (" + GameMain.Config.WindowMode.ToString() + ")");
                sb.AppendLine("VSync "+ (GameMain.Config.VSyncEnabled ? "ON" : "OFF"));
            }
            if (GameMain.SelectedPackages != null)
            {
                sb.AppendLine("Selected content packages: " + (!GameMain.SelectedPackages.Any() ? "None" : string.Join(", ", GameMain.SelectedPackages.Select(c => c.Name))));
            }
            sb.AppendLine("Level seed: " + ((Level.Loaded == null) ? "no level loaded" : Level.Loaded.Seed));
            sb.AppendLine("Loaded submarine: " + ((Submarine.MainSub == null) ? "None" : Submarine.MainSub.Info.Name + " (" + Submarine.MainSub.Info.MD5Hash + ")"));
            sb.AppendLine("Selected screen: " + (Screen.Selected == null ? "None" : Screen.Selected.ToString()));
            if (SteamManager.IsInitialized)
            {
                sb.AppendLine("SteamManager initialized");
            }

            if (GameMain.Client != null)
            {
                sb.AppendLine("Client (" + (GameMain.Client.GameStarted ? "Round had started)" : "Round hadn't been started)"));
            }

            sb.AppendLine("\n");
            sb.AppendLine("System info:");
            sb.AppendLine("    Operating system: " + System.Environment.OSVersion + (System.Environment.Is64BitOperatingSystem ? " 64 bit" : " x86"));

            if (game == null)
            {
                sb.AppendLine("    Game not initialized");
            }
            else
            {
                if (game.GraphicsDevice == null)
                {
                    sb.AppendLine("    Graphics device not set");
                }
                else
                {
                    if (game.GraphicsDevice.Adapter == null)
                    {
                        sb.AppendLine("    Graphics adapter not set");
                    }
                    else
                    {
                        sb.AppendLine("    GPU name: " + game.GraphicsDevice.Adapter.Description);
                        sb.AppendLine("    Display mode: " + game.GraphicsDevice.Adapter.CurrentDisplayMode);
                    }

                    sb.AppendLine("    GPU status: " + game.GraphicsDevice.GraphicsDeviceStatus);
                }
            }

            sb.AppendLine("\n");
            sb.AppendLine("Exception: " + exception.Message + " (" + exception.GetType().ToString() + ")");
#if WINDOWS
            if (exception is SharpDXException sharpDxException && ((uint)sharpDxException.HResult) == 0x887A0005)
            {
                var dxDevice = (SharpDX.Direct3D11.Device)game.GraphicsDevice.Handle;
                sb.AppendLine("Device removed reason: " + dxDevice.DeviceRemovedReason.ToString());
            }
#endif
            if (exception.TargetSite != null)
            {
                sb.AppendLine("Target site: " + exception.TargetSite.ToString());
            }

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
            for (int i = DebugConsole.Messages.Count - 1; i >= 0; i--)
            {
                sb.AppendLine("[" + DebugConsole.Messages[i].Time + "] " + DebugConsole.Messages[i].Text);
            }

            string crashReport = sb.ToString();

            sw.WriteLine(crashReport);
            sw.Close();

            if (GameSettings.SaveDebugConsoleLogs) DebugConsole.SaveLogs();

            if (GameSettings.SendUserStatistics)
            {
                CrashMessageBox("A crash report (\"" + filePath + "\") was saved in the root folder of the game and sent to the developers.", filePath);
                GameAnalytics.AddErrorEvent(EGAErrorSeverity.Critical, crashReport);
                GameAnalytics.OnQuit();
            }
            else
            {
                CrashMessageBox("A crash report (\"" + filePath + "\") was saved in the root folder of the game. The error was not sent to the developers because user statistics have been disabled, but" +
                    " if you'd like to help fix this bug, you may post it on Barotrauma's GitHub issue tracker: https://github.com/Regalis11/Barotrauma/issues/", filePath);
            }
        }
    }
#endif
        }
