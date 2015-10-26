using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    public class GameSettings
    {
        private GUIFrame settingsFrame;

        private float soundVolume, musicVolume;

        private Keys[] keyMapping;

        public GUIFrame SettingsFrame
        {
            get 
            {
                if (settingsFrame == null) CreateSettingsFrame();
                return settingsFrame;
            }
        }

        public int GraphicsWidth { get; set; }
        public int GraphicsHeight { get; set; }

        public bool FullScreenEnabled { get; set; }

        public ContentPackage SelectedContentPackage { get; set; }

        public string   MasterServerUrl { get; set; }
        public bool     AutoCheckUpdates { get; set; }
        public bool     WasGameUpdated { get; set; }

        public float SoundVolume
        {
            get { return soundVolume;  }
            set
            {
                soundVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
                Sounds.SoundManager.MasterVolume = soundVolume;
            }
        }

        public float MusicVolume
        {
            get { return musicVolume; }
            set
            {
                musicVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
                SoundPlayer.MusicVolume = musicVolume;
            }
        }

        public GameSettings(string filePath)
        {
            Load(filePath);
        }

        public void Load(string filePath)
        {
            XDocument doc = ToolBox.TryLoadXml(filePath);

            if (doc == null)
            {
                DebugConsole.ThrowError("No config file found");

                GraphicsWidth = 1024;
                GraphicsHeight = 678;

                MasterServerUrl = "";

                SelectedContentPackage = new ContentPackage("");

                return;
            }

            XElement graphicsMode = doc.Root.Element("graphicsmode");
            GraphicsWidth = ToolBox.GetAttributeInt(graphicsMode, "width", 0);
            GraphicsHeight = ToolBox.GetAttributeInt(graphicsMode, "height", 0);

            if (GraphicsWidth==0 || GraphicsHeight==0)
            {
                GraphicsWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                GraphicsHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
            }

            FullScreenEnabled = ToolBox.GetAttributeBool(graphicsMode, "fullscreen", true);

            MasterServerUrl = ToolBox.GetAttributeString(doc.Root, "masterserverurl", "");

            AutoCheckUpdates = ToolBox.GetAttributeBool(doc.Root, "autocheckupdates", true);
            WasGameUpdated = ToolBox.GetAttributeBool(doc.Root, "wasgameupdated", false);

            SoundVolume = ToolBox.GetAttributeFloat(doc.Root, "soundvolume", 1.0f);
            MusicVolume = ToolBox.GetAttributeFloat(doc.Root, "musicvolume", 0.3f);

            keyMapping = new Keys[Enum.GetNames(typeof(InputType)).Length];
            keyMapping[(int)InputType.Up]       = Keys.W;
            keyMapping[(int)InputType.Down]     = Keys.S;
            keyMapping[(int)InputType.Left]     = Keys.A;
            keyMapping[(int)InputType.Right]    = Keys.D;
            keyMapping[(int)InputType.Run]      = Keys.LeftShift;
            keyMapping[(int)InputType.Chat]     = Keys.Tab;
            keyMapping[(int)InputType.Select]   = Keys.E;

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "contentpackage":
                        string path = ToolBox.GetAttributeString(subElement, "path", "");
                        SelectedContentPackage = ContentPackage.list.Find(cp => cp.Path == path);

                        if (SelectedContentPackage == null) SelectedContentPackage = new ContentPackage(path);
                        break;
                    case "keymapping":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            InputType inputType;
                            Keys key;
                            if (Enum.TryParse(attribute.Name.ToString(), true, out inputType) &&
                                Enum.TryParse(attribute.Value.ToString(), true, out key))
                            {
                                keyMapping[(int)inputType] = key;
                            }
                        }
                        break;
                }
            }   
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument();            

            if (doc.Root == null)
            {
                doc.Add(new XElement("config"));
            }

            doc.Root.Add(
                new XAttribute("masterserverurl", MasterServerUrl),
                new XAttribute("autocheckupdates", AutoCheckUpdates),
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume));

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

            gMode.ReplaceAttributes(
                new XAttribute("width", GraphicsWidth),
                new XAttribute("height", GraphicsHeight),
                new XAttribute("fullscreen", FullScreenEnabled ? "true" : "false"));

            if (SelectedContentPackage != null)
            {
                doc.Root.Add(new XElement("contentpackage", 
                    new XAttribute("path", SelectedContentPackage.Path)));
            }


            doc.Save(filePath);
        }

        private bool ChangeSoundVolume(float barScroll)
        {
            SoundVolume = MathHelper.Clamp(barScroll, 0.0f, 1.0f);

            return true;
        }

        private bool ChangeMusicVolume(float barScroll)
        {
            MusicVolume = MathHelper.Clamp(barScroll, 0.0f, 1.0f);

            return true;
        }

        private void CreateSettingsFrame()
        {
            settingsFrame = new GUIFrame(new Rectangle(0, 0, 500, 500), null, Alignment.Center, GUI.Style);

            new GUITextBlock(new Rectangle(0, 0, 100, 20), "Sound volume:", GUI.Style, settingsFrame);
            GUIScrollBar soundScrollBar = new GUIScrollBar(new Rectangle(0, 20, 150, 20), GUI.Style,0.1f, settingsFrame);
            soundScrollBar.BarScroll = SoundVolume;
            soundScrollBar.OnMoved = ChangeSoundVolume;

            new GUITextBlock(new Rectangle(0, 40, 100, 20), "Music volume:", GUI.Style, settingsFrame);
            GUIScrollBar musicScrollBar = new GUIScrollBar(new Rectangle(0, 60, 150, 20), GUI.Style, 0.1f, settingsFrame);
            musicScrollBar.BarScroll = MusicVolume;
            musicScrollBar.OnMoved = ChangeMusicVolume;

            int x = 250;
            int y = 60;

            new GUITextBlock(new Rectangle(x, 40, 100, 20), "Controls:", GUI.Style, settingsFrame);
            var inputNames = Enum.GetNames(typeof(InputType));
            for (int i = 0; i< inputNames.Length; i++)
            {
                new GUITextBlock(new Rectangle(x, y, 100, 20), inputNames[i]+": ", GUI.Style, settingsFrame);
                var keyBox = new GUITextBox(new Rectangle(x + 100, y, 70, 15), GUI.Style, settingsFrame);
                keyBox.Text = keyMapping[i].ToString();
                keyBox.OnTextChanged = MapKey;

                y += 20;
            }

            var applyButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Apply", GUI.Style, settingsFrame);
            applyButton.OnClicked = ApplyClicked;
        }

        private bool MapKey(GUITextBox textBox, string text)
        {
            return true;
        }

        private bool ApplyClicked(GUIButton button, object userData)
        {
            Save("config.xml");
            return true;
        }
    }
}
