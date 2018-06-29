using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using FastMember;
using Newtonsoft.Json;
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
                               ?? throw new ArgumentException($@"Tried to get value from modification without a name: {FormatModification(modification)}");

                var getter = ValueGetters.GetOrAdd(type, _ => new ConcurrentDictionary<string, Func<PropertyModification, object>>())
                    .GetOrAdd(property, _ => CreateValueGetter(type, accessor, property));

                try
                {
                    accessor[newState, property] = getter(modification);
                }
                catch (Exception exception)
                {
                    throw new ApplicationException($"Could not set the value of the '{property}' property on {type} from modification {FormatModification(modification)}", exception);
                }
            }

            return (IdentifiedObject)newState;
        }

        static string FormatModification(PropertyModification modification) => new
        {
            modification.Name,
            Value = JsonConvert.SerializeObject(modification.Value),
        }.ToString();

        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<PropertyModification, object>>> ValueGetters = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<PropertyModification, object>>>();

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
                .GetOrAdd(property, _ => CreateModificationGetter(type, property));

            try
            {
                return getter(previousValue, newValue);
            }
            catch (Exception exception)
            {
                throw new ArgumentException($"Could not generate property modifications for the '{property}' property of {type}, value changed from {previousValue} to {newValue}", exception);
            }
        }

        static Func<object, object, (PropertyModification, PropertyModification)> CreateModificationGetter(Type type, string property)
        {
            var accessor = TypeAccessor.Create(type);

            return (prev, nev) =>
            {
                //

                //var previousValue = accessor[prev, property];
                //var newValue = accessor[nev, property];

                if (AreEqual(prev, nev)) return EmptyPropMod;

                return (
                    new PropertyModification {Name = property, Value = nev},
                    new PropertyModification {Name = property, Value = prev} 
                );
            };

            //if (type == typeof(Point2D))
            //{
            //    return (prev, nev) =>
            //    {
            //        var previousX = (double)accessor.GetValueOrNull(previousValue, nameof(Point2D.X));
            //        var newX = (double)accessor.GetValueOrNull(newValue, nameof(Point2D.X));
            //        var previousY = (double)accessor.GetValueOrNull(previousValue, nameof(Point2D.Y));
            //        var newY = (double)accessor.GetValueOrNull(newValue, nameof(Point2D.Y));

            //        if (AreEqual(previousX, newX) && AreEqual(previousY, newY)) return EmptyPropMod;

            //        return (
            //            new PropertyModification
            //            {
            //                Name = property,
            //                IsReference = false,
            //                Value = EncodeCoords2(newX, newY)
            //            },
            //            new PropertyModification
            //            {
            //                Name = property,
            //                IsReference = false,
            //                Value = EncodeCoords2(previousX, previousY)
            //            }
            //        );
            //    };
            //}

            //if (IsReferenceType(accessor))
            //{
            //    return (prev, nev) =>
            //    {
            //        var previousType = (string)accessor.GetValueOrNull(previousValue, nameof(PowerSystemResourceAssets.referenceType));
            //        var newType = (string)accessor.GetValueOrNull(newValue, nameof(PowerSystemResourceAssets.referenceType));
            //        var previousRef = (string)accessor.GetValueOrNull(previousValue, nameof(PowerSystemResourceAssets.@ref));
            //        var newRef = (string)accessor.GetValueOrNull(newValue, nameof(PowerSystemResourceAssets.@ref));

            //        if (AreEqual(previousType, newType) && AreEqual(previousRef, newRef)) return EmptyPropMod;

            //        return (
            //            new PropertyModification
            //            {
            //                Name = property,
            //                IsReference = true,
            //                Value = EncodeRef(newType, newRef)
            //            },
            //            new PropertyModification
            //            {
            //                Name = property,
            //                IsReference = true,
            //                Value = EncodeRef(previousType, previousRef)
            //            }
            //        );
            //    };
            //}

            //if (IsValueType(accessor))
            //{
            //    return (prev, nev) =>
            //    {
            //        var previousValueField = accessor.GetValueOrNull(previousValue, nameof(KiloActivePower.Value));
            //        var newValueField = accessor.GetValueOrNull(newValue, nameof(KiloActivePower.Value));

            //        var previousUnitField = accessor.GetValueOrNull(previousValue, nameof(KiloActivePower.unit));
            //        var newUnitField = accessor.GetValueOrNull(previousValue, nameof(KiloActivePower.unit));

            //        var previousMultiplierField = accessor.GetValueOrNull(previousValue, nameof(KiloActivePower.multiplier));
            //        var newMultiplierField = accessor.GetValueOrNull(previousValue, nameof(KiloActivePower.multiplier));

            //        if (AreEqual(previousValueField, newValueField)
            //            && AreEqual(previousUnitField, newUnitField)
            //            && AreEqual(previousMultiplierField, newMultiplierField)) return EmptyPropMod;

            //        return (
            //            new PropertyModification
            //            {
            //                Name = property,
            //                IsReference = false,
            //                Multiplier = (UnitMultiplier)newMultiplierField,
            //                Unit = (UnitSymbol)newUnitField,
            //                Value = FormatValue(newValueField)
            //            },
            //            new PropertyModification
            //            {
            //                Name = property,
            //                IsReference = false,
            //                Multiplier = (UnitMultiplier)previousMultiplierField,
            //                Unit = (UnitSymbol)previousUnitField,
            //                Value = FormatValue(previousValueField)
            //            }
            //        );
            //    };
            //}

            //return (prev, nev) =>
            //{
            //    if (AreEqual(prev, nev)) return EmptyPropMod;

            //    return (
            //        new PropertyModification { Name = property, IsReference = false, Value = nev?.ToString() },
            //        new PropertyModification { Name = property, IsReference = false, Value = prev?.ToString() }
            //    );
            //};
        }

        static string EncodeRef(string type, string @ref) => $"{type}/{@ref}";

        static bool IsReferenceType(TypeAccessor accessor)
        {
            var members = accessor.GetMembers().ToArray();

            return members.Any(m => m.Name == nameof(PowerSystemResourceAssets.referenceType))
                   && members.Any(m => m.Name == nameof(PowerSystemResourceAssets.@ref));
        }

        static string EncodeCoords2(double newX, double newY)
        {
            return string.Concat("[", newX.ToString(InvariantCulture), ",", newY.ToString(InvariantCulture), "]");
        }

        static Func<PropertyModification, object> CreateValueGetter(Type type, TypeAccessor accessor, string property)
        {
            var memberType = GetMemberType(type, accessor, property);
            var valueAccessor = TypeAccessor.Create(memberType);

            return moditication => moditication.Value;

            //if (IsReferenceType(valueAccessor))
            //{
            //    return modification =>
            //    {
            //        var newValue = valueAccessor.CreateNew();
            //        var parts = modification.Value.Split('/').Select(v => v.Trim()).ToArray();

            //        if (parts.Length != 2)
            //        {
            //            throw new FormatException($"Could not turn the text '{modification.Value}' into a reference - expected exactly two parts separated by /");
            //        }

            //        valueAccessor[newValue, nameof(PowerSystemResourceAssets.referenceType)] = parts[0];
            //        valueAccessor[newValue, nameof(PowerSystemResourceAssets.@ref)] = parts[1];

            //        return newValue;
            //    };
            //}

            //if (IsValueType(accessor))
            //{
            //    return modification =>
            //    {
            //        var newValue = valueAccessor.CreateNew();

            //        valueAccessor[newValue, "Value"] = double.Parse(modification.Value, InvariantCulture);
            //        valueAccessor[newValue, "unit"] = modification.Unit;
            //        valueAccessor[newValue, "multiplier"] = modification.Multiplier;

            //        return newValue;
            //    };
            //}

            //if (memberType.IsEnum)
            //{
            //    return modification => modification.Value != null
            //        ? Enum.Parse(memberType, modification.Value)
            //        : null;
            //}

            //return modification =>
            //{
            //    try
            //    {
            //        return Convert.ChangeType(modification.Value, memberType);
            //    }
            //    catch (Exception exception)
            //    {
            //        throw new FormatException($"Could not turn '{modification.Value}' into a {memberType}", exception);
            //    }
            //};
        }

        static bool IsValueType(TypeAccessor accessor)
        {
            var members = accessor.GetMembers().ToList();
            var isValueType = members.Any(m => m.Name == nameof(KiloActivePower.Value))
                              && members.Any(m => m.Name == nameof(KiloActivePower.unit))
                              && members.Any(m => m.Name == nameof(KiloActivePower.multiplier));
            return isValueType;
        }

        static string FormatValue(object value)
        {
            if (value is double doubleValue)
            {
                return doubleValue.ToString(InvariantCulture);
            }

            return value?.ToString();
        }

        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, object, (PropertyModification, PropertyModification)>>> Getters
            = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, object, (PropertyModification, PropertyModification)>>>();

        static readonly (PropertyModification, PropertyModification) EmptyPropMod = (null, null);
        static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

        static Dictionary<string, object> GetProperties(JObject obj) => obj.Properties()
            .ToDictionary(p => p.Name, p => p.Value.ToObject<object>());

        static bool AreEqual(object previousValue, object newValue)
        {
            return JsonConvert.SerializeObject(previousValue).Equals(JsonConvert.SerializeObject(newValue));
            
            if (ReferenceEquals(null, previousValue) && ReferenceEquals(null, newValue)) return true;
            if (ReferenceEquals(null, previousValue)) return false;
            if (ReferenceEquals(null, newValue)) return false;


            return previousValue.Equals(newValue);
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
