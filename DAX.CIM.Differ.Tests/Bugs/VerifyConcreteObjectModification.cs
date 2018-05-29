using System;
using System.Linq;
using DAX.CIM.Differ.Tests.Extensions;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using NUnit.Framework;
using Testy;

namespace DAX.CIM.Differ.Tests.Bugs
{
    [TestFixture]
    public class VerifyConcreteObjectModification : FixtureBase
    {
        const string InitialStateCson = @"{
  ""$type"": ""SubGeographicalRegion"",
  ""Substations"": [
    ""referenceType4cac7355-f92f-408e-9c92-1a852302cc54/refc503c7fd-26ce-47db-9941-fe41c21b106b"",
    ""referenceType61093a07-e920-4e71-9b8e-c1aca27b45a1/refebe04f21-0251-4c96-93be-eb971d6304e8"",
    ""referenceTypef098fbba-4bb4-49f0-9ea7-81d241351126/ref15e50ad8-a588-41f6-a484-8f3a702a2747""
  ],
  ""mRID"": ""mRID4884985d-8c9c-4246-bac9-ea487ac3ea5d"",
  ""description"": ""descriptionc076fdae-e5f3-49c4-9bf6-ad98c256ae08"",
  ""name"": ""name929e6cc8-7d1c-4385-a1b7-9682baa096e5"",
  ""Names"": [
    ""referenceType0df38df9-95cb-4f7f-ba2d-2101d15a46bb/reff8692c46-b8ea-4745-a3a2-7f39b2a9719b"",
    ""referenceType6e5d27e3-8728-4bfa-b9b0-77513f167577/ref680c0a5c-6346-4056-b412-ad01a9d6fd0d"",
    ""referenceType5a369e4c-fb4f-43de-a2dc-14f1131c3a48/ref07150c86-8765-4e16-9d50-a4810c82530b""
  ]
}
";

        const string NewStateCson = @"{
  ""$type"": ""SubGeographicalRegion"",
  ""Substations"": [
    ""referenceTypee6d72a01-fc54-49ce-9136-4b9dd6f87426/ref79cecb25-6957-486b-bb9c-a4e2d228b96d"",
    ""referenceType49fca46f-3e6a-4e79-ac26-90cb25af1662/reffba6f1a0-0ecb-4240-9f4b-3aef0f0e5161"",
    ""referenceType353aec32-c976-4f9a-bc3d-3025f326f460/refbb890725-ccf1-4c39-bb95-6aba3ac409aa""
  ],
  ""mRID"": ""mRID4884985d-8c9c-4246-bac9-ea487ac3ea5d"",
  ""description"": ""descriptionc076fdae-e5f3-49c4-9bf6-ad98c256ae08"",
  ""name"": ""name542f4e01-1363-4320-ab92-19214bd75917"",
  ""Names"": [
    ""referenceType0df38df9-95cb-4f7f-ba2d-2101d15a46bb/reff8692c46-b8ea-4745-a3a2-7f39b2a9719b"",
    ""referenceType6e5d27e3-8728-4bfa-b9b0-77513f167577/ref680c0a5c-6346-4056-b412-ad01a9d6fd0d"",
    ""referenceType5a369e4c-fb4f-43de-a2dc-14f1131c3a48/ref07150c86-8765-4e16-9d50-a4810c82530b""
  ]
}";

        [Test]
        public void GetDiff()
        {
            var differ = new CimDiffer();
            var serializer = new CsonSerializer();

            var initialState = serializer.DeserializeObject(InitialStateCson);
            var newState = serializer.DeserializeObject(NewStateCson);

            var dataSetMember = differ.GetDiff(new[] {initialState}, new[] {newState}).First();

            Console.WriteLine($@"{InitialStateCson}

=> 

{NewStateCson}

Yields this diff:

{dataSetMember.ToPrettyCson()}");

            var objectModification = (ObjectModification)dataSetMember.Change;

            Assert.That(objectModification.Properties.Keys.OrderBy(n => n).ToArray(), Is.EqualTo(new[]
            {
                "$type",
                "name",
                "Substations"
            }));
        }
    }
}