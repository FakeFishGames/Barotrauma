using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System;
using System.Net;
using System.ComponentModel;
using RestSharp;
using System.Text;

namespace Launcher
{
    /// <summary>
    /// This is the main type for your game
    /// </summary>
    public class LauncherMain : Game
    {
        string version = AssemblyName.GetAssemblyName("Barotrauma.exe").Version.ToString();

        private GameSettings settings;

        private string latestVersionFileList, latestVersionFolder;

        private int updateCheckState;

        private List<DisplayMode> supportedModes;

        private GUIDropDown resolutionDD, displayModeDD;
        private GUIListBox contentPackageList;

        private GUITextBlock updateInfoText;
        private GUIListBox updateInfoBox;
        private GUIProgressBar progressBar;
        private GUIButton downloadButton;

        private GUIButton launchButton;

        public bool AutoCheckUpdates
        {
            get { return settings.AutoCheckUpdates; }
            set { settings.AutoCheckUpdates = value; }
        }

        private Texture2D backgroundTexture, titleTexture;

        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;

        private RasterizerState scissorTestEnable;

        private int graphicsWidth, graphicsHeight;

        private GUIFrame guiRoot;

        public LauncherMain()
            : base()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 640,
                PreferredBackBufferHeight = 400
            };

            scissorTestEnable = new RasterizerState() { ScissorTestEnable = true };
            supportedModes = new List<DisplayMode>();
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            ContentPackage.LoadAll(ContentPackage.Folder);
            settings = new GameSettings();

            base.Initialize();
        }
        
        protected override void LoadContent()
        {
            graphicsWidth = GraphicsDevice.Viewport.Width;
            graphicsHeight = GraphicsDevice.Viewport.Height;

            //for whatever reason, window isn't centered automatically
            //since MonoGame 3.6 (nuget package might be broken), so
            //let's do it manually
            Window.Position = new Point((GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width - graphicsWidth) / 2,
                                        (GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height - graphicsHeight) / 2);

            TextureLoader.Init(GraphicsDevice);

            GUI.Init(Window, settings.SelectedContentPackages, GraphicsDevice);
            GUICanvas.Instance.NonScaledSize = new Point(graphicsWidth, graphicsHeight);

            // Create a new SpriteBatch, which can be used to draw textures.
            spriteBatch = new SpriteBatch(GraphicsDevice);

            GUI.LoadContent(false);

            backgroundTexture = TextureLoader.FromFile("Content/UI/titleBackground.png");
            titleTexture = TextureLoader.FromFile("Content/UI/titleText.png");

            guiRoot = new GUIFrame(new RectTransform(new Point(graphicsWidth, graphicsHeight), null), style: null);
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.75f), guiRoot.RectTransform, Anchor.Center) { AbsoluteOffset = new Point(0, 25) },
                style: null);

            launchButton = new GUIButton(new RectTransform(new Point(100, 30), paddedFrame.RectTransform, Anchor.BottomRight), "START")
            {
                OnClicked = LaunchClick
            };

            int y = 0;
            var checkForUpdates = new GUITickBox(new RectTransform(new Point(20, 20), paddedFrame.RectTransform), "Automatically check for updates")
            {
                Selected = settings.AutoCheckUpdates
            };

            updateInfoText = new GUITextBlock(new RectTransform(new Point(20, 20), paddedFrame.RectTransform) { AbsoluteOffset = new Point(0,30) }, "");

            updateInfoBox = new GUIListBox(new RectTransform(new Point(330, 210), paddedFrame.RectTransform) { AbsoluteOffset = new Point(0, 55) })
            {
                Visible = false
            };

            progressBar = new GUIProgressBar(new RectTransform(new Point(220, 20), paddedFrame.RectTransform, Anchor.BottomLeft) { AbsoluteOffset = new Point(110, 0) },
                0.0f, Color.Green)
            {
                Visible = false
            };

            downloadButton = new GUIButton(new RectTransform(new Point(100, 20), paddedFrame.RectTransform, Anchor.BottomLeft), "Download")
            {
                OnClicked = DownloadButtonClicked,
                Visible = false
            };

            //-----------------------------------------------------------------
            //-----------------------------------------------------------------

            int x = 360;
            new GUITextBlock(new RectTransform(new Point(20, 20), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y) }, "Resolution");
            resolutionDD = new GUIDropDown(new RectTransform(new Point(200, 20), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y + 20) });

            foreach (DisplayMode mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
            {
                if (supportedModes.FirstOrDefault(m => m.Width == mode.Width && m.Height == mode.Height) != null) continue;

                resolutionDD.AddItem(mode.Width + "x" + mode.Height, mode);
                supportedModes.Add(mode);

                if (settings.GraphicsWidth == mode.Width && settings.GraphicsHeight == mode.Height) resolutionDD.SelectItem(mode);
            }

            if (resolutionDD.SelectedItemData == null)
            {
                resolutionDD.SelectItem(GraphicsAdapter.DefaultAdapter.SupportedDisplayModes.Last());
            }
                        
            new GUITextBlock(new RectTransform(new Point(20, 20), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y + 50) }, "Display mode");
            displayModeDD = new GUIDropDown(new RectTransform(new Point(200, 20), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y + 70) });
            displayModeDD.AddItem("Fullscreen", WindowMode.Fullscreen);
            displayModeDD.AddItem("Windowed", WindowMode.Windowed);
