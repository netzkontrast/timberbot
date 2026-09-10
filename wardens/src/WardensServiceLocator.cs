// WardensServiceLocator.cs. Getting hold of a game service we were not handed.
//
// The level transition needs objects the mod does not inject: GameSceneLoader, MapItemProvider,
// ValidatingGameLoader in the Game scene. The obvious move — add them to a constructor — is the one
// move that must not be made here: Bindito resolves a configurator's bindings at scene load, so a
// dependency that turns out not to be bound in that context takes down the whole configurator, and
// with it the MCP server, the chat, the chapters and everything else that does work. A campaign
// convenience is not worth that trade, and none of those bindings is verified
// (design/wardens-campaign-maps.md §5).
//
// So: a registry the mod fills with services it already receives legitimately, plus Unity's own
// object lookup for services that are Unity objects. Anything else resolves to null, and the caller
// reports it by name instead of crashing.
//
// This is deliberately not a general service locator, and nothing outside the transition should use
// it. When the decompile confirms which of these are bindable and where, the right change is to
// inject them properly and delete the corresponding lookups here.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wardens
{
    public static class WardensServiceLocator
    {
        private static readonly List<object> Registered = new List<object>();

        /// Hand over a service this mod was injected with, so the transition can reach it.
        public static void Register(object service)
        {
            if (service == null) return;
            if (!Registered.Contains(service)) Registered.Add(service);
        }

        /// Scene change: the Game context's services must not leak into the main menu's lookups.
        public static void Clear() => Registered.Clear();

        public static object Resolve(Type type)
        {
            if (type == null) return null;

            foreach (var service in Registered)
                if (service != null && type.IsInstanceOfType(service)) return service;

            // Unity objects can be found without the container. Plain C# services cannot, which is
            // why Resolve returning null is an expected outcome and not an error.
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                try
                {
                    var found = Resources.FindObjectsOfTypeAll(type);
                    if (found != null && found.Length > 0) return found[0];
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Wardens] service lookup for " + type.Name + " failed: " + ex.Message);
                }
            }
            return null;
        }

        public static string Describe()
        {
            if (Registered.Count == 0) return "none registered";
            var names = new List<string>();
            foreach (var service in Registered) names.Add(service.GetType().Name);
            return string.Join(", ", names.ToArray());
        }
    }
}
