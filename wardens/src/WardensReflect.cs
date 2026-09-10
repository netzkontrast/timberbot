// WardensReflect.cs. Talking to game APIs we have not been able to verify.
//
// The campaign's level transition needs classes nobody here has compiled against:
// NewGameConfiguration, GameSceneLoader, MapItemProvider, MapFileReference. design/wardens-campaign-maps.md
// §5 lists them as "what the first decompile session must confirm", and the mod is written on
// machines with no Timberborn install.
//
// A direct reference to a type whose shape is a guess is a build error at best and a load-time
// crash at worst; an injected dependency that Bindito cannot resolve takes the whole configurator
// down, and with it the parts of the mod that do work. So the unverified surface is reached by
// reflection, and every lookup returns null instead of throwing.
//
// The point is not only that it degrades. It is that it *reports*: WardensLevelTransition logs
// exactly which type, method or property was missing, so the first run on a machine with the game
// turns the unverified list into a finding with a name in it. That is worth more than a guess that
// happens to compile.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Wardens
{
    public static class WardensReflect
    {
        /// A type by full or simple name, across every assembly the game has loaded. Null if absent.
        public static Type FindType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var direct = Type.GetType(name, throwOnError: false);
            if (direct != null) return direct;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = null;
                try
                {
                    found = assembly.GetType(name, throwOnError: false);
                    if (found == null)
                        found = assembly.GetTypes().FirstOrDefault(t => t.Name == name || t.FullName == name);
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;       // a half-loaded assembly is not worth failing over
                }
                catch (Exception)
                {
                    continue;
                }
                if (found != null) return found;
            }
            return null;
        }

        /// Call a method by name. `missing` names what was not found, for the log.
        public static object Call(object target, string method, out string missing, params object[] args)
        {
            missing = null;
            if (target == null) { missing = "instance is null"; return null; }
            return CallOn(target.GetType(), target, method, out missing, args);
        }

        public static object CallStatic(Type type, string method, out string missing, params object[] args)
            => CallOn(type, null, method, out missing, args);

        private static object CallOn(Type type, object target, string method, out string missing, object[] args)
        {
            missing = null;
            if (type == null) { missing = "type is null"; return null; }
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var candidates = type.GetMethods(Flags).Where(m => m.Name == method).ToArray();
            if (candidates.Length == 0)
            {
                missing = type.Name + "." + method + " (no such method)";
                return null;
            }
            var argc = args?.Length ?? 0;
            var chosen = candidates.FirstOrDefault(m => m.GetParameters().Length == argc);
            if (chosen == null)
            {
                missing = type.Name + "." + method + " takes " +
                          string.Join(" or ", candidates.Select(m => m.GetParameters().Length.ToString()).Distinct().ToArray()) +
                          " arguments, not " + argc;
                return null;
            }
            try
            {
                return chosen.Invoke(target, args);
            }
            catch (Exception ex)
            {
                missing = type.Name + "." + method + " threw: " + (ex.InnerException ?? ex).Message;
                return null;
            }
        }

        /// A property or field by name, on the instance or its type.
        public static object Get(object target, string member, out string missing)
        {
            missing = null;
            if (target == null) { missing = "instance is null"; return null; }
            var type = target.GetType();
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var property = type.GetProperty(member, Flags);
            if (property != null)
            {
                try { return property.GetValue(target); }
                catch (Exception ex) { missing = type.Name + "." + member + " threw: " + ex.Message; return null; }
            }
            var field = type.GetField(member, Flags);
            if (field != null)
            {
                try { return field.GetValue(target); }
                catch (Exception ex) { missing = type.Name + "." + member + " threw: " + ex.Message; return null; }
            }
            missing = type.Name + "." + member + " (no such property or field)";
            return null;
        }

        /// Every element of something enumerable, or an empty array. Map lists come back this way.
        public static object[] Enumerate(object value)
        {
            if (value is IEnumerable sequence && !(value is string))
            {
                var items = new System.Collections.Generic.List<object>();
                foreach (var item in sequence) items.Add(item);
                return items.ToArray();
            }
            return value == null ? new object[0] : new[] { value };
        }

        /// The first constructor with this many parameters, for a type we only know by name.
        public static object Construct(Type type, out string missing, params object[] args)
        {
            missing = null;
            if (type == null) { missing = "type is null"; return null; }
            var argc = args?.Length ?? 0;
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           .FirstOrDefault(c => c.GetParameters().Length == argc);
            if (ctor == null)
            {
                var shapes = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 .Select(c => c.GetParameters().Length.ToString()).Distinct().ToArray();
                missing = type.Name + " has no constructor taking " + argc + " arguments" +
                          (shapes.Length > 0 ? " (it takes " + string.Join(" or ", shapes) + ")" : "");
                return null;
            }
            try
            {
                return ctor.Invoke(args);
            }
            catch (Exception ex)
            {
                missing = type.Name + " constructor threw: " + (ex.InnerException ?? ex).Message;
                return null;
            }
        }

        public static void Warn(string what, string missing)
        {
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning("[Wardens] " + what + ": " + missing);
        }
    }
}