#if (!OSX)
            // Fullscreen option just sets itself to borderless on macOS.
            displayModeDD.AddItem(TextManager.Get("BorderlessWindowed"), WindowMode.BorderlessWindowed);
            displayModeDD.SelectItem(settings.WindowMode);
#else
            if (settings.WindowMode == WindowMode.BorderlessWindowed)
            {
                displayModeDD.SelectItem(WindowMode.Fullscreen);
            }
            else
            {
                displayModeDD.SelectItem(settings.WindowMode);
            }
#endif
            displayModeDD.OnSelected = (guiComponent, userData) => { settings.WindowMode = (WindowMode)guiComponent.UserData; return true; };

            new GUITextBlock(new RectTransform(new Point(20, 20), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y + 100) }, "Content packages");
            contentPackageList = new GUIListBox(new RectTransform(new Point(200, 120), paddedFrame.RectTransform) { AbsoluteOffset = new Point(x, y + 120) });

            foreach (ContentPackage contentPackage in ContentPackage.List)
            {
                new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), contentPackageList.Content.RectTransform, minSize: new Point(0, 15)), contentPackage.Name)
                {
                    UserData = contentPackage,
                    OnSelected = SelectContentPackage,
                    Selected = settings.SelectedContentPackages.Contains(contentPackage)
                };
            }
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            GUI.ClearUpdateList();

            if (settings.AutoCheckUpdates)
            {
                if (updateCheckState == 0)
                {
                    updateInfoText.Text = "Checking for updates...";
                    updateCheckState++;
                }
                else if (updateCheckState == 2)
                {
                    CheckForUpdates();
                    updateCheckState++;
                }
            }

            base.Update(gameTime);

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            PlayerInput.Update(deltaTime);
            
            guiRoot.AddToGUIUpdateList();
            GUI.Update(deltaTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, scissorTestEnable);

            spriteBatch.Draw(backgroundTexture,
                new Rectangle(0, 0, graphicsWidth, graphicsHeight),
                new Rectangle(635, 995, graphicsWidth, graphicsHeight),
                Color.White);

            spriteBatch.Draw(titleTexture, new Vector2(40.0f, 20.0f), null, Color.White, 0.0f, Vector2.Zero, new Vector2(0.2f, 0.2f), SpriteEffects.None, 0.0f);
            
            GUI.Draw(null, spriteBatch);

            spriteBatch.End();

            
            if (updateCheckState == 1) updateCheckState = 2;
        }

        private bool TrySaveSettings()
        {
            DisplayMode selectedMode = resolutionDD.SelectedItemData as DisplayMode;
            if (selectedMode == null)
            {
                resolutionDD.Flash();
                return false;
            }

            settings.GraphicsWidth = selectedMode.Width;
            settings.GraphicsHeight = selectedMode.Height;
            settings.SaveNewPlayerConfig();

            return true;
        }

        private bool SelectContentPackage(GUITickBox tickBox)
        {
            var contentPackage = tickBox.UserData as ContentPackage;
            if (contentPackage.CorePackage)
            {
                if (tickBox.Selected)
                {
                    //make sure no other core packages are selected
                    settings.SelectedContentPackages.RemoveWhere(cp => cp.CorePackage && cp != contentPackage);
                    settings.SelectedContentPackages.Add(contentPackage);
                    foreach (GUITickBox otherTickBox in tickBox.Parent.Children)
                    {
                        otherTickBox.Selected = settings.SelectedContentPackages.Contains(otherTickBox.UserData as ContentPackage);
                    }
                }
                else
                {
                    //core packages cannot be deselected, only switched by selecting another core package
                    new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("CorePackageRequiredWarning"));
                    tickBox.Selected = true;
                }
            }
            else
            {
                if (tickBox.Selected)
                {
                    settings.SelectedContentPackages.Add(contentPackage);
                }
                else
                {
                    settings.SelectedContentPackages.Remove(contentPackage);
                }
            }
            return true;
        }

        private bool LaunchClick(GUIButton button, object obj)
        {
            if (!TrySaveSettings()) return false;
            
            var executables = ContentPackage.GetFilesOfType(settings.SelectedContentPackages, ContentType.Executable);
            if (!executables.Any())
            {
                ShowError("Error", "The game executable isn't configured in the selected content package.");
                return false;
            }

            string exePath = Path.Combine(Directory.GetCurrentDirectory(), executables.First());
            if (!File.Exists(exePath))
            {
                ShowError("Error", "Couldn't find the executable \"" + exePath + "\"!");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exePath));
            }
            catch (Exception exception)
            {
                ShowError("Error while opening executable \"" + exePath + "\"", exception.Message);
                return false;
            }
            
            Exit();

            return true;
        }

        private void SetUpdateInfoBox(string text)
        {
            updateInfoBox.ClearChildren();

            string[] lines = text.Split('\n');
            foreach (string line in lines)
            {
                int indent = 10, height = 0;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (line[0] == '-') indent = 20;
                }
                else
                {
                    height = 5;
                }

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Point(updateInfoBox.Content.Rect.Width - 20, height), updateInfoBox.Content.RectTransform) { AbsoluteOffset = new Point(indent, 0) },
                    line, font: GUI.SmallFont, wrap: true)
                {
                    TextColor = indent > 10 ? Color.LightGray : Color.White,
                    CanBeFocused = false
                };
            }
            updateInfoBox.UpdateScrollBarSize();
        }

        private bool CheckForUpdates()
        {
            if (string.IsNullOrWhiteSpace(settings.MasterServerUrl))
            {
                updateInfoText.Text = "Checking updates failed";
                updateInfoBox.Visible = true;
                SetUpdateInfoBox("Update server URL not set");
                return false;
            
            }

            updateInfoText.Text = "Checking for updates...";

            XDocument doc = null;

            try
            {
                doc = FetchXML("versioninfo.xml");
            }

            catch (Exception e)
            {
                updateInfoText.Text = "Checking updates failed";
                updateInfoBox.Visible = true;

                SetUpdateInfoBox("Error while checking for updates: " + e.Message);
                return false;
            }

            if (doc == null)
            {
                updateInfoText.Text = "Checking updates failed";
                updateInfoBox.Visible = true;
                return false;
            }
        
            CheckUpdateXML(doc);

            return true;
        }

        private XDocument FetchXML(string fileName)
        {
            var client = new RestClient(settings.MasterServerUrl);

            var request = new RestRequest(fileName, Method.GET);

            IRestResponse response = client.Execute(request);

            if (response.ResponseStatus != ResponseStatus.Completed)
            {
                throw new Exception("Couldn't connect to update server");               
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Couldn't connect to update server ("+response.StatusCode+")");
            }

            string xml = response.Content;
            
            try
            {
                return XDocument.Parse(xml);
            }

            catch
            {
                int index = xml.IndexOf('<');
                xml = xml.Substring(index, xml.Length - index);

                return XDocument.Parse(xml);
            }
        }

        private bool CheckUpdateXML(XDocument doc)
        {
            if (doc.Root == null)
            {
                updateInfoText.Text = "Error while checking for updates: could not parse update info";
                return false;
            }

            Version currentVersion = new Version(version);


            string latestVersionStr = doc.Root.GetAttributeString("latestversion", "");
            latestVersionFolder = doc.Root.GetAttributeString("latestversionfolder", "");
            latestVersionFileList = doc.Root.GetAttributeString("latestversionfilelist", "");


            Version latestVersion = new Version(latestVersionStr);

            if (currentVersion.CompareTo(latestVersion) >= 0)
            {
                updateInfoText.Text =  "Your game is up to date!";
                return false;
            }

            progressBar.Visible = true;
            downloadButton.Visible = true;
            updateInfoBox.Visible = true;

            updateInfoText.Text = "New update found! (" + latestVersion + ")";

            XElement patchNotes = doc.Root.Element("patchnotes");

            if (patchNotes != null)
            {
                StringBuilder sb = new StringBuilder();

                foreach (XElement patchNote in patchNotes.Elements())
                {
                    string patchNumber = patchNote.GetAttributeString("version", "");

                    //read the patch notes until we reach the user's version
                    if (patchNumber == version) break;

                    Version patchVersion = new Version(patchNumber);
                    if (currentVersion.CompareTo(patchVersion) >= 0) break;

                    string innerText = patchNote.ElementInnerText();

                    innerText = innerText.Replace("\r\n", "\n");
                    innerText = innerText.Replace("\t", "");

                    sb.AppendLine("================================");
                    sb.AppendLine(patchNumber);
                    sb.AppendLine("================================\n");

                    sb.AppendLine(innerText);
                    
                }

                SetUpdateInfoBox(sb.ToString());
            }

            return true;
        }

        private bool DownloadButtonClicked(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(latestVersionFolder)) return false;

            button.Enabled = false;
            launchButton.Enabled = false;
            
            XDocument doc = null;

            try
            {
                doc = FetchXML("filelist.xml");
            }

            catch (Exception e)
            {
                SetUpdateInfoBox("Error while updating: " + e.Message);
                launchButton.Enabled = true;
                return false;
            }

            updateInfoBox.ClearChildren();

            latestVersionFiles = UpdaterUtil.GetFileList(doc);
            filesToDownload = UpdaterUtil.GetRequiredFiles(doc);

            string updaterVersion = doc.Root.GetAttributeString("updaterversion", "1.1");
            if (updaterVersion!=UpdaterUtil.Version)
            {
                ShowError("Warning", "The update may contain changes which can't be installed by the autoupdater. If you receive any error messages during the install, please download and install the update manually.");
            }

            string dir = Directory.GetCurrentDirectory();

            filesToDownloadCount = filesToDownload.Count;
            if (filesToDownloadCount > 0)
            {
                DownloadNextFile();
            }

            return true;
        }

        private List<string> filesToDownload;
        private List<string> latestVersionFiles;
        private int filesDownloaded, filesToDownloadCount;

        private void DownloadNextFile()
        {
            string dir = Directory.GetCurrentDirectory() + "\\UpdateFiles";

            if (filesDownloaded == filesToDownload.Count)
            {
                progressBar.Visible = false;
                downloadButton.Visible = false;
                //updateInfoBox.Visible = false;

                updateInfoText.Text = "Installing update...";

                try
                {
                    UpdaterUtil.InstallUpdatedFiles(dir);
                }

                catch (Exception e)
                {
                    updateInfoText.Text = "Update failed";
                    ShowError("Error while installing the update", e.Message);

                    launchButton.Enabled = true;
                    return;
                }

                settings.WasGameUpdated = true;

                UpdaterUtil.CleanUnnecessaryFiles(latestVersionFiles);

                updateInfoText.Text = "The game was updated succesfully!";
                launchButton.Enabled = true;
                
                return;
            }

            updateInfoText.Text = "Downloading file " + filesDownloaded + "/" + filesToDownloadCount;

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Point(0, 17), updateInfoBox.Content.RectTransform),
                "Downloading " + filesToDownload[filesDownloaded] + "...", font: GUI.SmallFont)
            {
                CanBeFocused = false
            };

            updateInfoBox.BarScroll = 1.0f;
            
            WebClient webClient = new WebClient();
            webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(Completed);
                       
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string fileDir = dir+"\\"+Path.GetDirectoryName(filesToDownload[filesDownloaded]);
            if (!string.IsNullOrWhiteSpace(fileDir) && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            webClient.DownloadFileAsync(new Uri(latestVersionFolder + filesToDownload[filesDownloaded]), @dir + "\\" + filesToDownload[filesDownloaded]);
        }

        private void ShowError(string header, string message)
        {
            GUIMessageBox errorBox = new GUIMessageBox(header, message, new string[] { "OK" }, 400, 250, Alignment.Center);
            errorBox.Buttons[0].OnClicked = errorBox.Close;
            /*errorBox.InnerFrame.Rect = new Rectangle(
                (graphicsWidth - errorBox.InnerFrame.Rect.Width) / 2,
                (graphicsHeight - errorBox.InnerFrame.Rect.Height) / 2,
                errorBox.InnerFrame.Rect.Width,
                errorBox.InnerFrame.Rect.Height);*/
        }

        private void Completed(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                string errorMsg = "Error while downloading: " + e.Error;

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, updateInfoBox.Content.RectTransform),
                    errorMsg, textColor: Color.Red, font: GUI.SmallFont, wrap: true);

                GUIMessageBox errorBox = new GUIMessageBox("Error while updating", "Downloading the update failed.",
                    new string[] { "Retry", "Cancel" }, 400, 200, Alignment.Center);
                /*errorBox.InnerFrame.RectTransform = new Rectangle(
                    (graphicsWidth - errorBox.InnerFrame.Rect.Width) / 2,
                    (graphicsHeight - errorBox.InnerFrame.Rect.Height) / 2,
                    errorBox.InnerFrame.Rect.Width,
                    errorBox.InnerFrame.Rect.Height);*/

                errorBox.Buttons[0].OnClicked += DownloadButtonClicked;
                errorBox.Buttons[0].OnClicked += errorBox.Close;

                errorBox.Buttons[1].OnClicked = CancelUpdate;
                errorBox.Buttons[1].OnClicked += errorBox.Close;

                return;
            }

            filesDownloaded++;
            progressBar.BarSize = ((float)filesDownloaded / (float)filesToDownloadCount);//e.ProgressPercentage;
                        
            DownloadNextFile();
        }

        private bool CancelUpdate(GUIButton button, object obj)
        {
            downloadButton.Enabled = false;
            launchButton.Enabled = true;

            return true;
        }
    }

}
