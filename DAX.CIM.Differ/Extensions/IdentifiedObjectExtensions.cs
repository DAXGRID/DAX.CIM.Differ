using DAX.CIM.PhysicalNetworkModel;
using Newtonsoft.Json;

namespace DAX.CIM.Differ.Extensions
{
    static class IdentifiedObjectExtensions
    {
        static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

        public static TIdentifiedObject Clone<TIdentifiedObject>(this TIdentifiedObject identifiedObject)
            where TIdentifiedObject : IdentifiedObject
        {
            return JsonConvert.DeserializeObject<TIdentifiedObject>(
                JsonConvert.SerializeObject(identifiedObject, JsonSerializerSettings),
                JsonSerializerSettings
            );
        }
    }
}