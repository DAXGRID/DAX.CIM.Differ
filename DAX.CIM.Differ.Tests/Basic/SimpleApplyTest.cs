using System;
using System.Collections.Generic;
using System.Linq;
using DAX.CIM.PhysicalNetworkModel;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Testy;

namespace DAX.CIM.Differ.Tests.Basic
{
    [TestFixture]
    public class SimpleApplyTest : FixtureBase
    {
        CimDiffer _differ;
        CsonSerializer _serializer;

        protected override void SetUp()
        {
            _serializer = new CsonSerializer();
            _differ = new CimDiffer();
        }

        [Test]
        public void CanApplyDiff_ObjectDeletion()
        {
            var deletedObject = new ConnectivityNode
            {
                mRID = "123",
                description = "this is my connectivitivity nodode",
                name = "Connode"
            };

            var change = new DataSetMember
            {
                mRID = Guid.NewGuid().ToString(),
                Change = new ObjectDeletion(),
                TargetObject = new TargetObject {@ref = deletedObject.mRID, referenceType = nameof(ConnectivityNode)},
                ReverseChange = new ObjectReverseModification
                {
                    Properties = JObject.Parse(_serializer.SerializeObject(deletedObject))
                        .Properties()
                        .ToDictionary(p => p.Name, p => p.Value.ToObject<object>())
                }
            };

            var result = _differ.ApplyDiff(new[] { deletedObject }, new[] { change }).ToList();

            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void CanApplyDiff_ObjectCreation()
        {
            var newObject = new ConnectivityNode
            {
                mRID = "123",
                description = "this is my connectivitivity nodode",
                name = "Connode"
            };

            var change = new DataSetMember
            {
                mRID = Guid.NewGuid().ToString(),
                Change = new ObjectCreation { Object = newObject },
                TargetObject = new TargetObject { @ref = newObject.mRID, referenceType = nameof(ConnectivityNode) },
            };

            var result = _differ.ApplyDiff(new IdentifiedObject[0], new[] { change }).ToList();

            Assert.That(result.Count, Is.EqualTo(1));

            var resultObject = result.First();
            Assert.That(resultObject.mRID, Is.EqualTo("123"));
            Assert.That(resultObject.description, Is.EqualTo("this is my connectivitivity nodode"));
            Assert.That(resultObject.name, Is.EqualTo("Connode"));
        }

        [Test]
        public void CanApplyDiff_ObjectModification()
        {
            var target = new ConnectivityNode
            {
                mRID = "123",
                description = "this is my connectivitivity nodode",
                name = "Connode"
            };

            var change = new DataSetMember
            {
                mRID = Guid.NewGuid().ToString(),
                Change = new ObjectModification
                {
                    Properties = new Dictionary<string, object>
                    {
                        {"description", "this is my connectivity node (spelning corected)"}
                    }
                },

                // not necessary for this test, but play it realistic
                ReverseChange = new ObjectReverseModification
                {
                    Properties = new Dictionary<string, object>
                    {
                        {"description", "this is my connectivitivity nodode"}
                    }
                },

                TargetObject = new TargetObject { @ref = target.mRID, referenceType = nameof(ConnectivityNode) }
            };

            var result = _differ.ApplyDiff(new[] { target }, new[] { change }).First();

            Assert.That(result.description, Is.EqualTo("this is my connectivity node (spelning corected)"));
        }
    }
}