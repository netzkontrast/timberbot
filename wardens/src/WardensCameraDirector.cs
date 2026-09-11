// WardensCameraDirector.cs. Keyframe camera flights over the vanilla CameraService.
//
// One interpolator serves three callers: the Cold Boot cutscene (WardensColdBoot), the
// agent's camera_fly MCP tool, and later the trailer / "Kamerafahrt" planner. A keyframe is
// {Target (world space), H, V (degrees), Zoom (CameraService.ZoomLevel units), Time (s)}.
// Time is unscaled so flights run while the game is paused. Between keyframes we ease with
// smoothstep; the current camera pose is inserted as frame 0 when the first frame is not at t=0.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.CameraSystem;
using Timberborn.Coordinates;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensCameraDirector : IUpdatableSingleton
    {
        public struct Keyframe
        {
            public Vector3 Target;
            public float H;
            public float V;
            public float Zoom;
            public float Time;
        }

        private readonly CameraService _camera;
        private List<Keyframe> _frames = new List<Keyframe>();
        private float _start;
        private bool _flying;
        private Action _onFinished;

        public bool IsFlying => _flying;

        public WardensCameraDirector(CameraService camera)
        {
            _camera = camera;
        }

        public Keyframe Current(float time = 0f) => new Keyframe
        {
            Target = _camera.Target,
            H = _camera.HorizontalAngle,
            V = _camera.VerticalAngle,
            Zoom = _camera.ZoomLevel,
            Time = time,
        };

        public void Fly(List<Keyframe> frames, Action onFinished = null)
        {
            if (frames == null || frames.Count == 0) return;
            frames.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (frames[0].Time > 0f) frames.Insert(0, Current(0f));
            _frames = frames;
            _start = Time.unscaledTime;
            _flying = true;
            _onFinished = onFinished;
            Apply(_frames[0]);
        }

        public void Stop()
        {
            _flying = false;
            _onFinished = null;
        }

        public void Apply(Keyframe k)
        {
            _camera.MoveTargetTo(k.Target);
            _camera.HorizontalAngle = k.H;
            _camera.VerticalAngle = k.V;
            _camera.ZoomLevel = k.Zoom;
        }

        public void UpdateSingleton()
        {
            if (!_flying) return;
            float t = Time.unscaledTime - _start;
            var last = _frames[_frames.Count - 1];
            if (t >= last.Time || _frames.Count == 1)
            {
                Apply(last);
                _flying = false;
                var cb = _onFinished;
                _onFinished = null;
                cb?.Invoke();
                return;
            }
            int i = 0;
            while (i < _frames.Count - 2 && _frames[i + 1].Time <= t) i++;
            var a = _frames[i];
            var b = _frames[i + 1];
            float span = Mathf.Max(0.0001f, b.Time - a.Time);
            float u = Mathf.Clamp01((t - a.Time) / span);
            u = u * u * (3f - 2f * u);
            Apply(new Keyframe
            {
                Target = Vector3.Lerp(a.Target, b.Target, u),
                H = Mathf.Lerp(a.H, b.H, u),
                V = Mathf.Lerp(a.V, b.V, u),
                Zoom = Mathf.Lerp(a.Zoom, b.Zoom, u),
                Time = t,
            });
        }

        public JObject State() => new JObject
        {
            ["target"] = Vec(_camera.Target),
            ["h"] = _camera.HorizontalAngle,
            ["v"] = _camera.VerticalAngle,
            ["zoom"] = _camera.ZoomLevel,
            ["freeMode"] = _camera.FreeMode,
            ["flying"] = _flying,
            ["units"] = "target is world space (x, height, z); h/v in degrees; zoom is CameraService.ZoomLevel",
        };

        public static JObject Vec(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };

        // A world position and the grid tile it stands on: `camera` takes the first (world: true),
        // `point` and every Timberbot endpoint the second (y there is north, z the height).
        public static JObject At(Vector3 world)
        {
            var o = Vec(world);
            var g = CoordinateSystem.WorldToGridInt(world);
            o["grid"] = new JObject { ["x"] = g.x, ["y"] = g.y, ["z"] = g.z };
            return o;
        }

        // Reads {x,y,z} as WORLD coordinates when "world": true, otherwise as grid coordinates
        // (x, y, z=height) like every Timberbot endpoint. Missing fields keep the fallback.
        public static Keyframe FromJson(JObject o, Keyframe fallback)
        {
            var k = fallback;
            if (o == null) return k;
            if (o["x"] != null && o["y"] != null)
            {
                float x = (float)o["x"], y = (float)o["y"], z = o["z"] != null ? (float)o["z"] : 0f;
                if (o["world"] != null && (bool)o["world"]) k.Target = new Vector3(x, y, z);
                else k.Target = CoordinateSystem.GridToWorldCentered(
                    new Vector3Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y), Mathf.RoundToInt(z)));
            }
            if (o["h"] != null) k.H = (float)o["h"];
            if (o["v"] != null) k.V = (float)o["v"];
            if (o["zoom"] != null) k.Zoom = (float)o["zoom"];
            if (o["t"] != null) k.Time = (float)o["t"];
            return k;
        }
    }
}
