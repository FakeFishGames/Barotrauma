using RestSharp;
using Subsurface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Launcher
{
    public partial class LauncherMain : Form
    {
        string version = AssemblyName.GetAssemblyName("subsurface.exe").Version.ToString();

        private const string configPath = "config.xml";
        private Subsurface.GameSettings settings;

        private string latestVersionFileList, latestVersionFolder;

        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(
              string deviceName, int modeNum, ref DEVMODE devMode);
        const int ENUM_CURRENT_SETTINGS = -1;

        const int ENUM_REGISTRY_SETTINGS = -2;

        private List<GraphicsMode> supportedModes;

        public bool FullScreenEnabled
        {
            get { return settings.FullScreenEnabled; }
            set { settings.FullScreenEnabled = value; }
        }

        public bool AutoCheckUpdates
        {
            get { return settings.AutoCheckUpdates; }
            set { settings.AutoCheckUpdates = value; }
        }

        //private GraphicsMode selectedMode;
        
        public LauncherMain()
        {
            InitializeComponent();

            ContentPackage.LoadAll(ContentPackage.Folder);
            contentPackageBox.DataSource = ContentPackage.list;

            supportedModes = new List<GraphicsMode>();

            DEVMODE vDevMode = new DEVMODE();
            int i = 0;
            while (EnumDisplaySettings(null, i, ref vDevMode))
            {
                if (vDevMode.dmBitsPerPel < 16 || supportedModes.FirstOrDefault(sm => sm.Width == vDevMode.dmPelsWidth && sm.Height == vDevMode.dmPelsHeight) != null)
                {
                    i++;
                    continue;
                }
                
                supportedModes.Add(
                    new GraphicsMode(vDevMode.dmPelsWidth,
                                    vDevMode.dmPelsHeight,
                                    vDevMode.dmBitsPerPel, vDevMode.dmDisplayFrequency));
                i++;
            }

            resolutionBox.DataSource = new BindingList<GraphicsMode>(supportedModes);
            //resolutionBox.SelectedIndexChanged = 
            //LoadSettings(configPath);
            settings = new GameSettings(configPath);
            resolutionBox.SelectedItem = supportedModes.FirstOrDefault(sm => sm.Width == settings.GraphicsWidth && sm.Height == settings.GraphicsHeight);

            if (resolutionBox.SelectedItem == null)
            {
                resolutionBox.SelectedItem = supportedModes.FirstOrDefault(sm => 
                    sm.Width == Screen.PrimaryScreen.Bounds.Width && 
                    sm.Height == Screen.PrimaryScreen.Bounds.Height);

                if (resolutionBox.SelectedItem == null) resolutionBox.SelectedItem = supportedModes[0];
            }

            fullscreenBox.DataBindings.Add("Checked", this, "FullscreenEnabled");

            autoUpdateCheckBox.DataBindings.Add("Checked", this, "AutoCheckUpdates");


            if (settings.SelectedContentPackage == null)
            {
                if (contentPackageBox.Items.Count > 0) contentPackageBox.SelectedItem = contentPackageBox.Items[0];
            }
            else
            {
                contentPackageBox.SelectedItem = settings.SelectedContentPackage;
            }

            progressBar.Visible = false;
            updateLabel.Visible = false;
            downloadButton.Visible = false;

            installedVersionLabel.Text = "Installed version: " + version;

            if (settings.AutoCheckUpdates)
            {
                CheckForUpdates();                
            }

            //resolutionBox.SelectedItem = selectedMode;
        }
        
        private void SaveSettings(string filePath)
        {
            GraphicsMode selectedMode = resolutionBox.SelectedItem as GraphicsMode;
            settings.GraphicsWidth = selectedMode.Width;
            settings.GraphicsHeight = selectedMode.Height;
            settings.Save(configPath);
        }
        
        private void launchButton_Click(object sender, EventArgs e)
        {
            SaveSettings(configPath);

            var executables = settings.SelectedContentPackage.GetFilesOfType(ContentType.Executable);
            if (executables.Count == 0)
            {
                MessageBox.Show("Error", "The game executable isn't configured in the selected content package.");
                return;
            }

            string exePath = Directory.GetCurrentDirectory() + "//" + executables[0];
            if (!File.Exists(exePath))
            {
                MessageBox.Show("Error", "Couldn't find the executable ''" + exePath + "''!");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exePath));
            }
            catch (Exception exception)
            {
                MessageBox.Show("Error while opening executable ''" + exePath + "''", exception.Message);
                return;
            }

            Application.Exit();
        }

        private void packageManagerButton_Click(object sender, EventArgs e)
        {
            var packageManager = new PackageManager(settings.SelectedContentPackage);
            packageManager.Show();
        }

        private void contentPackageBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (settings == null) return;

            ComboBox comboBox = sender as ComboBox;
            
            settings.SelectedContentPackage = comboBox.SelectedItem as ContentPackage;
        }

        private bool CheckForUpdates()
        {
            patchNoteBox.Text = "Checking for updates...";

            XDocument doc = null;

            try
            {
                doc = FetchXML("versioninfo.xml");
            }

            catch (Exception e)
            {
                patchNoteBox.Text = "Error while checking for updates: " + e.Message;
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

            if (response.ResponseStatus!= ResponseStatus.Completed) return null;            
            if (response.StatusCode != HttpStatusCode.OK) return null;

            string xml = response.Content;

            string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            if (xml.StartsWith(_byteOrderMarkUtf8))
            {
                xml = xml.Remove(0, _byteOrderMarkUtf8.Length);
            }

            return XDocument.Parse(xml);
        }

        private bool CheckUpdateXML(XDocument doc)
        {
            if (doc.Root==null)
            {
                patchNoteBox.Text = "Error while checking for updates: could not parse update info";
                return false;
            }

            progressBar.Visible = true;
            downloadButton.Visible = true;
            updateLabel.Visible = true;

            string latestVersion = ToolBox.GetAttributeString(doc.Root, "latestversion", "");
            latestVersionFolder = ToolBox.GetAttributeString(doc.Root, "latestversionfolder", "");
            latestVersionFileList = ToolBox.GetAttributeString(doc.Root, "latestversionfilelist", "");

            if (latestVersion == version)
            {
                patchNoteBox.Text = "Game is up to date!";
                return false;
            }

            updateLabel.Text = "New update found! (" + latestVersion + ")";

            XElement patchNotes = doc.Root.Element("patchnotes");

            if (patchNotes!=null)
            {
                StringBuilder sb = new StringBuilder();

                foreach (XElement patchNote in patchNotes.Elements())
                {
                    string patchNumber = ToolBox.GetAttributeString(patchNote, "version", "");

                    //read the patch notes until we reach the user's version
                    if (patchNumber == version) break;

                    sb.AppendLine(ToolBox.ElementInnerText(patchNote));
                    sb.AppendLine("*************************************\n");
                }

                patchNoteBox.Text = sb.ToString();
            }

            return true;
        }

        private void downloadButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(latestVersionFolder)) return;

            Button senderButton = sender as Button;
            senderButton.Enabled = false;

            XDocument doc = null;

            try
            {
                doc = FetchXML("filelist.xml");
            }

            catch (Exception exception)
            {
                patchNoteBox.Text = "Error while checking for updates: " + exception.Message;
                return;
            }

            filesToDownload = UpdaterUtil.GetRequiredFiles(doc);

            string dir = Directory.GetCurrentDirectory();

            filesToDownloadCount = filesToDownload.Count;
            if (filesToDownloadCount>0)
            {
                WebClient webClient = new WebClient();
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(Completed);
                //webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(ProgressChanged);

                webClient.DownloadFileAsync(new Uri(latestVersionFolder + filesToDownload[0]), dir);
            }            
        }

        private List<string> filesToDownload;

        private int filesDownloaded, filesToDownloadCount;
        
        private void Completed(object sender, AsyncCompletedEventArgs e)
        {
            filesDownloaded++; 
            progressBar.Value = (int)(((float)filesDownloaded / (float)filesToDownloadCount) * 100.0f);//e.ProgressPercentage;

            filesToDownload.RemoveAt(0);

            if (filesToDownload.Count==0)
            {
                progressBar.Visible = false;
                downloadButton.Visible = false;
                updateLabel.Visible = false;

                MessageBox.Show("Download completed!");
                settings.WasGameUpdated = true;
                return;
            }

            updateLabel.Text = "Downloading file "+ filesDownloaded + "/" + filesToDownloadCount + " ("+ filesToDownload[0] + ")";

            WebClient webClient = new WebClient();
            webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(Completed);
            //webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(ProgressChanged);

            string dir = Directory.GetCurrentDirectory();

            string fileDir = Path.GetDirectoryName(filesToDownload[0]);
            if (!string.IsNullOrWhiteSpace(fileDir) && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            webClient.DownloadFileAsync(new Uri(latestVersionFolder + filesToDownload[0]), @dir + "\\" + filesToDownload[0]);
           
        }

        private void LauncherMain_Load(object sender, EventArgs e)
        {

        }

    }

    public class GraphicsMode
    {
        public readonly int Width, Height;
        public readonly int Bits;
        public readonly int Frequency;

        public GraphicsMode(int width, int height, int bits, int freq)
        {
            Width = width;
            Height = height;
            Bits = bits;
            Frequency = freq;
        }

        public override string ToString()
        {
            return Width + "x" + Height;// +", " + Bits + " bit, " + Frequency + " Hz";
        }
    }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVMODE
        {

            private const int CCHDEVICENAME = 0x20;
            private const int CCHFORMNAME = 0x20;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public ScreenOrientation dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    
}

