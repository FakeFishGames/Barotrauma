using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Immutable;
using Barotrauma.Extensions;
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
        public readonly static PrefabCollection<RuinGenerationParams> RuinParams =
            new PrefabCollection<RuinGenerationParams>();

        public override string Name => "RuinGenerationParams";

        public RuinGenerationParams(ContentXElement element, RuinConfigFile file) : base(element, file) { }

        public static void SaveAll()
        {
            #warning TODO: revise
            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            IEnumerable<ContentPackage> packages = ContentPackageManager.LocalPackages;
#if DEBUG
            packages = packages.Union(ContentPackageManager.VanillaCorePackage.ToEnumerable());
#endif
            foreach (RuinGenerationParams generationParams in RuinParams)
            {
                foreach (RuinConfigFile configFile in packages.SelectMany(p => p.GetFiles<RuinConfigFile>()))
                {
                    if (configFile.Path != generationParams.ContentFile.Path) { continue; }

                    XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                    if (doc == null) { continue; }

                    SerializableProperty.SerializeProperties(generationParams, doc.Root);

                    using (var writer = XmlWriter.Create(configFile.Path.Value, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }
                }
            }
        }

        public override void Dispose() { }
    }
}
