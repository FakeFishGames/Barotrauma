using System;
using System.Security.Cryptography;

namespace Barotrauma
{
    public abstract class BaseSubFile : ContentFile
    {
        protected BaseSubFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path)
        {
            using var md5 = MD5.Create();
            #warning TODO: this doesn't account for collisions, this should probably be using the PrefabCollection class like everything else
            UintIdentifier = ToolBox.StringToUInt32Hash(Barotrauma.IO.Path.GetFileNameWithoutExtension(path.Value), md5);
        }

        public readonly UInt32 UintIdentifier;

        public override void LoadFile()
        {
            SubmarineInfo.RefreshSavedSub(Path.Value);
        }

        public override void UnloadFile()
        {
            SubmarineInfo.RemoveSavedSub(Path.Value);
        }

        public override void Sort()
        {
            //Overrides for subs don't exist! Should we change this?
        }
    }

    [NotSyncedInMultiplayer]
    public class SubmarineFile : BaseSubFile
    {
        public SubmarineFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }
    }
}
