using System.Text.RegularExpressions;
using System.Xml.Linq;
using Barotrauma;

namespace HomeAndHarbor.Resources
{
    /// <summary>
    /// Resource that pulls its value from a variable using reflection.
    /// </summary>
    internal sealed class LinkedResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : Resource(prefab, manager, element)
    {
        private Option<ResourceManager.Member> sourceMember = Option.None;

        private string source = string.Empty;

        [Serialize("", IsPropertySaveable.No, description: "Source of the resource. This is a string in the format 'Data::Path.To.Variable'.")]
        public string Source
        {
            get
            {
                return source;
            }
            private set
            {
                source = value;
                sourceMember = ParseSource(value);
            }
        }

        /// <summary>
        /// Example value of Source:
        /// Location::PersistentOutpostData.Population
        /// </summary>
        private readonly Regex sourceRegex = RegularExpressions.LinkedResourceRegex();

        protected override float GetAmountResourceSpecific()
        {
            return sourceMember.TryUnwrap(out var member) ? member.GetValue<float>() : 0;
        }

        protected override void SetAmount(float value)
        {
            if (sourceMember.TryUnwrap(out var member))
            {
                member.SetValue(value);
            }
        }

        private Option<ResourceManager.Member> ParseSource(string sourceString)
        {
            if (string.IsNullOrEmpty(sourceString)) { return Option.None; }

            Match match = sourceRegex.Match(sourceString);
            if (!match.Success)
            {
                return Option.None;
            }

            string type = match.Groups[RegularExpressions.TypeGroup].Value;
            string path = match.Groups[RegularExpressions.PathGroup].Value;

            return Manager.GetPath(type, path);
        }
    }
}