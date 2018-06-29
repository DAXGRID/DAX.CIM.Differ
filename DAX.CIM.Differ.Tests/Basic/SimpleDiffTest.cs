using System;
using System.Collections.Generic;
using System.Linq;
using DAX.CIM.Differ.Tests.Extensions;
using DAX.CIM.Differ.Tests.Stubs;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using FastMember;
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

            //var properties = change.Modifications.ToDictionary(m => m.Name, m => m.Value);

            var originalProperties = JObject.Parse(_serializer.SerializeObject(identifiedObject)).Properties()
                .ToDictionary(p => p.Name, p => p.Value.ToObject<object>());

            var properties = ReconstructObject<ConnectivityNode>(dataSetMember.TargetObject.@ref, change.Modifications);

            AssertDictionariesAreTheSame(properties, originalProperties);
        }

        Dictionary<string, object> ReconstructObject<T>(string mRID, PropertyModification[] modifications)
        {
            var accessor = TypeAccessor.Create(typeof(T));

            var target = (IdentifiedObject)accessor.CreateNew();
            accessor[target, nameof(IdentifiedObject.mRID)] = mRID;

            var dataSetMember = new DataSetMember
            {
                mRID = Guid.NewGuid().ToString(),
                TargetObject = new TargetObject { @ref = mRID, referenceType = typeof(T).Name },
                Change = new ObjectModification { Modifications = modifications }
            };

            var result = new CimDiffer()
                .ApplyDiff(new[] { target }, new[] { dataSetMember })
                .Single();

            return JObject.Parse(_serializer.SerializeObject(result)).Properties()
                .ToDictionary(p => p.Name, p => p.Value.ToObject<object>());
        }

        static void AssertDictionariesAreTheSame(Dictionary<string, object> d1, Dictionary<string, object> d2)
        {
            var dict1 = d1.OrderBy(k => k.Key).ToPrettyJson();
            var dict2 = d2.OrderBy(k => k.Key).ToPrettyJson();

            Assert.That(dict1, Is.EqualTo(dict2), $@"This dictionary

{dict1}

was not equal to this dictionary

{dict2}");
        }

        [Test]
        public void CanDetectObjectModification_New()
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

            var modifications = objectModification.Modifications;
            var reverseModifications = dataSetMember.ReverseChange.Modifications;

            Assert.That(GetModification(modifications, "description").Value, Is.EqualTo("this is my connectivity node"));
            Assert.That(GetModification(reverseModifications, "description").Value, Is.EqualTo("this is my connectivitivity nodode"));
        }

        [Test]
        public void CanDetectObjectModification_NewWithValue()
        {
            var previousState = new RatioTapChanger
            {
                mRID = "123",
                stepVoltageIncrement = new PerCent
                {
                    Value = 100,
                    multiplier = UnitMultiplier.k,
                    unit = UnitSymbol.V
                }
            };

            var newState = previousState.Clone();

            // voltage rises to 102 kV
            newState.stepVoltageIncrement = new PerCent
            {
                Value = 102,
                multiplier = UnitMultiplier.k,
                unit = UnitSymbol.V
            };

            var dataSetMembers = _differ.GetDiff(new IdentifiedObject[] { previousState }, new IdentifiedObject[] { newState }).ToList();

            Assert.That(dataSetMembers.Count, Is.EqualTo(1));

            var dataSetMember = dataSetMembers.First();

            var cson = dataSetMember.ToPrettyCson();

            Console.WriteLine(cson);

            var changeSetMember = dataSetMember.Change;

            Assert.That(changeSetMember, Is.TypeOf<ObjectModification>());

            var objectModification = (ObjectModification)changeSetMember;

            var modifications = objectModification.Modifications;
            var reverseModifications = dataSetMember.ReverseChange.Modifications;

            var forward = GetModification(modifications, nameof(RatioTapChanger.stepVoltageIncrement));
            var reverse = GetModification(reverseModifications, nameof(RatioTapChanger.stepVoltageIncrement));

            Assert.That(forward.Value is PerCent forwardValue && forwardValue.Value.IsWithinTolerance(102), $"value of {JsonConvert.SerializeObject(forward)} is not close enough to 102");
            Assert.That(reverse.Value is PerCent reverseValue && reverseValue.Value.IsWithinTolerance(100), $"value of {JsonConvert.SerializeObject(reverse)} is not close enough to 100");
        }

        static PropertyModification GetModification(PropertyModification[] modifications, string name)
        {
            return modifications.FirstOrDefault(m => m.Name == name)
                   ?? throw new AssertionException($@"Could not find modification with name '{name}' among these:

{string.Join(Environment.NewLine, modifications.Select(m => $"    {m.Name}: {m.Value.ToJson()}"))}");
        }
    }
}