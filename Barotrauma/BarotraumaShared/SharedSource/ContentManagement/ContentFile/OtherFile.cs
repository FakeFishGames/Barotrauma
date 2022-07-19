using Barotrauma;

namespace Barotrauma
{

    [AlternativeContentTypeNames("None")]
    public class OtherFile : HashlessFile
    {
        public OtherFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        //this content type is completely ignored by the game so LoadFile and UnloadFile don't do anything
        public sealed override void LoadFile() { }
        public sealed override void UnloadFile() { }
        public sealed override void Sort() { }
    }
}
