using System;
using System.Linq;
using DAX.CIM.PhysicalNetworkModel;
using DAX.Cson;
using Testy.Extensions;

namespace DAX.CIM.Differ.Tests.Extensions
{
    public static class CsonExtensions
    {
        static readonly CsonSerializer CsonSerializer = new CsonSerializer();

        public static string ToPrettyCson(this object obj)
        {
            if (!(obj is IdentifiedObject))
            {
                throw new ArgumentException($"The object is {obj.GetType()} and not IdentifiedObject. Only IdentifiedObject can be CSON-serialized");
            }

            var cson = CsonSerializer.SerializeObject((IdentifiedObject) obj);

            return cson.IndentJson();
        }
    }
}