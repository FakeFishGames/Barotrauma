using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    class HumanoidAnimParams : ISerializableEntity
    {
        public string Name
        {
            get;
            private set;
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        public static HumanoidAnimParams WalkInstance = new HumanoidAnimParams("Content/Characters/HumanoidAnimWalk.xml");
        public static HumanoidAnimParams RunInstance = new HumanoidAnimParams("Content/Characters/HumanoidAnimRun.xml");

        private string filePath;

        public HumanoidAnimParams(string file)
        {
            this.filePath = file;

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            Name = doc.Root.Name.ToString();

            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
        }
        
#if CLIENT
        private static GUIListBox editor;

        public static void UpdateEditor(float deltaTime)
        {
            if (editor == null)
            {
                editor = new GUIListBox(new Rectangle(0, 0, 300, GameMain.GraphicsHeight), "", null);
                editor.Padding = Vector4.One * 5.0f;
                new SerializableEntityEditor(WalkInstance, false, editor, true);
                new SerializableEntityEditor(RunInstance, false, editor, true);
            }
            editor.Update(deltaTime);
            editor.AddToGUIUpdateList();
        }

        public static void DrawEditor(SpriteBatch spriteBatch)
        {
            editor?.Draw(spriteBatch);

            if (PlayerInput.KeyDown(Keys.LeftAlt) && PlayerInput.KeyHit(Keys.S))
            {
                RunInstance.Save();
                WalkInstance.Save();
            }
        }

        private void Save()
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return;
            
            SerializableProperty.SerializeProperties(this, doc.Root, true);
 
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.OmitXmlDeclaration = true;
            settings.NewLineOnAttributes = true;

            using (var writer = XmlWriter.Create(filePath, settings))
            {
                doc.WriteTo(writer);
                writer.Flush();
            }
        }
#endif
        
        [Serialize(0.3f, true), Editable]
        public float GetUpSpeed
        {
            get;
            set;
        }

        [Serialize(1.54f, true), Editable]
        public float HeadPosition
        {
            get;
            set;
        }

        [Serialize(1.15f, true), Editable]
        public float TorsoPosition
        {
            get;
            set;
        }


        [Serialize(0.25f, true), Editable]
        public float HeadLeanAmount
        {
            get;
            set;
        }

        [Serialize(0.25f, true), Editable]
        public float TorsoLeanAmount
        {
            get;
            set;
        }

        [Serialize(5.0f, true), Editable]
        public float CycleSpeed
        {
            get;
            set;
        }

        [Serialize(15.0f, true), Editable]
        public float FootMoveStrength
        {
            get;
            set;
        }
        [Serialize(20.0f, true), Editable]
        public float FootRotateStrength
        {
            get;
            set;
        }


        [Serialize("0.4,0.12", true), Editable]
        public Vector2 StepSize
        {
            get;
            set;
        }

        [Serialize("0.0, 0.0", true), Editable]
        public Vector2 FootMoveOffset
        {
            get;
            set;
        }

        [Serialize(10.0f, true), Editable]
        public float LegCorrectionTorque
        {
            get;
            set;
        }

        [Serialize(15.0f, true), Editable]
        public float ThighCorrectionTorque
        {
            get;
            set;
        }

        [Serialize("0.4, 0.15", true), Editable]
        public Vector2 HandMoveAmount
        {
            get;
            set;
        }

        [Serialize("-0.15, 0.0", true), Editable]
        public Vector2 HandMoveOffset
        {
            get;
            set;
        }

        [Serialize(0.7f, true), Editable]
        public float HandMoveStrength
        {
            get;
            set;
        }

        [Serialize(-1.0f, true), Editable]
        public float HandClampY
        {
            get;
            set;
        }
        
    }
}
