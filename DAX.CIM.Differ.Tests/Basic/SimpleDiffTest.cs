using System;
using System.Collections.Generic;
using System.Linq;
using DAX.CIM.Differ.Tests.Extensions;
using DAX.CIM.Differ.Tests.Stubs;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Testy;
using Testy.Extensions;

namespace DAX.CIM.Differ.Tests.Basic
{
    [TestFixture]
    public class SimpleDiffTest : FixtureBase
    {
        static readonly IEnumerable<IdentifiedObject> None = Enumerable.Empty<IdentifiedObject>();

        CimDiffer _differ;
        CimObjectFactory _factory;
        CsonSerializer _serializer;

        protected override void SetUp()
        {
            _differ = new CimDiffer();
            _serializer = new CsonSerializer();
            _factory = new CimObjectFactory();
        }

        [Test]
        public void CanDiffTwoEmptySequences()
        {
            var result = _differ.GetDiff(None, None).ToList();

            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void CanDetectObjectCreation()
        {
            var identifiedObject = _factory.Create<ConnectivityNode>();
            var newState = new[] { identifiedObject };

            var result = _differ.GetDiff(None, newState).ToList();


            Assert.That(result.Count, Is.EqualTo(1));

            var dataSetMember = result.First();

            Console.WriteLine(dataSetMember.ToPrettyJson());

            var changeSetMember = dataSetMember.Change;

            Assert.That(changeSetMember, Is.TypeOf<ObjectCreation>());

            var objectCreation = (ObjectCreation)changeSetMember;
            var roundtrippedIdentifiedObject = objectCreation.Object;

            Assert.That(roundtrippedIdentifiedObject.ToPrettyJson(), Is.EqualTo(identifiedObject.ToPrettyJson()));
        }

        [Test]
        public void CanDetectObjectDeletion()
        {
            var identifiedObject = _factory.Create<ConnectivityNode>();
            var oldState = new[] { identifiedObject };

            var result = _differ.GetDiff(oldState, None).ToList();


            Assert.That(result.Count, Is.EqualTo(1));

            var dataSetMember = result.First();

            Console.WriteLine(dataSetMember.ToPrettyJson());

            var changeSetMember = dataSetMember.Change;

            Assert.That(changeSetMember, Is.TypeOf<ObjectDeletion>());

            var change = dataSetMember.ReverseChange;

            Assert.That(change, Is.Not.Null);

            var properties = change.Properties;

            var originalProperties = JObject.Parse(_serializer.SerializeObject(identifiedObject))
                .Properties()
                .ToDictionary(p => p.Name, p => p.Value.ToObject<object>());

            AssertDictionariesAreTheSame(properties, originalProperties);
        }

        static void AssertDictionariesAreTheSame(Dictionary<string, object> d1, Dictionary<string, object> d2)
        {
            var dict1 = d1.ToPrettyJson();
            var dict2 = d2.ToPrettyJson();

            Assert.That(dict1, Is.EqualTo(dict2) );
        }

        [Test]
        public void CanDetectObjectModification()
        {
            var previousState = new ConnectivityNode
            {
                mRID = "123",
                description = "this is my connectivitivity nodode",
                name = "Connode"
            };

            var newState = previousState.Clone();

            newState.description = "this is my connectivity node";

            var dataSetMembers = _differ.GetDiff(new IdentifiedObject[] { previousState }, new IdentifiedObject[] { newState }).ToList();

            Assert.That(dataSetMembers.Count, Is.EqualTo(1));

            var dataSetMember = dataSetMembers.First();

            var cson = dataSetMember.ToPrettyCson();

            Console.WriteLine(cson);

            var changeSetMember = dataSetMember.Change;

            Assert.That(changeSetMember, Is.TypeOf<ObjectModification>());

            var objectModification = (ObjectModification)changeSetMember;

            var change = objectModification.Properties;
            var reverse = dataSetMember.ReverseChange.Properties;

            Assert.That(change, Contains.Key("description").And.ContainValue("this is my connectivity node"));
            Assert.That(reverse, Contains.Key("description").And.ContainValue("this is my connectivitivity nodode"));
        }
    }
}