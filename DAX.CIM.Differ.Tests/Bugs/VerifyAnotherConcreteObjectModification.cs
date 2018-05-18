using System;
using System.Linq;
using DAX.CIM.Differ.Tests.Extensions;
using DAX.CIM.PhysicalNetworkModel.Changes;
using DAX.Cson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Testy;
using Testy.Extensions;

namespace DAX.CIM.Differ.Tests.Bugs
{
    [TestFixture]
    public class VerifyAnotherConcreteObjectModification : FixtureBase
    {
        CimDiffer _differ;
        CsonSerializer _serializer;

        protected override void SetUp()
        {
            _serializer = new CsonSerializer();
            _differ = new CimDiffer();
        }

        const string PreviousCson = @"{
  ""$type"": ""ACLineSegment"",
  ""b0ch"": ""156 cA"",
  ""bch"": ""4 ddeg"",
  ""g0ch"": ""169 GdegC"",
  ""gch"": ""153 kF"",
  ""r"": ""96 mg"",
  ""r0"": ""47 Mh"",
  ""x"": ""70 microH"",
  ""x0"": ""119 nHz"",
  ""length"": ""12 J"",
  ""BaseVoltage"": 86.0,
  ""aggregate"": true,
  ""EquipmentContainer"": ""referenceType2e6b50dc-805a-4790-bdff-ac53d0308d7b/ref7c4db13d-6085-4c0f-9105-07d24693d1f9"",
  ""PSRType"": ""PSRType9bb389d3-7dd4-4910-a86d-a3a7238ac9e7"",
  ""Location"": ""referenceType6309b86a-9b9a-471f-98b8-87ba4c7c2e0b/refff8a254a-959f-49bd-a78b-8ac1b7b17273"",
  ""Assets"": ""referenceType6320b404-c963-4b02-b5a8-e3a2e92c3334/ref2a9a24bf-bfcf-4760-9549-1bce71b1de7d"",
  ""mRID"": ""mRID7b631326-748c-46d5-b318-073be484f911"",
  ""description"": ""description8187e5b2-ab88-4791-9883-ceff9e4e5f7f"",
  ""name"": ""name98253cf9-d408-48e6-a136-53af1c966eec"",
  ""Names"": [
    ""referenceType8922dc74-4b24-4756-beca-aca4516abdd9/ref54a6ac08-e82a-453c-aa37-30055de21b94"",
    ""referenceTypec3aebacd-d207-47fb-a9dc-0479ff8595d7/ref1bd92a53-0529-40a2-8767-78060dbc1ee0"",
    ""referenceTypea8e40a6d-bdc0-4fce-9135-29a93c67743f/reff96c085d-1a97-4e98-96b9-32baa8a2ff8e""
  ]
}

";

        const string NewCson = @"{
  ""$type"": ""ACLineSegment"",
  ""b0ch"": ""156 cA"",
  ""bch"": ""4 ddeg"",
  ""g0ch"": ""169 GdegC"",
  ""gch"": ""29100 GW"",
  ""r"": ""96 mg"",
  ""r0"": ""47 Mh"",
  ""x"": ""70 microH"",
  ""x0"": ""119 nHz"",
  ""length"": ""12 J"",
  ""BaseVoltage"": 86.0,
  ""aggregate"": true,
  ""EquipmentContainer"": ""referenceType2e6b50dc-805a-4790-bdff-ac53d0308d7b/ref7c4db13d-6085-4c0f-9105-07d24693d1f9"",
  ""PSRType"": ""PSRTypefe03822c-a151-4318-bc78-3f90e45b51ec"",
  ""Location"": ""referenceTypeaf864274-8865-4e45-8a99-0f169a58d4bd/refa50f4d68-90fc-4f5e-a0be-0fa42797ec17"",
  ""Assets"": ""referenceType6320b404-c963-4b02-b5a8-e3a2e92c3334/ref2a9a24bf-bfcf-4760-9549-1bce71b1de7d"",
  ""mRID"": ""mRID7b631326-748c-46d5-b318-073be484f911"",
  ""description"": ""description8187e5b2-ab88-4791-9883-ceff9e4e5f7f"",
  ""name"": ""name98253cf9-d408-48e6-a136-53af1c966eec"",
  ""Names"": [
    ""referenceType48720356-8c6b-447b-9062-8440288dc383/ref2686b3fc-c294-47fd-87af-4239439c8639"",
    ""referenceType91581677-2292-4e24-9fa7-b41e552815f4/reff387b614-333b-4ea9-a8ae-6f34ff8cf653"",
    ""referenceType135e4458-e28f-4e36-98d8-b7bd27e7d17e/refb8b180d0-9503-44d6-9bd9-19f8caafbcb5""
  ]
}";

        [Test]
        public void WorksAsExpected()
        {
            var previousState = _serializer.DeserializeObject(PreviousCson);
            var newState = _serializer.DeserializeObject(NewCson);

            var dataSetMembers = _differ.GetDiff(new[] {previousState}, new[] {newState}).First();

            var cson = dataSetMembers.ToPrettyCson();

            var objectModification = (ObjectModification)dataSetMembers.Change;
            var change = objectModification.ToPrettyJson();

            Console.WriteLine($@"{PreviousCson}

=>

{NewCson}

yields this diff:

{change}");

            var propertyNamesIncludedInTheDiff = objectModification.Properties.Keys.OrderBy(n => n).ToArray();

            Assert.That(propertyNamesIncludedInTheDiff, Is.EqualTo(new[]
            {
                "$type",
                "gch",
                "Location",
                "Names",
                "PSRType",
            }));
        }
    }
}