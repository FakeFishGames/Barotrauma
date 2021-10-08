using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
#if DEBUG
using System.Xml;
#else
using Barotrauma.IO;
#endif


namespace Barotrauma.RuinGeneration
{
    [Flags]
    enum RuinEntityType
    {
        Wall, Back, Door, Hatch, Prop
    }

    class RuinGenerationParams : OutpostGenerationParams
    {
        public static List<RuinGenerationParams> RuinParams
        {
            get
            {
                if (paramsList == null)
                {
                    LoadAll();
                }
                return paramsList;
            }
        }

        private static List<RuinGenerationParams> paramsList;

        private readonly string filePath;
                        
        public override string Name => "RuinGenerationParams";
        

        private RuinGenerationParams(XElement element, string filePath) : base(element, filePath)
        {
            this.filePath = filePath;
        }

        public static RuinGenerationParams GetRandom(Rand.RandSync randSync = Rand.RandSync.Server)
        {
            if (paramsList == null) { LoadAll(); }

            if (paramsList.Count == 0)
            {
                DebugConsole.ThrowError("No ruin configuration files found in any content package.");
                return new RuinGenerationParams(null, null);
            }

            return paramsList[Rand.Int(paramsList.Count, randSync)];
        }

        private static void LoadAll()
        {
            paramsList = new List<RuinGenerationParams>();
            foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.RuinConfig))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc?.Root == null) { continue; }

                foreach (XElement subElement in doc.Root.Elements())
                {
                    var mainElement = subElement;
                    if (subElement.IsOverride())
                    {
                        mainElement = subElement.FirstElement();
                        paramsList.Clear();
                        DebugConsole.NewMessage($"Overriding all ruin generation parameters using the file {configFile.Path}.", Color.Yellow);
                    }
                    else if (paramsList.Any())
                    {
                        DebugConsole.NewMessage($"Adding additional ruin generation parameters from file '{configFile.Path}'");
                    }
                    var newParams = new RuinGenerationParams(mainElement, configFile.Path);
                    paramsList.Add(newParams);
                }
            }
        }

        public static void ClearAll()
        {
            paramsList?.Clear();
            paramsList = null;
        }

        public static void SaveAll()
        {
            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            foreach (RuinGenerationParams generationParams in RuinParams)
            {
                foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.RuinConfig))
                {
                    if (configFile.Path != generationParams.filePath) { continue; }

                    XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                    if (doc == null) { continue; }

                    SerializableProperty.SerializeProperties(generationParams, doc.Root);

                    using (var writer = XmlWriter.Create(configFile.Path, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }
                }
            }
        }
    }
}
