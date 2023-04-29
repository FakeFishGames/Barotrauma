#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text.RegularExpressions;
using System.Collections;

using Barotrauma.Extensions;
using System.Runtime.ConstrainedExecution;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    public class PrefabInstance
    {
        public Identifier id;
        public string package;
        public PrefabInstance(Identifier identifier, string Package){ id = identifier; package = Package; }

        public bool IsEmpty { get { return id.IsEmpty; } }
    }

    public static class PrefabInstanceExtension
    {
        public static Identifier ToIdentifier(this PrefabInstance inst)
        {
            return inst.id;
        }
    }

    // flags prefab types that can inherit but not necessarily have variants.
    // this means the prefab can inherit+override same identifier or the prefab is singleton.
    // typically: afflictions and cprsettings, etc.
    public interface IImplementsInherit { }

    public interface IImplementsPartialOverride : IImplementsInherit { }

	public interface IImplementsAnyInherit : IImplementsInherit { }

	public interface IImplementsActivator { }

    // evaluate T only when sortall happens (force resolve inheritance)
    // inherits from T so that current prefabselector code works with Activators.
    // not thread-safe, as with prefabs
    public class PrefabActivator<T> : Prefab, IImplementsActivator where T : Prefab
    {
        static PrefabActivator() {
            if (!typeof(T).GetInterfaces().Any(i => i.Name.Contains(nameof(IImplementsInherit))))
            {
                throw new InvalidOperationException("PrefabActivator<T> can only take IImplementsInherit types!");
            }
			if (typeof(T).GetInterfaces().Any(i => i.Name.Contains(nameof(IImplementsVariants<T>))))
			{
				throw new InvalidOperationException("PrefabActivator<T> currently does not handle variant logic!");
			}
		}
        public PrefabActivator(ContentFile file, ContentXElement element, Func<ContentXElement, T> constructorLambda, Func<PrefabActivator<T>, PrefabActivator<T>?> locator,
		    VariantExtensions.VariantXMLChecker? create_callback = null, Action<T>? onAdd = null) 
            : base(file, element)
        {
			originalElement = element;
            CurrentElement = element;
            constructor = constructorLambda;
            GetParentFunc = locator;
			OnAdd = onAdd;
            inherit_callback = create_callback;
            // we don't need to wait for sortall to resolve.
            if (originalElement.InheritParent().id.IsEmpty) {
				cached = constructor.Invoke(CurrentElement);
                cached_valid = true;
                is_immutable = true;
				bool prev_potental_call = potentialCallFromConstructor;
				potentialCallFromConstructor = false;
				OnAdd?.Invoke(cached);
				potentialCallFromConstructor = prev_potental_call;
			}
		}

        public T? Activate() {
			if (cached != null && cached_valid)
			{
				return cached;
			}
			else
			{
				DoInherit(inherit_callback);
				cached = constructor.Invoke(CurrentElement);
				cached_valid = true;
                OnAdd?.Invoke(cached);
				return cached;
			}
		}

        public void InvalidateCache(Action<T>? OnRemoved=null) {
            T? current = cached;
            if (!is_immutable) {
				cached?.Dispose();
				cached = null;
				cached_valid = false;
			}
            if (current != null) {
				OnRemoved?.Invoke(current);
			}
        }

		public T? Current { get => cached; }

		public PrefabInstance InheritParent => originalElement.InheritParent();

		public IEnumerable<PrefabActivator<T>> InheritHistory {
            get {
                var cur = this;
				while (cur != null)
                {
                    cur = GetParentFunc.Invoke(cur);
					if (cur is null) break;
					yield return cur;
				}
			}
        }

		public void CheckInheritHistory(PrefabActivator<T> parent)
		{
			if (parent.InheritHistory.Any(p => ReferenceEquals(p, this as T)))
			{
				throw new Exception("Inheritance cycle detected: "
					+ string.Join(", ", InheritHistory.Select(n => "(id: " + n.Identifier.ToString() + ", package: " + n.ContentPackage!.Name + ")"))
					+ "\n(id: " + (this as T)!.Identifier.ToString() + ", package: " + (this as T)!.ContentPackage?.Name + ")");
			}
		}

		public void DoInherit(VariantExtensions.VariantXMLChecker? create_callback)
		{
			Stack<ContentXElement> preprocessed = new Stack<ContentXElement>();
			var last_elem = originalElement;
			foreach (var it in InheritHistory)
			{
                CheckInheritHistory(it);
				preprocessed.Push(last_elem.PreprocessInherit(it.originalElement, false));
				last_elem = preprocessed.Peek();
			}
			ContentXElement previous = preprocessed.Pop();
			while (preprocessed.Any())
			{
				previous = preprocessed.Pop().CreateVariantXML(previous, create_callback);
			}
			CurrentElement = originalElement.CreateVariantXML(previous, create_callback);
		}

        private T? cached = null;
        private bool cached_valid = false;

		private bool is_immutable { get; }

        Func<PrefabActivator<T>, PrefabActivator<T>?> GetParentFunc;

		public static PrefabActivator<T>? GetParent_Collection(PrefabActivator<T> current, PrefabCollection<T> collection)
        {
            PrefabInstance parent_instance = current.InheritParent;
			if (parent_instance.id.IsEmpty) {
                return null;
            }
            else if (parent_instance.package.IsNullOrEmpty())
            {
                string current_package = current.ContentPackage?.GetBestEffortId()??"0";
                return collection.GetSelector(parent_instance.id)?.GetPreviousActivator(current_package);
			}
            else
            {
                collection.TryGet(parent_instance, out PrefabActivator<T>? res);
                return res;
            }
		}

		public static PrefabActivator<T>? GetParent_Selector(PrefabActivator<T> current, PrefabSelector<T> selector)
		{
			PrefabInstance parent_instance = current.InheritParent;
            if (parent_instance.id.IsEmpty)
            {
                return null;
            }
            else if (parent_instance.id != current.Identifier) {
                throw new InvalidOperationException("Cannot use GetParent_Selector inheriting different identifiers");
            }
            else if (parent_instance.package.IsNullOrEmpty())
            {
                string current_package = current.ContentPackage?.GetBestEffortId() ?? "0";
                return selector.GetPreviousActivator(current_package);
            }
            else
            {
                return selector.GetPackageActivator(parent_instance.package);
            }
		}

		public ContentXElement originalElement { get; }

		public ContentXElement CurrentElement { get; set; }

		private Func<ContentXElement, T> constructor;

        VariantExtensions.VariantXMLChecker? inherit_callback;
		public override void Dispose(){
			InvalidateCache();
		}

        private Action<T>? OnAdd;
	}

	public interface IImplementsVariants<T> : IImplementsInherit where T : Prefab
    {
        // direct parent of the prefab
        public PrefabInstance InheritParent => originalElement.InheritParent();

        // ancestry line of the prefab
        public IEnumerable<T> InheritHistory { 
            get {
                IImplementsVariants<T>? cur = this;
                while(!(cur?.InheritParent?.IsEmpty??true)){
                    if(cur.InheritParent.package.IsNullOrEmpty()){
                        cur = cur.GetPrevious(cur.InheritParent.id) as IImplementsVariants<T>;
                    }
                    else{
                        cur = FindByPrefabInstance(cur.InheritParent) as IImplementsVariants<T>;
                    }
                    T? res = (cur as T);
					if (res is null) break;
                    yield return res;
                }
            } 
        }

        public XElement originalElement{ get; }

        public ContentXElement ConfigElement { get; }

        public void InheritFrom(T parent);

        public bool CheckInheritHistory(T parent)
        {
            bool result = true;
            if ((parent as IImplementsVariants<T>)!.InheritHistory.Any(p => ReferenceEquals(p, this as T)))
            {
                throw new Exception("Inheritance cycle detected: "
                    + string.Join(", ", InheritHistory.Select(n => "(id: " + n.Identifier.ToString() + ", package: " + n.ContentPackage!.Name + ")"))
                    + "\n(id: " + (this as T)!.Identifier.ToString() + ", package: " + (this as T)!.ContentPackage?.Name + ")");
            }
            return result;
        }

        public T FindByPrefabInstance(PrefabInstance instance);

        public T GetPrevious(Identifier id);

        public ContentXElement DoInherit(VariantExtensions.VariantXMLChecker create_callback)
        {
            Stack<ContentXElement> preprocessed = new Stack<ContentXElement>();
            var last_elem = originalElement.FromContent((this as T)!.FilePath);
            foreach(var it in InheritHistory)
            {
                preprocessed.Push(last_elem.PreprocessInherit((it as IImplementsVariants<T>)!.originalElement.FromContent(it.ContentFile.Path), false));
                last_elem = preprocessed.Peek();
            }
            ContentXElement previous = preprocessed.Pop();
            while (preprocessed.Any())
            {
                previous = preprocessed.Pop().CreateVariantXML(previous, create_callback);
            }
            return originalElement.FromContent((this as T)!.ContentFile.Path).CreateVariantXML(previous, create_callback);
        }
    }

    public static class VariantExtensions
    {
        /* preprocess the parent element according to xpath patch element specified.
         the patch element is defined as <inherit/> element
         allowed attributes: identifier, package
         child elements are applied from top to bottom.
         allowed child elements:
            add:        adds the containing element/attribute to the items selected by xpath
            replace:    replaces the elements/attributes selected by xpath with content
            del:        removes the elements/attributes selected by xpath
         meta child element:
            doinherit:  separates preprocess and postprocess.

            allowed attributes for edit elements:
                sel:    the xpath selector
            allowed contents:
                attribute:  literal value
                element:    literal xml element

          Note on path: %ModDir% refers to the mod that is applying the patch. As specified in
                        xpathElement.baseUri. Aka, preprocessing happens after path replace.
                        But this also should happen before parent element inherits anything.
            */
        public static void PreprocessXPath(this ContentXElement newElement, ContentXElement variantElement, ContentXElement baseElement, bool is_post_process) {
            // characters use speciesname instead of identifier attribute
            var inherits = variantElement.Elements()
                .Where(p => p.Name.ToString().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                    && p.GetAttributeIdentifier("identifier", "") == 
                        (baseElement.Name.ToString().Equals("Character",StringComparison.OrdinalIgnoreCase)? 
                            baseElement.GetAttributeIdentifier("speciesname", "") : baseElement.GetAttributeIdentifier("identifier", "")))
                .SelectMany(p => p.Elements().SkipWhile(p => is_post_process ? !p.Name.ToString().Equals("doinherit") : false).Skip(is_post_process?1:0));
            // adapted from barotrauma mod generator
            foreach (ContentXElement change_ctx in inherits)
            {
                XElement change = change_ctx.Element;
				string xpath = change.Attribute("sel")?.Value??".";
                bool done = false;
                switch (change.Name.ToString().ToLower())
                {
                    case "doinherit":
                        done = true;
                        break;
                    case "add":
                        MatchCollection matches = Regex.Matches(xpath, @"^(?<element>.+)/@(?<attributeName>[a-zA-Z]+)$");
                        if (!matches.Any())
                        {
                            foreach (XElement elt in newElement.Element.XPathSelectElements(xpath)) { 
                                elt.Add(change.Elements());
                            }
                        }
                        else
                        {
                            string newXpath = matches[0].Groups["element"].Value.Trim();
                            string attributeName = matches[0].Groups["attributeName"].Value.Trim();
                            foreach (XElement elt in newElement.Element.XPathSelectElements(newXpath))
                                elt.SetAttributeValue(attributeName, change.Value.Trim());
                        }
                        break;
                    case "del":
                        {
                            // removing while iterating may break loop.
                            var all = ((IEnumerable)newElement.Element.XPathEvaluate(xpath)).Cast<XObject>().ToArray();
                            foreach (XObject xObject in all)
                            {
                                switch (xObject)
                                {
                                    case XElement element:
                                        element.Remove();
                                        break;
                                    case XAttribute attribute:
                                        attribute.Remove();
                                        break;
                                }
                            }
                        }
                        break;
                    case "replace":
                        {
                            var all = ((IEnumerable)newElement.Element.XPathEvaluate(xpath)).Cast<XObject>().ToArray();
                            foreach (XObject xObject in all)
                            {
                                switch (xObject)
                                {
                                    case XAttribute attribute:
                                        attribute.Value = change.Value.Trim();
                                        break;
                                    case XElement element:
                                        if (change.HasElements)
                                        {
                                            XElement? target = element.Parent;
                                            if (target != null) {
                                                element.Remove();
                                                target.Add(change.Elements());
                                            }
                                        }
                                        else
                                        {
                                            element.Value = change.Value.Trim();
                                        }
                                        break;
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
                if (done)
                {
                    break;
                }
            }
        }

        public delegate void VariantXMLChecker(XElement originalElement, XElement variantElement, XElement result);


        public static ContentXElement PreprocessInherit(this ContentXElement variantElement, ContentXElement baseElement, bool is_post_process) {
            ContentXElement newElement;
            if(is_post_process){
                newElement = CopyUpdateFilePath(variantElement, baseElement);
            }
            else{
                newElement = new XElement(baseElement.Element).FromContent(baseElement.ContentPath);
            }
            newElement.PreprocessXPath(variantElement, baseElement, is_post_process);
            return newElement;
        }

        // changes file="", texture="", path="", folder="" attributes that use local path and %ModDir%, etc into %ModDir:OldMod%
        // vineatlas="", decayatlas="" for gardening plants, branchatlas="" for ballast flora
        // also note that File.Exists(path) may not exist for ragdoll folders, thus "Content/" as local path will require more work.
        // also note that folders' GetDirectoryName() return parent directory or itself (depending on if trailing sep is trimed).
        public static ContentXElement CopyUpdateFilePath(this ContentXElement variantElement,  ContentXElement baseElement)
        {
            ContentXElement newElement = new XElement(variantElement.Name).FromContent(variantElement.ContentPath);
            ReplacePath(baseElement, newElement);

            void ReplacePath(ContentXElement old_element, ContentXElement new_element)
            {
                foreach (XAttribute attribute in old_element.Attributes())
                {
                    /*  "//@*[matches(name(),'texture','i')]"
                        "//@*[matches(name(),'file','i')]"
                        "//@*[matches(name(),'path','i')]"
                            "//*[matches(name(),'submarine','i')]/@*[matches(name(),'path','i')]"
                            "//*[matches(name(),'huskappendage','i')]/@*[matches(name(),'path','i')]"
                            "//*[matches(name(),'names','i')]/@*[matches(name(),'path','i')]"
                        "//@*[matches(name(),'folder','i')]"
                            "//*[matches(name(),'ragdolls','i')]/@*[matches(name(),'folder','i') && !matches(@*,'default','i')]"
                            "//*[matches(name(),'animations','i')]/@*[matches(name(),'folder','i') && !matches(@*,'default','i')]"

                            "//*[matches(name(),'ragdolls','i')]/@*[matches(name(),'folder','i') && matches(@*,'default','i')]"
                            "//*[matches(name(),'animations','i')]/@*[matches(name(),'folder','i') && matches(@*,'default','i')]"
                        "//@*[matches(name(),'vineatlas','i')]"
                            "//*[matches(name(),'vinesprites','i')]/@*[matches(name(),'vineatlas','i')]"
                        "//@*[matches(name(),'decayatlas','i')]"
                            "//*[matches(name(),'vinesprites','i')]/@*[matches(name(),'decayatlas','i')]"
                        "//@*[matches(name(),'branchatlas','i')]"
                            "//*[matches(name(),'BallastFloraBehavior','i')]/@*[matches(name(),'decayatlas','i')]"
                            "//*[matches(name(),'BallastFloraBehavior','i')]/@*[matches(name(),'branchatlas','i')]"

                        string to string conversion for use with default.

                        path_attr_xpath_list.Select(p=>newElement.Evaluate(xpath).ForEach(attr=>p.cb(attr, old_element_path)))
                    */
                    
                    //Dictionary<string, Action<XAttribute, ContentPath, ContentPath>> replacements = new Dictionary<string, Action<XAttribute, ContentPath, ContentPath>>();

                    //replacements.ForEach(p => ((IEnumerable)newElement.Element.XPathEvaluate(p.Key)).Cast<XAttribute>().ToArray().ForEach(attr => p.Value(attr,variantElement.ContentPath, baseElement.ContentPath)));

                    if ((attribute.Name.ToString().Equals("texture", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.ToString().Equals("file", StringComparison.OrdinalIgnoreCase)
                        // it seems one sound prefab code path have an unused "path" atrribute read...
                        || attribute.Name.ToString().Equals("path", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.ToString().Equals("folder", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.ToString().Equals("vineatlas", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.ToString().Equals("decayatlas", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.ToString().Equals("branchatlas", StringComparison.OrdinalIgnoreCase)))
                    {
                        ContentPath evaluated;
                        if (attribute.Name.ToString().Equals("folder", StringComparison.OrdinalIgnoreCase) && attribute.Value.ToString().Equals("default", StringComparison.OrdinalIgnoreCase))
                        {
                            if(!old_element.GetAttributeBool("usehuskappendage",false)) {
                                if (attribute.Parent?.Name.ToString().Equals("ragdolls", StringComparison.OrdinalIgnoreCase)??false)
                                {
                                    evaluated = ContentPath.FromRaw(old_element.ContentPath, "./Ragdolls/");
                                }
                                else if (attribute.Parent?.Name.ToString().Equals("animations", StringComparison.OrdinalIgnoreCase)??false)
                                {
                                    evaluated = ContentPath.FromRaw(old_element.ContentPath, "./Animations/");
                                }
                                else
                                {
                                    evaluated = attribute.GetContentPath(old_element.ContentPath);
                                }
                            }
                            else
                            {
                                evaluated = attribute.GetContentPath(old_element.ContentPath);
                            }
                        }
                        else
                        {
                            evaluated = attribute.GetContentPath(old_element.ContentPath);
                        }
                        
                        if((evaluated.isVanilla || ReferenceEquals(evaluated.ContentPackage,new_element.ContentPath.ContentPackage))
                            // vanilla may be relative path, then need to mutate
                            && evaluated.RelativePath==attribute.Value.ToString().CleanUpPathCrossPlatform())
                        {
                            new_element.Add(attribute);
                        }
                        else {
                            new_element.Add(new XAttribute(attribute.Name, evaluated.MutateContentPath(new_element.ContentPath).ToAttrString()));
                        }
                    }
                    else
                    {
                        new_element.Add(attribute);
                    }
                }
                // for use with <replace sel="./@tags">xxxxx</replace>
                // the xxxxx is "value" of the element
                new_element.Element.Value = old_element.Element.Value;
                foreach (ContentXElement old_child in old_element.Elements())
                {
                    ContentXElement new_child = new XElement(old_child.Name).FromContent(new_element.ContentPath);
                    ReplacePath(old_child, new_child);
                    new_element.Add(new_child);
                }
            }
            return newElement;
        }

        public static ContentXElement CreateVariantXML(this ContentXElement variantElement, ContentXElement baseElement, VariantXMLChecker? create_callback)
        {
            // As of 0.18.15.1, grep -r "Content/" yields only texture="xxx" and file="yyy" attributes.
            // This means for config feature set vanilla is using, replacing can be safely done via these two attributes.

            // cannot copy baseuri here
            ContentXElement newElement = variantElement.PreprocessInherit(baseElement, true);

            ReplaceElement(newElement.Element, variantElement.Element);

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
                        if (replacementSubElement.Name.ToString().Equals("clear", StringComparison.OrdinalIgnoreCase))
                        {
                            matchingElementFound = true;
                            elementsToRemove.AddRange(element.Elements());
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
                            break;
                        }
                        i++;
                    }
                    // ignore <inherit/> element when creating variants
                    if (!matchingElementFound && !replacementSubElement.Name.ToString().Equals("inherit", StringComparison.OrdinalIgnoreCase))
                    {
                        element.Add(replacementSubElement);
                    }
                }
                elementsToRemove.ForEach(e => e.Remove());
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

            create_callback?.Invoke(newElement.Element,variantElement.Element,baseElement.Element);
            return newElement;
        }
    }
}
