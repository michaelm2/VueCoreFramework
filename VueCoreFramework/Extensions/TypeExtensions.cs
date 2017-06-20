﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace VueCoreFramework.Extensions
{
    public static class TypeExtensions
    {
        /// <summary>
        /// Gets an Attribute of the given type for an object.
        /// </summary>
        public static T GetAttribute<T>(this object value) where T : Attribute
        {
            var type = value.GetType();
            var memberInfo = type.GetMember(value.ToString());
            var attributes = memberInfo.FirstOrDefault()?.GetCustomAttributes(typeof(T), false);
            return attributes?.FirstOrDefault() as T;
        }

        private static HashSet<Type> integralTypes = new HashSet<Type>
        {
            typeof(byte), typeof(sbyte),
            typeof(short), typeof(ushort),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong)
        };

        private static HashSet<Type> realTypes = new HashSet<Type>
        {
            typeof(float), typeof(double), typeof(decimal)
        };

        /// <summary>
        /// Determines if the Type is an interger-type numeric type.
        /// </summary>
        /// <remarks>
        /// Integer-type numeric types are considered to include byte, sbyte, short, ushort, int,
        /// uint, long, and ulong.
        /// </remarks>
        public static bool IsIntegralNumeric(this Type type)
            => integralTypes.Contains(type)
            || integralTypes.Contains(Nullable.GetUnderlyingType(type));
        
        /// <summary>
        /// Determines if the Type is a numeric type.
        /// </summary>
        public static bool IsNumeric(this Type type)
            => IsIntegralNumeric(type)
            || IsRealNumeric(type);

        /// <summary>
        /// Determines if the Type is a real-type numeric type.
        /// </summary>
        /// <remarks>
        /// Real-type numeric types are considered to include float, double, and decimal.
        /// </remarks>
        public static bool IsRealNumeric(this Type type)
            => realTypes.Contains(type)
            || realTypes.Contains(Nullable.GetUnderlyingType(type));
    }
}