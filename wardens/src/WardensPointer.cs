// WardensPointer.cs. "Look here": the agent points at a tile for the player.
//
// The game already has everything a pointer needs, it just never exposes it to mods as one
// thing: Highlighter (SelectionSystem) tints an entity's model, MarkerDrawerFactory (Rendering)
// hands out immediate-mode mesh drawers for tiles and arrows, CameraService can pan. A pointer =
// highlight the object on the tile + a bobbing arrow above it + an optional line in the Wardens'
// window (not a toast: only events toast) + optional camera pan, for N seconds (unscaled, so paused games
// still show it); a cutscene highlight is the same thing without the arrow (arrow: false).
// Pointers are re-drawn every frame from UpdateSingleton (Graphics.DrawMesh
// is per-frame), which is why this is an IUpdatableSingleton and not a one-shot call.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.CameraSystem;
using Timberborn.Coordinates;
using Timberborn.EntitySystem;
using Timberborn.Rendering;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensPointer : ILoadableSingleton, IUpdatableSingleton
    {
        private class Pointer
        {
            public Vector3Int Coords;
            public Color Color;
            public float ExpiresAt;
            public BaseComponent Target;
            public string Label;
            public bool Arrow = true;   // false: the tint and the tile only (a cutscene highlight)
        }

        private readonly BlockService _blockService;
        private readonly Highlighter _highlighter;
        private readonly MarkerDrawerFactory _markerDrawerFactory;
        private readonly CameraService _cameraService;
        private readonly WardensChat _chat;
        private readonly List<Pointer> _pointers = new List<Pointer>();
        private MeshDrawer _tileDrawer;
        private MeshDrawer _arrowDrawer;

        public int Count => _pointers.Count;

        public WardensPointer(BlockService blockService, Highlighter highlighter,
            MarkerDrawerFactory markerDrawerFactory, CameraService cameraService,
            WardensChat chat)
        {
            _blockService = blockService;
            _highlighter = highlighter;
            _markerDrawerFactory = markerDrawerFactory;
            _cameraService = cameraService;
            _chat = chat;
        }

        public void Load()
        {
            _tileDrawer = _markerDrawerFactory.CreateTileDrawer();
            _arrowDrawer = _markerDrawerFactory.CreateArrowMarkerDrawer();
        }

        public JObject Point(Vector3Int coords, string message, float seconds, string colorName, bool focus, bool arrow = true)
        {
            var color = ParseColor(colorName);
            BaseComponent target = null;
            string template = null;
            string entityId = null;
            if (_blockService.Contains(coords))
            {
                foreach (var blockObject in _blockService.GetObjectsAt(coords))
                {
                    target = blockObject;   // last one wins: top of the stack at that tile
                    template = blockObject.GetComponent<TemplateSpec>()?.TemplateName;
                    entityId = blockObject.GetComponent<EntityComponent>()?.EntityId.ToString();
                }
            }
            if (target != null) _highlighter.HighlightPrimary(target, color);

            _pointers.Add(new Pointer
            {
                Coords = coords,
                Color = color,
                ExpiresAt = Time.unscaledTime + Mathf.Max(1f, seconds),
                Target = target,
                Label = message,
                Arrow = arrow,
            });

            if (focus) _cameraService.MoveTargetTo(CoordinateSystem.GridToWorldCentered(coords));
            // A pointer's message goes to the Wardens' window, not a toast: only events toast (2026-09-11).
            if (!string.IsNullOrEmpty(message)) _chat.SystemSays(message);

            return new JObject
            {
                ["x"] = coords.x,
                ["y"] = coords.y,
                ["z"] = coords.z,
                ["found"] = target != null,
                ["template"] = template,
                ["entityId"] = entityId,
                ["seconds"] = seconds,
                ["active"] = _pointers.Count,
            };
        }

        public int Clear()
        {
            int n = _pointers.Count;
            foreach (var p in _pointers) Unhighlight(p);
            _pointers.Clear();
            return n;
        }

        public void UpdateSingleton()
        {
            if (_pointers.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _pointers.Count - 1; i >= 0; i--)
            {
                var p = _pointers[i];
                if (now >= p.ExpiresAt)
                {
                    Unhighlight(p);
                    _pointers.RemoveAt(i);
                    continue;
                }
                float bob = 0.25f * Mathf.Sin(now * 4f);
                var basePos = CoordinateSystem.GridToWorldCentered(p.Coords);
                _tileDrawer.DrawAtCoordinates(p.Coords, 0.03f, p.Color);
                if (p.Arrow)
                    _arrowDrawer.DrawAtPosition(basePos + new Vector3(0f, 1.6f + bob, 0f),
                        Quaternion.Euler(90f, now * 60f, 0f), p.Color);
            }
        }

        private void Unhighlight(Pointer p)
        {
            if (p.Target == null) return;
            try { _highlighter.UnhighlightPrimary(p.Target); } catch { /* target may be gone */ }
        }

        public static Color ParseColor(string name)
        {
            var cyan = new Color(0f, 0.9f, 1f, 1f);   // the Wardens' data-light accent
            if (string.IsNullOrEmpty(name)) return cyan;
            return ColorUtility.TryParseHtmlString(name, out var c) ? c : cyan;
        }
    }
}
