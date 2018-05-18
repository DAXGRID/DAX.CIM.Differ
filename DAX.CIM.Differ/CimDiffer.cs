using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using FastMember;
// ReSharper disable ReturnTypeCanBeEnumerable.Local
// ReSharper disable InconsistentNaming

namespace DAX.CIM.Differ
{
    /// <summary>
    /// Implementation of CIM differ
    /// </summary>
    public class CimDiffer
    {
        public IEnumerable<IdentifiedObject> ApplyDiff(IEnumerable<IdentifiedObject> state, IEnumerable<DataSetMember> diff)
        {
            var stateDictionary = state.ToDictionary(s => s.mRID);

            foreach (var dataSetMember in diff)
            {
                var change = dataSetMember.Change;

                if (change is ObjectCreation objectCreation)
                {
                    var identifiedObject = objectCreation.Object;

                    stateDictionary[identifiedObject.mRID] = identifiedObject;
                }
                else
                {
                    var targetObject = dataSetMember.TargetObject;
                    var mRID = targetObject.@ref;

                    if (change is ObjectDeletion)
                    {
                        stateDictionary.Remove(mRID);
                    }
                    else if (change is ObjectModification objectModification)
                    {
                        var partial = objectModification.Object;

                        if (!stateDictionary.TryGetValue(mRID, out var currentState))
                        {
                            throw new ArgumentException($"Could not find {targetObject.referenceType}/{mRID} to apply data set member {dataSetMember.mRID} to");
                        }

                        var newState = InnerApplyDiff(currentState, partial);

                        stateDictionary[newState.mRID] = newState;
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown ChangeSetMember subclass: {change.GetType()}");
                    }
                }
            }

            return stateDictionary.Values;
        }

        static IdentifiedObject InnerApplyDiff(IdentifiedObject currentState, IdentifiedObject partial)
        {
            var type = currentState.GetType();
            var typeAccessor = GetTypeAccessor(type);
            var properties = GetPropertyNames(type);

            var clone = Clone(currentState, properties, typeAccessor);

            foreach (var property in properties)
            {
                var value = typeAccessor[partial, property];

                if (ReferenceEquals(null, value)) continue;

                typeAccessor[clone, property] = value;
            }

            return clone;
        }

        static IdentifiedObject Clone(IdentifiedObject obj, string[] properties, TypeAccessor typeAccessor)
        {
            var clone = (IdentifiedObject)typeAccessor.CreateNew();

            foreach (var property in properties)
            {
                typeAccessor[clone, property] = typeAccessor[obj, property];
            }

            return clone;
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
                        Object = deletedObject
                    }
                };
            }

            var idsOfCommonObjects = previousStateDictionary.Keys.Intersect(newStateDictionary.Keys).ToList();

            foreach (var id in idsOfCommonObjects)
            {
                var previousInstance = previousStateDictionary[id];
                var newInstance = newStateDictionary[id];
                var objectType = previousInstance.GetType();

                var typeAccessor = GetTypeAccessor(objectType);
                var propertyNames = GetPropertyNames(objectType);

                object change = null;
                object reverseChange = null;

                foreach (var property in propertyNames)
                {
                    var previousValue = typeAccessor[previousInstance, property];
                    var newValue = typeAccessor[newInstance, property];

                    if (AreEqual(previousValue, newValue)) continue;

                    if (change == null)
                    {
                        change = typeAccessor.CreateNew();
                        reverseChange = typeAccessor.CreateNew();
                    }

                    typeAccessor[change, property] = newValue;
                    typeAccessor[reverseChange, property] = previousValue;
                }

                if (change == null) continue;

                yield return new DataSetMember
                {
                    mRID = Guid.NewGuid().ToString(),
                    TargetObject = new TargetObject
                    {
                        @ref = newInstance.mRID,
                        referenceType = newInstance.GetType().Name,
                    },
                    Change = new ObjectModification { Object = (IdentifiedObject)change },
                    ReverseChange = new ObjectReverseModification { Object = (IdentifiedObject)reverseChange }
                };
            }
        }

        static TypeAccessor GetTypeAccessor(Type type) => TypeAccessors.GetOrAdd(type, _ => TypeAccessor.Create(type));

        static readonly ConcurrentDictionary<Type, TypeAccessor> TypeAccessors = new ConcurrentDictionary<Type, TypeAccessor>();

        static bool AreEqual(object previousValue, object newValue)
        {
            if (ReferenceEquals(null, previousValue) && ReferenceEquals(null, newValue)) return true;

            if (ReferenceEquals(null, previousValue)) return false;

            if (ReferenceEquals(null, newValue)) return false;

            // sopecial handling of sequences of objects
            if (previousValue is IEnumerable<object> previousSequence && newValue is IEnumerable<object> newSequence)
            {
                var previousValues = previousSequence.ToArray();
                var newValues = newSequence.ToArray();

                if (previousValues.Length != newValues.Length) return false;

                for (var index = 0; index < previousValues.Length; index++)
                {
                    if (!AreEqual(previousValues[index], newValues[index])) return false;
                }

                return true;
            }

            return Equals(previousValue, newValue);
        }

        static readonly ConcurrentDictionary<Type, string[]> PropertyNames = new ConcurrentDictionary<Type, string[]>();

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
