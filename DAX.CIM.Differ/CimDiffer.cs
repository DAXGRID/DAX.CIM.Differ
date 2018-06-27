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
                        var modifications = objectModification.Modifications;


                        var properties = objectModification.Properties;

                        if (!stateDictionary.TryGetValue(mRID, out var currentState))
                        {
                            throw new ArgumentException($"Could not find {targetObject.referenceType}/{mRID} to apply data set member {dataSetMember.mRID} to");
                        }

                        var newState = InnerApplyDiffNew(currentState, modifications);

                        stateDictionary[newState.mRID] = newState;

                        break;

                    default:
                        throw new ArgumentException($"Unknown ChangeSetMember subclass: {change.GetType()}");
                }
            }

            return stateDictionary.Values;
        }

        static IdentifiedObject InnerApplyDiffNew(IdentifiedObject currentState, PropertyModification[] modifications)
        {
            var type = currentState.GetType();
            var accessor = TypeAccessor.Create(type);
            var newState = accessor.CreateNew();

            // set all non-modified properties
            var propertyNames = GetPropertyNames(type).Except(modifications.Select(m => m.Name));

            foreach (var property in propertyNames)
            {
                accessor[newState, property] = accessor[currentState, property];
            }

            // apply modified properties
            foreach (var modification in modifications)
            {
                var property = modification.Name
                               ?? throw new ArgumentException($@"Tried to get value from modification without a name: value: {modification.Value}, unit: {modification.Unit}, mult: {modification.Multiplier}");

                var getter = ValueGetters.GetOrAdd(type, _ => new ConcurrentDictionary<string, Func<PropertyModification, object>>())
                    .GetOrAdd(property, _ => CreateValueGetter(type, accessor, property));

                accessor[newState, property] = getter(modification);
            }

            return (IdentifiedObject)newState;
        }

        static Func<PropertyModification, object> CreateValueGetter(Type type, TypeAccessor accessor, string property)
        {
            var memberType = GetMemberType(type, accessor, property);
            var valueAccessor = TypeAccessor.Create(memberType);

            if (IsValueType(accessor))
            {
                return modification =>
                {
                    var newValue = valueAccessor.CreateNew();

                    valueAccessor[newValue, "Value"] = double.Parse(modification.Value);
                    valueAccessor[newValue, "unit"] = modification.Unit;
                    valueAccessor[newValue, "multiplier"] = modification.Multiplier;

                    return newValue;
                };
            }

            return modification => Convert.ChangeType(modification.Value, memberType);
        }

        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<PropertyModification, object>>> ValueGetters = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<PropertyModification, object>>>();

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
                var accessor = TypeAccessor.Create(type);

                var propertyNames = GetPropertyNames(previousInstance.GetType());

                List<PropertyModification> modifications = null;
                List<PropertyModification> reverseModifications = null;

                foreach (var property in propertyNames)
                {
                    // we know this one is the same
                    if (property == nameof(IdentifiedObject.mRID)) continue;

                    var memberType = GetMemberType(type, accessor, property);
                    var previousValue = accessor[previousInstance, property];
                    var newValue = accessor[newInstance, property];

                    var (modification, reverseModifiction) = GetModificationsOrNull(memberType, property, previousValue, newValue);

                    if (modification == null) continue;

                    if (modifications == null)
                    {
                        modifications = new List<PropertyModification>();
                        reverseModifications = new List<PropertyModification>();
                    }

                    modifications.Add(modification);
                    reverseModifications.Add(reverseModifiction);
                }

                if (modifications == null) continue;

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
                        Modifications = modifications.ToArray()
                    },
                    ReverseChange = new ObjectReverseModification
                    {
                        Modifications = reverseModifications.ToArray()
                    }
                };

                //var previousCsonObject = JObject.Parse(Serializer.SerializeObject(previousInstance));
                //var newCsonObject = JObject.Parse(Serializer.SerializeObject(newInstance));

                //JObject change = null;
                //JObject reverseChange = null;

                //var propertyNames = GetPropertyNames(previousInstance.GetType());

                //foreach (var property in propertyNames)
                //{
                //    // we know this one is the same
                //    if (property == nameof(IdentifiedObject.mRID)) continue;

                //    var previousValue = previousCsonObject[property];
                //    var newValue = newCsonObject[property];

                //    if (AreEqualJson(previousValue, newValue)) continue;

                //    if (change == null)
                //    {
                //        change = new JObject();
                //        reverseChange = new JObject();

                //        change["$type"] = type.Name;
                //        reverseChange["$type"] = type.Name;
                //    }

                //    change[property] = newValue != null ? JToken.FromObject(newValue) : null;
                //    reverseChange[property] = previousValue != null ? JToken.FromObject(previousValue) : null;
                //}

                //if (change == null) continue;

                //var modifications = new List<PropertyModification>();
                //var reverseModifications = new List<PropertyModification>();

                //yield return new DataSetMember
                //{
                //    mRID = Guid.NewGuid().ToString(),
                //    TargetObject = new TargetObject
                //    {
                //        @ref = newInstance.mRID,
                //        referenceType = type.Name,
                //    },
                //    Change = new ObjectModification
                //    {
                //        Properties = GetProperties(change),
                //        Modifications = modifications.ToArray()
                //    },
                //    ReverseChange = new ObjectReverseModification
                //    {
                //        Properties = GetProperties(reverseChange),
                //        Modifications = reverseModifications.ToArray()
                //    }
                //};
            }
        }

        static Type GetMemberType(Type objectType, TypeAccessor accessor, string property)
        {
            return MemberTypes.GetOrAdd(objectType, _ => new ConcurrentDictionary<string, Type>())
                .GetOrAdd(property, _ =>
                {
                    var member = GetMemberOrThrow(objectType, accessor, property);

                    return member.Type;
                });
        }

        static Member GetMemberOrThrow(Type objectType, TypeAccessor accessor, string property)
        {
            return accessor.GetMembers().FirstOrDefault(m => m.Name == property)
                   ?? throw new ArgumentException($@"Weird! Could not find member named '{property}' among these:

{string.Join(Environment.NewLine, accessor.GetMembers().Select(m => $"    {m.Name}"))}

from type {objectType}");
        }

        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Type>> MemberTypes = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Type>>();

        static (PropertyModification, PropertyModification) GetModificationsOrNull(Type type, string property, object previousValue, object newValue)
        {
            var getter = Getters
                .GetOrAdd(type, _ => new ConcurrentDictionary<string, Func<object, object, (PropertyModification, PropertyModification)>>())
                .GetOrAdd(property, _ => { return CreateGetterFunction(type, property, previousValue, newValue); });

            return getter(previousValue, newValue);
        }

        static Func<object, object, (PropertyModification, PropertyModification)> CreateGetterFunction(Type type, string property, object previousValue, object newValue)
        {
            var accessor = TypeAccessor.Create(type);

            if (IsValueType(accessor))
            {
                return (prev, nev) =>
                {
                    var previousValueField = accessor.GetValueOrNull(previousValue, "Value");
                    var newValueField = accessor.GetValueOrNull(newValue, "Value");

                    var previousUnitField = accessor.GetValueOrNull(previousValue, "unit");
                    var newUnitField = accessor.GetValueOrNull(previousValue, "unit");

                    var previousMultiplierField = accessor.GetValueOrNull(previousValue, "multiplier");
                    var newMultiplierField = accessor.GetValueOrNull(previousValue, "multiplier");

                    if (AreEqual(previousValueField, newValueField)
                        && AreEqual(previousUnitField, newUnitField)
                        && AreEqual(previousMultiplierField, newMultiplierField)) return EmptyPropMod;

                    return (
                        new PropertyModification
                        {
                            Name = property,
                            IsReference = false,
                            Multiplier = (UnitMultiplier)newMultiplierField,
                            Unit = (UnitSymbol)newUnitField,
                            Value = FormatValue(newValueField)
                        },
                        new PropertyModification
                        {
                            Name = property,
                            IsReference = false,
                            Multiplier = (UnitMultiplier)previousMultiplierField,
                            Unit = (UnitSymbol)previousUnitField,
                            Value = FormatValue(previousValueField)
                        }
                    );
                };
            }

            return (prev, nev) =>
            {
                if (AreEqual(prev, nev)) return EmptyPropMod;

                return (
                    new PropertyModification { Name = property, IsReference = false, Value = prev?.ToString() },
                    new PropertyModification { Name = property, IsReference = false, Value = nev?.ToString() }
                );
            };
        }

        static bool IsValueType(TypeAccessor accessor)
        {
            var members = accessor.GetMembers().ToList();
            var isValueType = members.Count == 3
                              && members.Any(m => m.Name == "Value")
                              && members.Any(m => m.Name == "unit")
                              && members.Any(m => m.Name == "multiplier");
            return isValueType;
        }

        static string FormatValue(object value)
        {
            return value?.ToString();
        }

        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, object, (PropertyModification, PropertyModification)>>> Getters
            = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, object, (PropertyModification, PropertyModification)>>>();

        static readonly (PropertyModification, PropertyModification) EmptyPropMod = (null, null);

        static Dictionary<string, object> GetProperties(JObject obj) => obj.Properties()
            .ToDictionary(p => p.Name, p => p.Value.ToObject<object>());

        static bool AreEqual(object previousValue, object newValue)
        {
            if (ReferenceEquals(null, previousValue) && ReferenceEquals(null, newValue)) return true;
            if (ReferenceEquals(null, previousValue)) return false;
            if (ReferenceEquals(null, newValue)) return false;

            return previousValue.Equals(newValue);
        }

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
