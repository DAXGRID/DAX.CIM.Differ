using FastMember;

namespace DAX.CIM.Differ.Extensions
{
    static class TypeAccessorExtensions
    {
        public static object GetValueOrNull(this TypeAccessor accessor, object target, string property) => target == null
            ? null
            : accessor[target, property];
    }
}