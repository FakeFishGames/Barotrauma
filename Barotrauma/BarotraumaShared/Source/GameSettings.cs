using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif
using System;

namespace Barotrauma
{
    public enum WindowMode
    {
        Windowed, Fullscreen, BorderlessWindowed
    }

    public partial class GameSettings
    {
        public int GraphicsWidth { get; set; }
        public int GraphicsHeight { get; set; }

        public bool VSyncEnabled { get; set; }

        public bool EnableSplashScreen { get; set; }

        public int ParticleLimit { get; set; }

        //public bool FullScreenEnabled { get; set; }
        
        private KeyOrMouse[] keyMapping;

        private WindowMode windowMode;

        public List<string> jobNamePreferences;

        public WindowMode WindowMode
        {
            get { return windowMode; }
            set { windowMode = value; }
        }

        public List<string> JobNamePreferences
        {
            get { return jobNamePreferences; }
            set
            {
                // Begin saving coroutine. Remove any existing save coroutines if one is running.
                if (CoroutineManager.IsCoroutineRunning("saveCoroutine")) { CoroutineManager.StopCoroutines("saveCoroutine"); }
                CoroutineManager.StartCoroutine(ApplyUnsavedChanges(), "saveCoroutine");

                jobNamePreferences = value;
            }
        }

        private bool unsavedSettings;

        public bool UnsavedSettings
        {
            get
            {
                return unsavedSettings;
            }
            private set
            {
                unsavedSettings = value;
#if CLIENT
                if (applyButton != null)
                {
                    //applyButton.Selected = unsavedSettings;
                    applyButton.Enabled = unsavedSettings;
                    applyButton.Text = unsavedSettings ? "Apply*" : "Apply";
                }
#endif
            }
        }

        private float soundVolume, musicVolume;

        public float SoundVolume
        {
            get { return soundVolume; }
            set
            {
                soundVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                if (GameMain.SoundManager!=null) GameMain.SoundManager.SetCategoryGainMultiplier("default",soundVolume);
#endif
            }
        }

        public float MusicVolume
        {
            get { return musicVolume; }
            set
            {
                musicVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                SoundPlayer.MusicVolume = musicVolume;
#endif
            }
        }

        public ContentPackage SelectedContentPackage { get; set; }

        public string   MasterServerUrl { get; set; }
        public bool     AutoCheckUpdates { get; set; }
        public bool     WasGameUpdated { get; set; }

        private string defaultPlayerName;
        public string   DefaultPlayerName
        {
            get
            {
                return defaultPlayerName ?? "";
            }
            set
            {
                if (defaultPlayerName != value)
                {
                    defaultPlayerName = value;
                    Save("config.xml");
                }
            }
        }

        public static bool VerboseLogging { get; set; }

        public GameSettings(string filePath)
        {
            ContentPackage.LoadAll(ContentPackage.Folder);

            Load(filePath);
        }

        public void Load(string filePath)
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            
            MasterServerUrl = doc.Root.GetAttributeString("masterserverurl", "");

            AutoCheckUpdates = doc.Root.GetAttributeBool("autocheckupdates", true);
            WasGameUpdated = doc.Root.GetAttributeBool("wasgameupdated", false);

            VerboseLogging = doc.Root.GetAttributeBool("verboselogging", false);

            if (doc == null)
            {
                GraphicsWidth = 1024;
                GraphicsHeight = 678;

                MasterServerUrl = "";

                SelectedContentPackage = ContentPackage.list.Any() ? ContentPackage.list[0] : new ContentPackage("");

                JobNamePreferences = new List<string>();
                foreach (JobPrefab job in JobPrefab.List)
                {
                    JobNamePreferences.Add(job.Name);
                }
                return;
            }

            XElement graphicsMode = doc.Root.Element("graphicsmode");
            GraphicsWidth = graphicsMode.GetAttributeInt("width", 0);
            GraphicsHeight = graphicsMode.GetAttributeInt("height", 0);
            VSyncEnabled = graphicsMode.GetAttributeBool("vsync", true);

            XElement graphicsSettings = doc.Root.Element("graphicssettings");
            ParticleLimit = graphicsSettings.GetAttributeInt("particlelimit", 1500);

#if CLIENT
            if (GraphicsWidth == 0 || GraphicsHeight == 0)
            {
                GraphicsWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                GraphicsHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
            }
#endif

            //FullScreenEnabled = ToolBox.GetAttributeBool(graphicsMode, "fullscreen", true);

            var windowModeStr = graphicsMode.GetAttributeString("displaymode", "Fullscreen");
            if (!Enum.TryParse<WindowMode>(windowModeStr, out windowMode))
            {
                windowMode = WindowMode.Fullscreen;
            }

            SoundVolume = doc.Root.GetAttributeFloat("soundvolume", 1.0f);
            MusicVolume = doc.Root.GetAttributeFloat("musicvolume", 0.3f);

            EnableSplashScreen = doc.Root.GetAttributeBool("enablesplashscreen", true);

