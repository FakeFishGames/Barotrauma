#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public interface IImplementsVariants<T> where T : Prefab
    {
        public Identifier VariantOf { get; }

        public T? ParentPrefab { get; set; }

        public void InheritFrom(T parent);
    }

    public static class VariantExtensions
    {
        public delegate void VariantXMLChecker(XElement originalElement, XElement? variantElement, XElement result);

        public static ContentXElement CreateVariantXML(this ContentXElement variantElement, ContentXElement baseElement, VariantXMLChecker? checker = null)
        {
            XElement newElement = new XElement(baseElement);

            //if the base element is from a different content package, we must make sure the %ModDir% elements inherited from it refer to that content package
            //otherwise there can be situations in which mod B defines a variant of some item without overriding the sprite,
            //and then when you enable mod A which overrides that item and replaces it's sprite, mod B would attempt to find the sprite for the variant item from it's own folder (even though it's in mod A's folder).            
            if (baseElement!.ContentPackage != null &&
                baseElement!.ContentPackage != variantElement.ContentPackage)
            {
                foreach (var subElement in newElement.Descendants())
                {
                    foreach (var attribute in subElement.Attributes())
                    {
                        if (attribute.Value.Contains(ContentPath.ModDirStr))
                        {
                            //make mod dir point to the original content package
                            attribute.SetValue(
                                attribute.Value.Replace(ContentPath.ModDirStr, string.Format(ContentPath.OtherModDirFmt, baseElement!.ContentPackage.Name), StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }

            ReplaceElement(newElement, variantElement);

            void ReplaceElement(XElement element, XElement replacement)
            {
                XElement originalElement = new XElement(element);

                List<XElement> newElementsFromBase = new List<XElement>(element.Elements());
                List<XElement> elementsToRemove = new List<XElement>();
                foreach (XAttribute attribute in replacement.Attributes())
                {
                    ReplaceAttribute(element, attribute);
                }
                foreach (XElement replacementSubElement in replacement.Elements())
                {
                    int index = replacement.Elements().ToList().FindAll(e => e.Name.ToString().Equals(replacementSubElement.Name.ToString(), StringComparison.OrdinalIgnoreCase)).IndexOf(replacementSubElement);
                    System.Diagnostics.Debug.Assert(index > -1);

                    int i = 0;
                    bool matchingElementFound = false;
                    bool cleared = false;
                    foreach (var subElement in element.Elements())
                    {
                        if (replacementSubElement.Name.ToString().Equals("clear", StringComparison.OrdinalIgnoreCase))
                        {
                            matchingElementFound = true;
                            newElementsFromBase.Clear();
                            elementsToRemove.AddRange(element.Elements());
                            //add all the other elements defined after <Clear>
                            foreach (var elementAfterClear in replacementSubElement.ElementsAfterSelf())
                            {
                                element.Add(elementAfterClear);
                            }
                            cleared = true;
                            break;
                        }
                        if (!subElement.Name.ToString().Equals(replacementSubElement.Name.ToString(), StringComparison.OrdinalIgnoreCase)) { continue; }
                        if (i == index)
                        {
                            if (!replacementSubElement.HasAttributes && !replacementSubElement.HasElements)
                            {
                                //if the replacement is empty (no attributes or child elements)
                                //remove the element from the variant
                                elementsToRemove.Add(subElement);
                            }
                            else
                            {
                                ReplaceElement(subElement, replacementSubElement);
                            }
                            matchingElementFound = true;
                            newElementsFromBase.Remove(subElement);
                            break;
                        }
                        i++;
                    }
                    if (!matchingElementFound)
                    {
                        element.Add(replacementSubElement);
                    }
                    //this element cleared all the subelements from the base xml and potentially added new elements after the <Clear>,
                    //no need to handle any other subelements here
                    if (cleared) { break; }
                }
                elementsToRemove.ForEach(e => e.Remove());
                checker?.Invoke(originalElement, replacement, element);
                foreach (XElement newElement in newElementsFromBase)
                {
                    checker?.Invoke(newElement, null, newElement);
                }
            }

            void ReplaceAttribute(XElement element, XAttribute newAttribute)
            {
                XAttribute? existingAttribute = element.Attributes().FirstOrDefault(a => a.Name.ToString().Equals(newAttribute.Name.ToString(), StringComparison.OrdinalIgnoreCase));
                if (existingAttribute == null)
                {
                    element.Add(newAttribute);
                    return;
                }
                float.TryParse(existingAttribute.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float value);
                if (newAttribute.Value.StartsWith('*'))
                {
                    string multiplierStr = newAttribute.Value.Substring(1, newAttribute.Value.Length - 1);
                    float.TryParse(multiplierStr, NumberStyles.Any, CultureInfo.InvariantCulture, out float multiplier);
                    if (multiplierStr.Contains('.') || existingAttribute.Value.Contains('.'))
                    {
                        existingAttribute.Value = (value * multiplier).ToString("G", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        existingAttribute.Value = ((int)(value * multiplier)).ToString();
                    }
                }
                else if (newAttribute.Value.StartsWith('+'))
                {
                    string additionStr = newAttribute.Value.Substring(1, newAttribute.Value.Length - 1);
                    float.TryParse(additionStr, NumberStyles.Any, CultureInfo.InvariantCulture, out float addition);
                    if (additionStr.Contains('.') || existingAttribute.Value.Contains('.'))
                    {
                        existingAttribute.Value = (value + addition).ToString("G", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        existingAttribute.Value = ((int)(value + addition)).ToString();
                    }
                }
                else
                {
                    existingAttribute.Value = newAttribute.Value;
                }
            }

            return newElement.FromPackage(variantElement.ContentPackage);
        }
        
    }
}