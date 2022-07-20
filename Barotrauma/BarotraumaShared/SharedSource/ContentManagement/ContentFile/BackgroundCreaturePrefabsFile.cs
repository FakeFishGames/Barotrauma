namespace Barotrauma
{
    sealed class BackgroundCreaturePrefabsFile : OtherFile
    {
        public BackgroundCreaturePrefabsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        //this content type only comes into play when a level is generated, so LoadFile and UnloadFile don't have anything to do
    }
}