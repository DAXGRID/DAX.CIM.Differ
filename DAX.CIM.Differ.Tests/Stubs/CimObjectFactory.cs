using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using AutoFixture;
using AutoFixture.Kernel;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using FastMember;

namespace DAX.CIM.Differ.Tests.Stubs
{
    public class CimObjectFactory
    {
        readonly SpecimenContext _specimenContext;
        readonly List<Type> _objectTypes;
        readonly Random _random;

        public CimObjectFactory()
        {
            var fixture = new Fixture();

            fixture.Behaviors.OfType<ThrowingRecursionBehavior>().ToList()
                .ForEach(b => fixture.Behaviors.Remove(b));

            fixture.Behaviors.Add(new OmitOnRecursionBehavior());

            fixture.Customizations.Add(new IgnoredPropertyOmitter());

            _specimenContext = new SpecimenContext(fixture);

            _objectTypes = typeof(IdentifiedObject).Assembly
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => typeof(IdentifiedObject).IsAssignableFrom(t))
                .Except(new[]
                {
                    typeof(DataSetMember),
                })
                .ToList();

            _random = new Random(DateTime.Now.GetHashCode());
        }

        public IEnumerable<Type> GetObjectTypes() => _objectTypes.ToList();

        static readonly ConcurrentDictionary<Type, TypeAccessor> TypeAccessors = new ConcurrentDictionary<Type, TypeAccessor>();
        static readonly ConcurrentDictionary<Type, string[]> PropertyNames = new ConcurrentDictionary<Type, string[]>();

        public IEnumerable<IdentifiedObject> Read()
        {
            while (true)
            {
                var type = _objectTypes[_random.Next(_objectTypes.Count)];

                yield return Create(type);
            }
        }

        public IdentifiedObject Create<TIdentifiedObject>() where TIdentifiedObject : IdentifiedObject => Create(typeof(TIdentifiedObject));

        public IdentifiedObject Create(Type type)
        {
            try
            {
                var identifiedObject = (IdentifiedObject) _specimenContext.Resolve(type);
                var propertyNames = GetPropertyNames(type);
                var typeAccessor = GetTypeAccessor(type);

                foreach (var name in propertyNames)
                {
                    const string suffixToLookFor = "Specified";

                    // we set the value of all of these, depending on whether their corresponding values are set
                    if (name.EndsWith(suffixToLookFor))
                    {
                        var correpondingPropertyName = name.Substring(0, name.Length - suffixToLookFor.Length);

                        // we have some cases where we have the ****Specified property without the actual value property
                        if (!propertyNames.Contains(correpondingPropertyName)) continue;

                        try
                        {
                            var value = typeAccessor[identifiedObject, correpondingPropertyName];

                            typeAccessor[identifiedObject, name] = !ReferenceEquals(null, value);
                        }
                        catch (Exception exception)
                        {
                            throw new ApplicationException($"Error when trying to get value from property {correpondingPropertyName} from {type} in an attempt to set the value of {name} accordingly", exception);
                        }
                    }
                }

                return identifiedObject;
            }
            catch (Exception exception)
            {
                throw new ApplicationException($"Could not generate {type} with AutoFixture", exception);
            }
        }

        static string[] GetPropertyNames(Type type)
        {
            return PropertyNames.GetOrAdd(type, _ =>
            {
                var typeAccessor = GetTypeAccessor(type);
                var names = typeAccessor.GetMembers().Select(m => m.Name);
                return names.ToArray();
            });
        }

        static TypeAccessor GetTypeAccessor(Type type)
        {
            return TypeAccessors.GetOrAdd(type, TypeAccessor.Create);
        }

        class IgnoredPropertyOmitter : ISpecimenBuilder
        {
            public object Create(object request, ISpecimenContext context)
            {
                if (request is PropertyInfo propertyInfo)
                {
                    if (propertyInfo.GetCustomAttributes(typeof(IgnoreDataMemberAttribute)).Any())
                    {
                        return new OmitSpecimen();
                    }
                    if (propertyInfo.GetCustomAttributes(typeof(XmlIgnoreAttribute)).Any())
                    {
                        return new OmitSpecimen();
                    }
                }

                return new NoSpecimen();
            }
        }
    }
}