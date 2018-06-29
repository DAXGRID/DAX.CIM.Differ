using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Serialization;
using DAX.CIM.PhysicalNetworkModel;
using FastMember;
using Newtonsoft.Json;

namespace DAX.CIM.Differ.Extensions
{
    static class IdentifiedObjectExtensions
    {
        static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

        static readonly ConcurrentDictionary<Type, Func<IdentifiedObject, IdentifiedObject>> Cloners = new ConcurrentDictionary<Type, Func<IdentifiedObject, IdentifiedObject>>();

        public static TIdentifiedObject CloneNew<TIdentifiedObject>(this TIdentifiedObject identifiedObject)
            where TIdentifiedObject : IdentifiedObject
        {
            var type = identifiedObject.GetType();
            return (TIdentifiedObject)Cloners.GetOrAdd(type, CreateCloner)(identifiedObject);
        }

        static Func<IdentifiedObject, IdentifiedObject> CreateCloner(Type type)
        {
            var accessor = TypeAccessor.Create(type);

            var propertyNames = accessor.GetMembers()
                .Where(m => m.GetAttribute(typeof(IgnoreDataMemberAttribute), true) == null)
                .Where(m => m.CanRead && m.CanWrite)
                .Select(m => m.Name)
                .ToArray();

            return identifiedObject =>
            {
                try
                {
                    var clone = (IdentifiedObject) accessor.CreateNew();

                    foreach (var property in propertyNames)
                    {
                        try
                        {
                            accessor[clone, property] = accessor[identifiedObject, property];
                        }
                        catch (Exception exception)
                        {
                            throw new ApplicationException(
                                $"Could not transfer value of property '{property}' from {identifiedObject} to {clone}",
                                exception);
                        }
                    }

                    return clone;
                }
                catch (Exception exception)
                {
                    throw new ApplicationException($"Could not clone object {identifiedObject}", exception);
                }
            };
        }
    }
}