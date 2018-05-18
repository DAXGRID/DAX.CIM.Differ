using System;
using System.Collections.Generic;
using System.Linq;
using DAX.CIM.Differ.Tests.Extensions;
using DAX.CIM.Differ.Tests.Stubs;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
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

        protected override void SetUp()
        {
            _differ = new CimDiffer();

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
            var identifiedObject = _factory.Read().OfType<ConnectivityNode>().First();
            var newState = new IdentifiedObject[] { identifiedObject };


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
            var identifiedObject = _factory.Read().OfType<ConnectivityNode>().First();
            var oldState = new IdentifiedObject[] { identifiedObject };


            var result = _differ.GetDiff(oldState, None).ToList();


            Assert.That(result.Count, Is.EqualTo(1));

            var dataSetMember = result.First();

            Console.WriteLine(dataSetMember.ToPrettyJson());

            var changeSetMember = dataSetMember.Change;

            Assert.That(changeSetMember, Is.TypeOf<ObjectDeletion>());

            var change = dataSetMember.ReverseChange;

            Assert.That(change, Is.Not.Null);

            var roundtrippedIdentifiedObject = change.Object;

            Assert.That(roundtrippedIdentifiedObject.ToPrettyJson(), Is.EqualTo(identifiedObject.ToPrettyJson()));
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

            var newObject = objectModification.Object;
            var previousObject = dataSetMember.ReverseChange.Object;

            var newCson = newObject.ToPrettyCson();
            var oldCson = previousObject.ToPrettyCson();

            Console.WriteLine($@"{oldCson}

=>

{newCson}");

            var newJsonObject = JObject.Parse(newCson);
            var oldJsonObject = JObject.Parse(oldCson);

            Assert.That(oldJsonObject.Count, Is.EqualTo(2));
            Assert.That(newJsonObject.Count, Is.EqualTo(2));

            Assert.That(oldJsonObject["$type"].Value<string>(), Is.EqualTo(nameof(ConnectivityNode)));
            Assert.That(newJsonObject["$type"].Value<string>(), Is.EqualTo(nameof(ConnectivityNode)));

            Assert.That(oldJsonObject["description"].Value<string>(), Is.EqualTo("this is my connectivitivity nodode"));
            Assert.That(newJsonObject["description"].Value<string>(), Is.EqualTo("this is my connectivity node"));
        }
    }
}