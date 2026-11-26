using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GestureSign.Daemon
{
    internal static class JitWarmUp
    {
        private const BindingFlags AllMethods = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Compiles every non-generic method of the given types (and their nested types).</summary>
        public static int Prepare(params Type[] types)
        {
            var count = 0;
            foreach (var type in types)
                count += PrepareType(type);
            return count;
        }

        private static int PrepareType(Type type)
        {
            if (type.ContainsGenericParameters)
                return 0;
            var count = 0;
            foreach (var method in type.GetMethods(AllMethods))
                count += PrepareMethod(method);
            foreach (var constructor in type.GetConstructors(AllMethods))
                count += PrepareMethod(constructor);
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                count += PrepareType(nested);
            return count;
        }

        private static int PrepareMethod(MethodBase method)
        {
            if (method.IsAbstract || method.ContainsGenericParameters ||
                (method.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0 ||
                (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                return 0;
            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                return 1;
            }
            catch
            {
                return 0;
            }
        }
    }
}
