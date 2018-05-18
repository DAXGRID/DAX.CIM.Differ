using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using DAX.CIM.Differ.Extensions;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using FastMember;
using Newtonsoft.Json.Linq;
// ReSharper disable ReturnTypeCanBeEnumerable.Local
// ReSharper disable InconsistentNaming

namespace DAX.CIM.Differ
{
    /// <summary>
    /// Implementation of CIM differ
    /// </summary>
    public class CimDiffer
    {
        static readonly ConcurrentDictionary<Type, string[]> PropertyNames = new ConcurrentDictionary<Type, string[]>();
        static readonly CsonSerializer Serializer = new CsonSerializer();

        public IEnumerable<IdentifiedObject> ApplyDiff(IEnumerable<IdentifiedObject> state, IEnumerable<DataSetMember> diff)
        {
            var stateDictionary = state.ToDictionary(s => s.mRID);

            foreach (var dataSetMember in diff)
            {
                var change = dataSetMember.Change;

                var targetObject = dataSetMember.TargetObject;
                var mRID = targetObject.@ref;

                switch (change)
                {
                    case ObjectCreation objectCreation:
                        var identifiedObject = objectCreation.Object;

                        stateDictionary[identifiedObject.mRID] = identifiedObject;
                        break;

                    case ObjectDeletion _:
                        stateDictionary.Remove(mRID);
                        break;

                    case ObjectModification objectModification:
                        var properties = objectModification.Properties;

                        if (!stateDictionary.TryGetValue(mRID, out var currentState))
                        {
                            throw new ArgumentException($"Could not find {targetObject.referenceType}/{mRID} to apply data set member {dataSetMember.mRID} to");
                        }

                        var newState = InnerApplyDiff(currentState, properties);

                        stateDictionary[newState.mRID] = newState;

                        break;

                    default:
                        throw new ArgumentException($"Unknown ChangeSetMember subclass: {change.GetType()}");
                }
            }

            return stateDictionary.Values;
        }

        static IdentifiedObject InnerApplyDiff(IdentifiedObject currentState, Dictionary<string, object> properties)
        {
            var targetCsonObject = JObject.Parse(Serializer.SerializeObject(currentState));

            foreach (var property in properties)
            {
                var key = property.Key;
                var value = property.Value;

                targetCsonObject[key] = JToken.FromObject(value);
            }

            return Serializer.DeserializeObject(targetCsonObject.ToString());
        }

        public IEnumerable<DataSetMember> GetDiff(IEnumerable<IdentifiedObject> previousState, IEnumerable<IdentifiedObject> newState)
        {
            var previousStateDictionary = previousState.ToDictionary(s => s.mRID);
            var newStateDictionary = newState.ToDictionary(s => s.mRID);

            var idsOfCreatedObjects = newStateDictionary.Keys.Except(previousStateDictionary.Keys).ToList();

            foreach (var createdObject in idsOfCreatedObjects.Select(id => newStateDictionary[id]))
            {
                yield return new DataSetMember
                {
                    mRID = Guid.NewGuid().ToString(),
                    TargetObject = new TargetObject
                    {
                        @ref = createdObject.mRID,
                        referenceType = createdObject.GetType().Name,
                    },
                    Change = new ObjectCreation { Object = createdObject },
                };
            }

            var idsOfDeletedObjects = previousStateDictionary.Keys.Except(newStateDictionary.Keys).ToList();

            foreach (var deletedObject in idsOfDeletedObjects.Select(id => previousStateDictionary[id]))
            {
                yield return new DataSetMember
                {
                    mRID = Guid.NewGuid().ToString(),
                    TargetObject = new TargetObject
                    {
                        @ref = deletedObject.mRID,
                        referenceType = deletedObject.GetType().Name,
                    },
                    Change = new ObjectDeletion(),
                    ReverseChange = new ObjectReverseModification
                    {
                        Properties = GetProperties(JObject.Parse(Serializer.SerializeObject(deletedObject)))
                    }
                };
            }

            var idsOfCommonObjects = previousStateDictionary.Keys.Intersect(newStateDictionary.Keys).ToList();

            foreach (var id in idsOfCommonObjects)
            {
                var previousInstance = previousStateDictionary[id];
                var newInstance = newStateDictionary[id];
                var type = newInstance.GetType();

                var previousCsonObject = JObject.Parse(Serializer.SerializeObject(previousInstance));
                var newCsonObject = JObject.Parse(Serializer.SerializeObject(newInstance));

                JObject change = null;
                JObject reverseChange = null;

                var propertyNames = GetPropertyNames(previousInstance.GetType());

                foreach (var property in propertyNames)
                {
                    // we know this one is the same
                    if (property == nameof(IdentifiedObject.mRID)) continue;

                    var previousValue = previousCsonObject[property];
                    var newValue = newCsonObject[property];

                    if (AreEqualJson(previousValue, newValue)) continue;

                    if (change == null)
                    {
                        change = new JObject();
                        reverseChange = new JObject();

                        change["$type"] = type.Name;
                        reverseChange["$type"] = type.Name;
                    }

                    change[property] = newValue != null ? JToken.FromObject(newValue) : null;
                    reverseChange[property] = previousValue != null ? JToken.FromObject(previousValue) : null;
                }

                if (change == null) continue;

                yield return new DataSetMember
                {
                    mRID = Guid.NewGuid().ToString(),
                    TargetObject = new TargetObject
                    {
                        @ref = newInstance.mRID,
                        referenceType = type.Name,
                    },
                    Change = new ObjectModification
                    {
                        Properties = GetProperties(change)
                    },
                    ReverseChange = new ObjectReverseModification
                    {
                        Properties = GetProperties(reverseChange)
                    }
                };
            }
        }

        static Dictionary<string, object> GetProperties(JObject obj) => obj.Properties()
            .ToDictionary(p => p.Name, p => p.Value.ToObject<object>());

        static bool AreEqualJson(JToken previousValue, JToken newValue)
        {
            if (ReferenceEquals(null, previousValue) && ReferenceEquals(null, newValue)) return true;

            if (ReferenceEquals(null, previousValue)) return false;

            if (ReferenceEquals(null, newValue)) return false;

            var previousStr = previousValue.ToString();
            var newStr = newValue.ToString();
            var areEqual = string.Equals(previousStr, newStr, StringComparison.Ordinal);

            return areEqual;
        }

        static string[] GetPropertyNames(Type type)
        {
            return PropertyNames.GetOrAdd(type, t =>
            {
                var members = TypeAccessor.Create(type).GetMembers();

                return members
                    .Where(m => m.GetAttribute(typeof(XmlIgnoreAttribute), true) == null)
                    .Where(m => m.GetAttribute(typeof(IgnoreDataMemberAttribute), true) == null)
                    .Select(m => m.Name)
                    .ToArray();
            });
        }
    }
}
