namespace Hyper.ComponentModel
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Security;
    using System.Security.Permissions;

    public sealed class HyperTypeDescriptionProvider : TypeDescriptionProvider
    {
        private static readonly Dictionary<Type, ICustomTypeDescriptor> descriptors = new Dictionary<Type, ICustomTypeDescriptor>();
        private static readonly Dictionary<Type, TypeDescriptionProvider> providers = new Dictionary<Type, TypeDescriptionProvider>();

        public static void Add(Type type)
        {
            lock (descriptors)
            {
                if (!providers.ContainsKey(type))
                {
                    // determine if the base type was already added
                    // if so, remove it before adding sub-type
                    // (if a sub-type is added after its base type, infinite recursion occurs in GetTypeDescriptor())
                    var baseFound = false;
                    if (type.BaseType != null && providers.ContainsKey(type.BaseType))
                    {
                        baseFound = true;
                        Remove(type.BaseType);
                    }

                    // add the provider for the type
                    var provider = new HyperTypeDescriptionProvider(TypeDescriptor.GetProvider(type));

                    TypeDescriptor.AddProvider(provider, type);
                    providers.Add(type, provider);

                    // initialize descriptor
                    provider.GetTypeDescriptor(type);

                    // if base type was removed, we can now add it back after building the sub-type descriptor
                    if (baseFound)
                        Add(type.BaseType);
                }
            }
        }

        public static void Remove(Type type)
        {
            lock (descriptors)
            {
                TypeDescriptor.RemoveProvider(providers[type], type);
                providers.Remove(type);
                descriptors.Remove(type);
            }
        }

        public static void Clear()
        {
            lock (descriptors)
            {
                foreach (var provider in providers)
                    TypeDescriptor.RemoveProvider(provider.Value, provider.Key);
                providers.Clear();
                descriptors.Clear();
            }
        }


        private HyperTypeDescriptionProvider()
            : this(typeof(object))
        { }

        private HyperTypeDescriptionProvider(Type type)
            : this(TypeDescriptor.GetProvider(type))
        { }

        private HyperTypeDescriptionProvider(TypeDescriptionProvider parent)
            : base(parent)
        { }

        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            lock (descriptors)
            {
                ICustomTypeDescriptor descriptor;
                if (!descriptors.TryGetValue(objectType, out descriptor))
                {
                    try
                    {
                        descriptor = BuildDescriptor(objectType);
                    }
                    catch
                    {
                        return base.GetTypeDescriptor(objectType, instance);
                    }
                }
                return descriptor;
            }
        }

        [SecuritySafeCritical]
        private ICustomTypeDescriptor BuildDescriptor(Type objectType)
        {
            // NOTE: "descriptors" already locked here

            // get the parent descriptor and add to the dictionary so that
            // building the new descriptor will use the base rather than recursing
            var descriptor = base.GetTypeDescriptor(objectType, null);
            descriptors.Add(objectType, descriptor);
            try
            {
                // build a new descriptor from this, and replace the lookup
                descriptor = new HyperTypeDescriptor(descriptor);
                descriptors[objectType] = descriptor;
                return descriptor;
            }
            catch
            {   // rollback and throw
                // (perhaps because the specific caller lacked permissions;
                // another caller may be successful)
                descriptors.Remove(objectType);
                throw;
            }
        }
    }
}