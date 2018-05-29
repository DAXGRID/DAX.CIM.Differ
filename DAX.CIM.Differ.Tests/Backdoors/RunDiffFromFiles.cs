using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using DAX.CIM.PhysicalNetworkModel;
using DAX.Cson;
using NUnit.Framework;
using Testy;

namespace DAX.CIM.Differ.Tests.Backdoors
{
    [TestFixture]
    [Ignore("Can be manually executed to check the result of diffing two specific files")]
    public class RunDiffFromFiles : FixtureBase
    {
        CimDiffer _differ;
        CsonSerializer _serializer;

        protected override void SetUp()
        {
            _differ = new CimDiffer();

            _serializer = new CsonSerializer();
        }

        [TestCase(@"C:\temp\cim-diff\delta1.jsonl", @"C:\temp\cim-diff\delta2.jsonl")]
        public void GenerateDiff(string initialStateFilePath, string newStateFilePath)
        {
            var initialState = ReadObjects(initialStateFilePath);
            var newState = ReadObjects(newStateFilePath);

            Console.WriteLine($"Read {initialState.Length} initial state objects and {newState.Length} new state objects");

            var diff = _differ.GetDiff(initialState, newState).ToArray();

            Console.WriteLine($"Got {diff.Length} data set members");
        }

        IdentifiedObject[] ReadObjects(string filePath)
        {
            if (!File.Exists(filePath)) throw new AssertionException($"Could not find file here: {filePath}");

            try
            {
                using (var source = File.OpenRead(filePath))
                {
                    return _serializer.DeserializeObjects(source).ToArray();
                }
            }
            catch (Exception exception)
            {
                throw new SerializationException($"An error occurred when deserializing {filePath}", exception);
            }
        }
    }
}