using System;

namespace DAX.CIM.Differ.Tests.Extensions
{
    static class DoubleExt
    {
        public static bool IsWithinTolerance(this double value, double otherValue, double tolerance = 0.00000001)
        {
            var diff = value-otherValue;

            return Math.Abs(diff) < tolerance;
        }    
    }
}