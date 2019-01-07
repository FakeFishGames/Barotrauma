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
    partial class HumanoidAnimParams : ISerializableEntity
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
