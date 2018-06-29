using System;
using System.Linq;
using DAX.CIM.PhysicalNetworkModel;
using NUnit.Framework;
using Testy;
using Testy.Extensions;

namespace DAX.CIM.Differ.Tests.Bugs
{
    [TestFixture]
    public class CheckThatValueSpecifiedThing : FixtureBase
    {
        CimDiffer _differ;

        protected override void SetUp()
        {
            _differ = new CimDiffer();
        }

        [Test]
        public void CanDoIt()
        {
            var coil = new PetersenCoil
            {
                mRID = "known-id",
                aggregate = false
            };

            var moddedCoil = coil.Clone();
            moddedCoil.aggregate = true;

            var modifications = _differ.GetDiff(new[] { coil }, new[] { moddedCoil });

            Console.WriteLine(modifications.ToPrettyJson());

            var resultingCoil = _differ.ApplyDiff(new[] { coil }, modifications).OfType<PetersenCoil>().Single();

            Assert.That(resultingCoil.aggregate, Is.EqualTo(true));
            Assert.That(resultingCoil.aggregateSpecified, Is.EqualTo(true));
        }
    }
}