            keyMapping = new KeyOrMouse[Enum.GetNames(typeof(InputType)).Length];
            keyMapping[(int)InputType.Up] = new KeyOrMouse(Keys.W);
            keyMapping[(int)InputType.Down] = new KeyOrMouse(Keys.S);
            keyMapping[(int)InputType.Left] = new KeyOrMouse(Keys.A);
            keyMapping[(int)InputType.Right] = new KeyOrMouse(Keys.D);
            keyMapping[(int)InputType.Run] = new KeyOrMouse(Keys.LeftShift);

            keyMapping[(int)InputType.Chat] = new KeyOrMouse(Keys.Tab);
            keyMapping[(int)InputType.RadioChat] = new KeyOrMouse(Keys.OemPipe);
            keyMapping[(int)InputType.CrewOrders] = new KeyOrMouse(Keys.C);

            keyMapping[(int)InputType.Select] = new KeyOrMouse(Keys.E);

            keyMapping[(int)InputType.Use] = new KeyOrMouse(0);
            keyMapping[(int)InputType.Aim] = new KeyOrMouse(1);

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "keymapping":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            InputType inputType;
                            if (Enum.TryParse(attribute.Name.ToString(), true, out inputType))
                            {
                                int mouseButton;
                                if (int.TryParse(attribute.Value.ToString(), out mouseButton))
                                {
                                    keyMapping[(int)inputType] = new KeyOrMouse(mouseButton);
                                }
                                else
                                {
                                    Keys key;
                                    if (Enum.TryParse(attribute.Value.ToString(), true, out key))
                                    {
                                        keyMapping[(int)inputType] = new KeyOrMouse(key);
                                    }
                                }
                            }
                        }
                        break;
                    case "gameplay":
                        JobNamePreferences = new List<string>();
                        foreach (XElement ele in subElement.Element("jobpreferences").Elements("job"))
                        {
                            JobNamePreferences.Add(ele.GetAttributeString("name", ""));
                        }
                        break;
                    case "player":
                        defaultPlayerName = subElement.GetAttributeString("name", "");
                        break;
                }
            }

            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                if (keyMapping[(int)inputType] == null)
                {
                    DebugConsole.ThrowError("Key binding for the input type \"" + inputType + " not set!");
                    keyMapping[(int)inputType] = new KeyOrMouse(Keys.D1);
                }
            }
            
            UnsavedSettings = false;

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "contentpackage":
                        string path = subElement.GetAttributeString("path", "");


                        SelectedContentPackage = ContentPackage.list.Find(cp => cp.Path == path);

                        if (SelectedContentPackage == null) SelectedContentPackage = new ContentPackage(path);
                        break;
                }
            }
        }
        
        public void Save(string filePath)
        {
            UnsavedSettings = false;

            XDocument doc = new XDocument();

            if (doc.Root == null)
            {
                doc.Add(new XElement("config"));
            }

            doc.Root.Add(
                new XAttribute("masterserverurl", MasterServerUrl),
                new XAttribute("autocheckupdates", AutoCheckUpdates),
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume),
                new XAttribute("verboselogging", VerboseLogging),
                new XAttribute("enablesplashscreen", EnableSplashScreen));

            if (WasGameUpdated)
            {
                doc.Root.Add(new XAttribute("wasgameupdated", true));
            }

            XElement gMode = doc.Root.Element("graphicsmode");
            if (gMode == null)
            {
                gMode = new XElement("graphicsmode");
                doc.Root.Add(gMode);
            }

            if (GraphicsWidth == 0 || GraphicsHeight == 0)
            {
                gMode.ReplaceAttributes(new XAttribute("displaymode", windowMode));
            }
            else
            {
                gMode.ReplaceAttributes(
                    new XAttribute("width", GraphicsWidth),
                    new XAttribute("height", GraphicsHeight),
                    new XAttribute("vsync", VSyncEnabled),
                    new XAttribute("displaymode", windowMode));
            }

            XElement gSettings = doc.Root.Element("graphicssettings");
            if (gSettings == null)
            {
                gSettings = new XElement("graphicssettings");
                doc.Root.Add(gSettings);
            }

            gSettings.ReplaceAttributes(new XAttribute("particlelimit", ParticleLimit));

            if (SelectedContentPackage != null)
            {
                doc.Root.Add(new XElement("contentpackage",
                    new XAttribute("path", SelectedContentPackage.Path)));
            }

            var keyMappingElement = new XElement("keymapping");
            doc.Root.Add(keyMappingElement);
            for (int i = 0; i < keyMapping.Length; i++)
            {
                if (keyMapping[i].MouseButton == null)
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].Key));
                }
                else
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].MouseButton));
                }
            }

            var gameplay = new XElement("gameplay");
            var jobPreferences = new XElement("jobpreferences");
            foreach (string jobName in JobNamePreferences)
            {
                jobPreferences.Add(new XElement("job", new XAttribute("name", jobName)));
            }
            gameplay.Add(jobPreferences);
            doc.Root.Add(gameplay);

            var playerElement = new XElement("player");
            playerElement.Add(new XAttribute("name", defaultPlayerName ?? ""));
            doc.Root.Add(playerElement);

            doc.Save(filePath);
        }
        
        private IEnumerable<object> ApplyUnsavedChanges()
        {
            yield return new WaitForSeconds(10.0f);

            Save("config.xml");
        }
    }
}
