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

        public void InheritFrom(T parent);
    }

    public static class VariantExtensions
    {
        public static ContentXElement CreateVariantXML(this ContentXElement variantElement, ContentXElement baseElement)
        {
            #warning TODO: fix %ModDir% instances in the base element such that they become %ModDir:BaseMod% if necessary
            return variantElement.Element.CreateVariantXML(baseElement.Element).FromPackage(variantElement.ContentPackage);
        }
        
        public static XElement CreateVariantXML(this XElement variantElement, XElement baseElement)
        {
            XElement newElement = new XElement(variantElement.Name);
            newElement.Add(baseElement.Attributes());
            newElement.Add(baseElement.Elements());

            ReplaceElement(newElement, variantElement);

            void ReplaceElement(XElement element, XElement replacement)
            {
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
                    foreach (var subElement in element.Elements())
                    {
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
                            break;
                        }
                        i++;
                    }
                    if (!matchingElementFound)
                    {
                        element.Add(replacementSubElement);
                    }
                }
                elementsToRemove.ForEach(e => e.Remove());
            }

            void ReplaceAttribute(XElement element, XAttribute newAttribute)
            {
                XAttribute existingAttribute = element.Attributes().FirstOrDefault(a => a.Name.ToString().Equals(newAttribute.Name.ToString(), StringComparison.OrdinalIgnoreCase));
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

            return newElement;
        }
        
    }
}