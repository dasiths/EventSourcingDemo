using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using NEventLite.Core;
using NEventLite.Core.Domain;

namespace NEventLite.Util
{
    public static class ReflectionHelper
    {
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, string>> AggregateEventHandlerCache =
            new ConcurrentDictionary<Type, ConcurrentDictionary<Type, string>>();

        public static Dictionary<Type, string> FindEventHandlerMethodsInAggregate<TAggregateKey, TEventKey>(Type aggregateType)
        {
            if (AggregateEventHandlerCache.ContainsKey(aggregateType) == false)
            {
                var eventHandlers = new ConcurrentDictionary<Type, string>();

                var typeArgs = new [] { aggregateType, typeof(TAggregateKey), typeof(TEventKey) };
                var eventType = typeof(IEvent<,,>).MakeGenericType(typeArgs);

                var voidMethods = aggregateType.GetMethodsBySig(typeof(void), typeof(InternalEventHandler),
                    true, eventType).ToList();
                var voidMethodsWithReplay = aggregateType.GetMethodsBySig(typeof(void), typeof(InternalEventHandler),
                    true, eventType, typeof(ReplayStatus)).ToList();
                var asyncMethods = aggregateType.GetMethodsBySig(typeof(Task), typeof(InternalEventHandler),
                    true, eventType).ToList();
                var asyncMethodsWithReplay = aggregateType.GetMethodsBySig(typeof(Task), typeof(InternalEventHandler),
                    true, eventType, typeof(ReplayStatus)).ToList();

                var methods = voidMethods.Union(asyncMethods).Union(voidMethodsWithReplay).Union(asyncMethodsWithReplay).ToList();

                if (methods.Any())
                {
                    foreach (var m in methods)
                    {
                        var parameter = m.GetParameters().First();
                        if (eventHandlers.TryAdd(parameter.ParameterType, m.Name) == false)
                        {
                            throw new Exception($"Multiple methods found handling same event in {aggregateType.Name}");
                        }
                    }
                }

                AggregateEventHandlerCache.GetOrAdd(aggregateType, eventHandlers);
            }


            return AggregateEventHandlerCache[aggregateType].ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public static IEnumerable<MethodInfo> GetMethodsBySig(this Type type,
                                                               Type returnType,
                                                               Type customAttributeType,
                                                               bool matchParameterInheritance,
                                                               params Type[] parameterTypes)
        {
            return type.GetRuntimeMethods().Where((m) =>
            {
                if (m.ReturnType != returnType) return false;

                if ((customAttributeType != null) && (m.GetCustomAttributes(customAttributeType, true).Any() == false))
                    return false;

                var parameters = m.GetParameters();

                if ((parameterTypes == null || parameterTypes.Length == 0))
                    return parameters.Length == 0;

                if (parameters.Length != parameterTypes.Length)
                    return false;

                return parameterTypes.Select((param, index) =>
                {
                    var paramTypeMatched = parameters[index].ParameterType == param;
                    var paramTypeIsAssignable = param.GetTypeInfo()
                        .IsAssignableFrom(parameters[index].ParameterType.GetTypeInfo());

                    return paramTypeMatched || (matchParameterInheritance && paramTypeIsAssignable);
                }).All(r => r);
            });
        }

        public static string GetTypeName(Type t)
        {
            return t.Name;
        }

        public static string GetTypeFullName(Type t)
        {
            return t.AssemblyQualifiedName;
        }

        public static MethodInfo[] GetMethods(Type t)
        {
            return t.GetTypeInfo().DeclaredMethods.ToArray();
        }

        public static MethodInfo GetMethod(Type t, string methodName, Type[] paramTypes)
        {
            return t.GetRuntimeMethod(methodName, paramTypes);
        }

        public static MemberInfo[] GetMembers(Type t)
        {
            return t.GetTypeInfo().DeclaredMembers.ToArray();
        }

        // Use this method to cast types that are not IConvertible
        public static object Cast(this Type Type, object data)
        {
            var DataParam = Expression.Parameter(typeof(object), "data");
            var Body = Expression.Block(Expression.Convert(Expression.Convert(DataParam, data.GetType()), Type));

            var Run = Expression.Lambda(Body, DataParam).Compile();
            var ret = Run.DynamicInvoke(data);
            return ret;
        }
    }
}
