using Subsurface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Launcher
{
    public partial class LauncherMain : Form
    {
        private const string configPath = "config.xml";
        private Subsurface.GameSettings settings;

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

            if (settings.SelectedContentPackage == null)
            {
                if (contentPackageBox.Items.Count > 0) contentPackageBox.SelectedItem = contentPackageBox.Items[0];
            }
            else
            {
                contentPackageBox.SelectedItem = settings.SelectedContentPackage;
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

            Process.Start(new ProcessStartInfo(Directory.GetCurrentDirectory() + "//"+settings.SelectedContentPackage.GetFilesOfType(ContentType.Executable)[0]));
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

