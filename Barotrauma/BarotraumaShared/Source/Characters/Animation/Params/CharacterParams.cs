using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using System.Xml;
using Barotrauma.Extensions;

namespace Barotrauma
{
    /// <summary>
    /// Contains character data that should be editable in the character editor.
    /// </summary>
    class CharacterParams : EditableParams
    {
        [Serialize("", true), Editable]
        public string SpeciesName { get; private set; }

        [Serialize(false, true), Editable]
        public bool Humanoid { get; private set; }

        [Serialize(false, true), Editable]
        public bool Husk { get; private set; }

        [Serialize(false, true), Editable]
        public bool NeedsAir { get; set; }

        [Serialize(false, true), Editable]
        public bool CanSpeak { get; set; }

        [Serialize(100f, true), Editable]
        public float Noise { get; set; }

        public void Init(string file)
        {
            base.Load(file);
            // TODO: implement subparams
        }

        /* 
         * 
         * health
         * ai
         * inventory
         * sound
         * 
         * ?:
         * blooddecal
         * bloodemitter
         * gibemitter
         * 
         * 
        */
    }
}
