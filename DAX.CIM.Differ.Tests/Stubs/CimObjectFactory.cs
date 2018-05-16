using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using AutoFixture;
using AutoFixture.Kernel;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;

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

        public IEnumerable<IdentifiedObject> Read()
        {
            IdentifiedObject Resolve(Type type)
            {
                try
                {
                    return (IdentifiedObject)_specimenContext.Resolve(type);
                }
                catch (Exception exception)
                {
                    throw new ApplicationException($"Could not generate {type} with AutoFixture", exception);
                }
            }

            while (true)
            {
                var type = _objectTypes[_random.Next(_objectTypes.Count)];

                yield return Resolve(type);
            }
        }

        class IgnoredPropertyOmitter : ISpecimenBuilder
        {
            public object Create(object request, ISpecimenContext context)
            {
                if (request is PropertyInfo propertyInfo
                    && propertyInfo.GetCustomAttributes(typeof(IgnoreDataMemberAttribute)).Any())
                {
                    return new OmitSpecimen();
                }

                return new NoSpecimen();
            }
        }
    }
}