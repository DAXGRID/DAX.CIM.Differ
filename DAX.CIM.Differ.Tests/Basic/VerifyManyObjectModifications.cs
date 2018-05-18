﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using DAX.CIM.Differ.Tests.Extensions;
using DAX.CIM.Differ.Tests.Stubs;
using DAX.CIM.PhysicalNetworkModel;
using FastMember;
using NUnit.Framework;
using Testy;
using Testy.Extensions;

namespace DAX.CIM.Differ.Tests.Basic
{
    [TestFixture]
    public class VerifyManyObjectModifications : FixtureBase
    {
        readonly Random _random = new Random(DateTime.Now.GetHashCode());

        CimDiffer _differ;
        CimObjectFactory _factory;

        protected override void SetUp()
        {
            _differ = new CimDiffer();

            _factory = new CimObjectFactory();
        }

        [Test]
        [TestCase(1, true)]
        public void ObjectModificationsLookFine(int count, bool verbose)
        {
            var identifiedObjects = _factory.Read().Take(count);

            foreach (var identifiedObject in identifiedObjects)
            {
                CheckIt(identifiedObject, verbose);
            }
        }

        void CheckIt(IdentifiedObject currentState, bool verbose)
        {
            var type = currentState.GetType();
            var newState = _factory.Read().First(o => o.GetType() == type);
            var typeAccessor = TypeAccessor.Create(newState.GetType());

            // only change some of the properties
            var properties = GetProperties(typeAccessor);
            Console.WriteLine($"Type {type} has these properties:");
            foreach (var property in properties)
            {
                Console.WriteLine($"    {property}");
                try
                {
                    // 20% chance we will change the value
                    if (_random.Next(5) == 0) continue;

                    // 80% chance we will keep the previous value
                    typeAccessor[newState, property] = typeAccessor[currentState, property];
                }
                catch (Exception exception)
                {
                    throw new ApplicationException($"Could not set property '{property}'", exception);
                }
            }

            // it's the same object
            newState.mRID = currentState.mRID;

            if (verbose)
            {
                Console.WriteLine($@"Getting diff for this object modification:

{currentState.ToPrettyCson()}

=>

{newState.ToPrettyCson()}
");
            }

            var dataSetMembers = _differ.GetDiff(new[] { currentState }, new[] { newState }).ToList();

            if (dataSetMembers.Count == 0)
            {
                Console.WriteLine("No change :)");

                Assert.That(newState.ToPrettyCson(), Is.EqualTo(currentState.ToPrettyCson()), 
                    "Didn't get a result from the differ, so the two states should be equal");

                return;
            }

            Assert.That(dataSetMembers.Count, Is.EqualTo(1));

            var dataSetMember = dataSetMembers.First();

            Console.WriteLine($@"Got this diff:

{dataSetMember.ToPrettyCson()}
");

            var roundtrippedSequence = _differ.ApplyDiff(new[] { currentState }, dataSetMembers).ToList();

            Assert.That(roundtrippedSequence.Count, Is.EqualTo(1));

            var roundtrippedState = roundtrippedSequence.First();

            Console.WriteLine($@"Got this roundtripped state:

{roundtrippedState.ToPrettyCson()}");

            Assert.That(roundtrippedState.ToPrettyJson(), Is.EqualTo(newState.ToPrettyJson()));
        }

        static IEnumerable<string> GetProperties(TypeAccessor typeAccessor)
        {
            return typeAccessor.GetMembers()
                .Where(m => m.GetAttribute(typeof(XmlIgnoreAttribute), true) == null)
                .Where(m => m.GetAttribute(typeof(IgnoreDataMemberAttribute), true) == null)
                .Select(m => m.Name);
        }
    }
}