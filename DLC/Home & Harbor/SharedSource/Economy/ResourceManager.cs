using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Barotrauma;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor
{
    internal sealed class ResourceManager
    {
        public readonly Dictionary<Identifier, Resource> ResourcesByIdentifier = [];
        public IEnumerable<Resource> Resources => ResourcesByIdentifier.Values;

        public readonly Location Location;
        public readonly PersistentOutpostData OutpostData;

        public Identifier FactionIdentifier
            => Location.Faction?.Prefab.Identifier ?? Identifier.Empty;

        public ResourceManager(Location location, PersistentOutpostData outpostData, XElement? element = null)
        {
            Location = location;
            OutpostData = outpostData;

            if (element != null)
            {
                Load(element);
            }

            foreach (ResourcePrefab prefab in ResourcePrefab.Prefabs)
            {
                if (!ResourcesByIdentifier.ContainsKey(prefab.Identifier))
                {
                    if (!prefab.AppliesToLocation(location)) { continue; }
                    if (Resource.Load(prefab, this) is { } resource)
                    {
                        ResourcesByIdentifier.Add(resource.Identifier, resource);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Failed to load resource from prefab: {prefab}");
                    }
                }
            }
            //instantiating a new ResourceManager rather than loading from a save -> set resource values to the defaults
            if (element == null)
            {
                Random rand = GenerateRandom(location);
                foreach (Resource resource in Resources.OrderBy(static r => r.Identifier))
                {
                    resource.SetDefault(rand);
                }
            }
        }

        private static Random GenerateRandom(Location location)
        {
            int seed = ToolBox.StringToInt(location.NameIdentifier + $"{location.MapPosition.X},{location.MapPosition.Y}");
            Random rand = new MTRandom(seed);
            return rand;
        }

        private void Load(XElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            foreach (XElement subElement in element.Elements())
            {
                if (Resource.Load(this, subElement) is { } resource)
                {
                    ResourcesByIdentifier.Add(resource.Identifier, resource);
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to load resource from element: {subElement}");
                }
            }
        }

        public void Save(XElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            XElement saveElement = new XElement(nameof(ResourceManager));
            foreach (Resource resource in Resources)
            {
                saveElement.Add(resource.Save());
            }

            element.Add(saveElement);
        }

        public readonly record struct Member(MemberInfo Info, Type Type, object Instance)
        {
            public T GetValue<T>()
            {
                object? value = Info switch
                {
                    FieldInfo field       => field.GetValue(Instance),
                    PropertyInfo property => property.GetValue(Instance),
                    _                     => throw new InvalidOperationException()
                };

                if (value is T t)
                {
                    return t;
                }
                else if (typeof(T).IsValueType && value is IConvertible convertible)
                {
                    return (T)convertible.ToType(typeof(T), null);
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to cast value to {typeof(T)}: {value}");
                    return default!;
                }
            }

            public void SetValue(object value)
            {
                Action<object?, object?> setMethod;
                Type fieldType;

                switch (Info)
                {
                    case FieldInfo field:
                        fieldType = field.FieldType;
                        setMethod = field.SetValue;
                        break;
                    case PropertyInfo property:
                        if (property.SetMethod == null)
                        {
                            throw new InvalidOperationException($"Cannot set the value of the resource. Property {property.Name} has no setter.");
                        }
                        fieldType = property.PropertyType;
                        setMethod = property.SetValue;
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                object valueWithType = value;
                if (valueWithType.GetType() != fieldType)
                {
                    if (valueWithType is IConvertible convertible)
                    {
                        valueWithType = convertible.ToType(fieldType, null);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Cannot convert value to {fieldType}: {valueWithType}");
                    }
                }

                setMethod(Instance, valueWithType);
            }
        }

        public Option<Member> GetPath(string typeName, string path)
        {
            ArgumentNullException.ThrowIfNull(typeName);
            ArgumentNullException.ThrowIfNull(path);

            Type thisType = GetType();
            MemberInfo? member = GetMember(thisType, typeName);
            if (member == null) { return Option.None; }

            string[] pathParts = path.Split('.');

            Member nextMember = new(member, GetMemberType(member), GetValue(member, this)!);
            (Type Type, object Instance) currentMember = (thisType, this);

            StringBuilder currentPath = new StringBuilder($"{typeName}::");
            foreach (string pathPart in pathParts)
            {
                currentPath.Append($".{pathPart}");
                MemberInfo? memberInfo = GetMember(nextMember.Type, pathPart);

                if (memberInfo == null) { return Option.None; }

                object? nextInstance = GetValue(memberInfo, nextMember.Instance);

                if (nextInstance == null)
                {
                    DebugConsole.ThrowError($"Value is null: {currentPath}");
                    return Option.None;
                }

                Type nextType = GetMemberType(memberInfo);

                currentMember = (nextMember.Type, nextMember.Instance);
                nextMember = new Member(memberInfo, nextType, nextInstance);
            }

            return Option.Some(nextMember with { Instance = currentMember.Instance, Type = currentMember.Type });

            static object? GetValue(MemberInfo member, object instance)
            {
                return member switch
                {
                    FieldInfo field       => field.GetValue(instance),
                    PropertyInfo property => property.GetValue(instance),
                    _                     => throw new InvalidOperationException()
                };
            }

            static Type GetMemberType(MemberInfo member)
            {
                return member switch
                {
                    FieldInfo field       => field.FieldType,
                    PropertyInfo property => property.PropertyType,
                    _                     => throw new InvalidOperationException()
                };
            }

            static MemberInfo? GetMember(Type type, string name)
            {
                MemberInfo[] members = type.GetMember(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                switch (members.Length)
                {
                    case > 1:
                        DebugConsole.ThrowError($"Ambiguous field name: {name}");
                        return null;
                    case 0:
                        DebugConsole.ThrowError($"Field not found: {name}");
                        return null;
                }

                return members[0];
            }
        }

        public bool HasResource(Identifier identifier, PropertyConditional.ComparisonOperatorType op, Either<float, Identifier> amountOrIdentifierToCompareTo,
            ResourceManager? resourceManagerToCompareTo = null)
        {
            resourceManagerToCompareTo ??= this;
            if (ResourcesByIdentifier.TryGetValue(identifier, out Resource? r))
            {
                if (amountOrIdentifierToCompareTo.TryGet(out float amount))
                {
                    return PropertyConditional.Compare(r.Amount, amount, op);
                }
                else if (amountOrIdentifierToCompareTo.TryGet(out Identifier resourceId))
                {
                    if (resourceManagerToCompareTo.ResourcesByIdentifier.TryGetValue(resourceId, out Resource? resourceToCompareTo))
                    {
                        return PropertyConditional.Compare(r.Amount, resourceToCompareTo.Amount, op);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Failed to compare resource {identifier} to {resourceId} (resource or variable {resourceId} not found)");
                        return false;

                    }
                }
            }
            DebugConsole.ThrowError($"Resource not found: {identifier}, location \"{Location.DisplayName}\"");
            return false;
        }

        public bool HasActiveResourceStatusIndicator(Identifier identifier, string debugLogName = "")
        {
            bool indicatorExists = false;
            foreach (var resource in Resources)
            {
                foreach (var resourceStatusIndicator in resource.ResourceStatusIndicators)
                {
                    if (resourceStatusIndicator.Identifier == identifier)
                    {
                        indicatorExists = true;
                        if (resourceStatusIndicator.IsActive) { return true; }
                    }
                }
            }
            if (!indicatorExists)
            {
                DebugConsole.ThrowError($"Error in {nameof(HasActiveResourceStatusIndicator)}: resource modifier {identifier} doesn't exist! {debugLogName}");
            }
            return false;
        }

        public IEnumerable<ResourceStatusIndicator> GetActiveResourceStatusIndicators()
        {
            foreach (var resource in Resources)
            {
                resource.UpdateResourceStatusIndicators();
                foreach (var resourceResourceStatusIndicator in resource.ResourceStatusIndicators)
                {
                    if (resourceResourceStatusIndicator.IsActive)
                    {
                        yield return resourceResourceStatusIndicator;
                    }
                }
            }
        }

        public void SetResource(Identifier identifier, float amount, bool createNetworkEvent = false, bool createUINotification = true)
        {
            if (ResourcesByIdentifier.TryGetValue(identifier, out Resource? r))
            {
                if (r.IsReadonly)
                {
                    DebugConsole.AddWarning($"Attempted to modify a read-only resource \"{r.Prefab.Identifier}\" in location \"{Location.DisplayName}\".");
                    return;
                }
                float prevAmount = r.Amount;
                r.Amount = amount;

                r.UpdateResourceStatusIndicators();
#if SERVER
                if (createNetworkEvent)
                {
                    OutpostData.ServerWriteResources(createUINotification: createUINotification);
                }
#endif
#if CLIENT
                if (createUINotification)
                {
                    CreateResourceChangeNotification(r, prevAmount);
                }
#endif
                return;
            }
            //update all resource status indicators, they might be affected by the resource we just modified
            foreach (var resource in Resources)
            {
                resource.UpdateResourceStatusIndicators();
            }
            DebugConsole.ThrowError($"Resource not found: {identifier}, location \"{Location.DisplayName}\"");
        }

        /// <summary>
        /// Add or remove a specified amount to a resource.
        /// </summary>
        /// <param name="identifier">Resource to add to</param>
        /// <param name="amountToAdd">Amount to add to the resource. Can be negative to remove resources.</param>
        /// <param name="remainingAmountToAdd">If all of the desired amount to add/remove could not be applied, due to the resource hitting the minimum or maximum values,
        /// this will contain the remaining amount. Positive values mean this much couldn't be added due to the resource hitting the maximum value, 
        /// negative values mean that much couldn't be removed due to the resource hitting the minimum value.</param>
        public void AddToResource(Identifier identifier, float amountToAdd, out float remainingAmountToAdd)
        {
            remainingAmountToAdd = amountToAdd;
            if (ResourcesByIdentifier.TryGetValue(identifier, out Resource? r))
            {
                if (r.IsReadonly)
                {
                    DebugConsole.AddWarning($"Attempted to modify a read-only resource \"{r.Prefab.Identifier}\" in location \"{Location.DisplayName}\".");
                    return;
                }

                float newValue = r.Amount + amountToAdd;
                r.Amount = newValue;

                if (!MathUtils.NearlyEqual(newValue, r.Amount))
                {
                    remainingAmountToAdd = newValue - r.Amount;
                }
                else
                {
                    remainingAmountToAdd = 0f;
                }

                return;
            }
            DebugConsole.ThrowError($"Resource not found: {identifier}, location \"{Location.DisplayName}\"");
        }

        internal bool LocationHasResource(Identifier identifier) => ResourcesByIdentifier.ContainsKey(identifier);

        /// <summary>
        /// Returns the outpost of this location if it's the outpost currently loaded, otherwise null.
        /// </summary>
        internal Submarine? GetLoadedOutpost()
        {
            if (Level.Loaded is { Type: LevelData.LevelType.Outpost, StartOutpost: Submarine outpost } level && level.StartLocation == Location)
            {
                return outpost;
            }
            return null;
        }

        /// <summary>
        /// If this location's outpost is currently loaded, refreshes ItemResources based on the items currently in the outpost (normally done when leaving the outpost). 
        /// Used when we need to check and modify ItemResources mid-round (e.g. when sending out a shipment of items):
        /// without this, the player could lets say take all the weapons to their sub, and still sell them because the ItemResource hadn't been updated 
        /// to reflect the amount of weapons on the outpost.
        /// </summary>
        public void SyncResourcesWithLoadedOutpostItems()
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            if (GetLoadedOutpost() is not Submarine outpost) { return; }
            foreach (ItemResource itemResource in Resources.OfType<ItemResource>())
            {
                itemResource.SyncWithLoadedOutpostItems(outpost);
            }
        }

        /// <summary>
        /// If this location's outpost is currently loaded, spawns/removes items in the outpost to match ItemResources (normally done when entering the outpost).
        /// Used when we need to check and modify ItemResources mid-round (e.g. when sending out a shipment of items):
        /// without this, the player could lets say order all the reports to be sold to nearby outposts, 
        /// and then take those weapons and bring them to their sub instead (essentially duplicating them).
        /// </summary>
        public void SyncLoadedOutpostItemsWithResources()
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            if (GetLoadedOutpost() is not Submarine outpost) { return; }
            List<ItemResource> itemResources = Resources.OfType<ItemResource>().OrderBy(r => r.Prefab.SpawnOrder).ToList();
            foreach (ItemResource itemResource in itemResources)
            {
                itemResource.OnOutpostEntered(outpost, OutpostData);
            }
            foreach (ItemResource itemResource in itemResources)
            {
                itemResource.FixEmptiedContainers(outpost);
            }
        }

        public Resource? GetResource(Identifier identifier)
        {
            if (ResourcesByIdentifier.TryGetValue(identifier, out Resource? r))
            {
                return r;
            }

            DebugConsole.ThrowError($"Resource not found: {identifier}, location \"{Location.DisplayName}\"");
            return null;
        }

#if CLIENT
        public void CreateResourceChangeNotification(Resource resource, float prevAmount)
        {
            if (!OutpostData.IsPlayerOwned) { return; }
            if (!MathUtils.NearlyEqual(prevAmount, resource.Amount) &&
                Location == GameMain.GameSession?.Campaign?.CurrentLocation &&
                Screen.Selected is GameScreen && !resource.Hidden &&
                Character.Controlled != null)
            {
                int change = (int)resource.Amount - (int)prevAmount;
                if (change != 0 && Character.Controlled != null)
                {
                    string sign = change > 0 ? "+" : "-";
                    Character.Controlled.AddMessage(
                        $"{sign}[value] {resource.DisplayName.Value}",
                        change > 0 ? GUIStyle.Green : GUIStyle.Red,
                        playSound: true, identifier: resource.Prefab.Identifier, Math.Abs(change), lifetime: 5.0f);
                }
            }
        }
#endif

        public bool TryGetResource(Identifier identifier, [NotNullWhen(true)] out Resource? resource) => ResourcesByIdentifier.TryGetValue(identifier, out resource);

    }
}