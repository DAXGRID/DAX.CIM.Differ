using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DAX.CIM.Differ.Tests.Stubs;
using DAX.CIM.PhysicalNetworkModel;
using DAX.Cson;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Testy;
using Testy.Extensions;
// ReSharper disable ReturnValueOfPureMethodIsNotUsed

namespace DAX.CIM.Differ.Tests.Speed
{
    [TestFixture]
    public class SeeHowFastWeCanGenerateDiff : FixtureBase
    {
        CimObjectFactory _factory;
        CsonSerializer _serializer;
        CimDiffer _differ;

        protected override void SetUp()
        {
            _factory = new CimObjectFactory();
            _serializer = new CsonSerializer();
            _differ = new CimDiffer();
        }

        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        public void MeasureDiff(int count)
        {
            var originalObjects = _factory.Read().Take(count).ToList();
            var mutatedObjects = Mutate(originalObjects).ToList();

            var diffStopwatch = Stopwatch.StartNew();
            var diff = _differ.GetDiff(originalObjects, mutatedObjects).ToList();
            var totalDiffSeconds = diffStopwatch.Elapsed.TotalSeconds;
            Console.WriteLine($"Generating diff for {count} objects took {totalDiffSeconds} - that's {count / totalDiffSeconds:0.0} obj/s");

            var applyStopwatch = Stopwatch.StartNew();
            _differ.ApplyDiff(originalObjects, diff).ToList();
            var totalApplySeconds = applyStopwatch.Elapsed.TotalSeconds;
            Console.WriteLine($"Applying diff for {count} objects took {totalApplySeconds} - that's {count / totalApplySeconds:0.0} obj/s");
        }

        IEnumerable<IdentifiedObject> Mutate(List<IdentifiedObject> source)
        {
            var random = new Random();

            foreach (var obj in source)
            {
                // 20% chance we will mutate
                if (random.Next(5) != 0) continue;

                var cson = _serializer.SerializeObject(obj);
                var jObject = JObject.Parse(cson);
                var properties = jObject.Properties().Select(p => p.Name).ToArray();

                var propertiesToMutate = properties.InRandomOrder()
                    .Take(random.Next(properties.Length / 2))
                    .ToArray();

                var otherJObject = JObject.Parse(_serializer.SerializeObject(_factory.Create(obj.GetType())));

                foreach (var property in propertiesToMutate)
                {
                    jObject[property] = otherJObject[property];
                }

                yield return _serializer.DeserializeObject(jObject.ToString());
            }
        }
    }
}