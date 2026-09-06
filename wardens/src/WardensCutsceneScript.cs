// WardensCutsceneScript.cs. The cutscene format: what a scene file says, and the parser.
//
// design/wardens-cutscenes.md §3. A scene is Cutscenes/<Id>.json in the mod folder: a list of shots,
// each with a camera flight (keyframes relative to a named anchor and to the pose the camera had
// when the scene began), one caption (a loc key), and optionally a pointer, a toast and an Uplink
// line. This file holds the plain data classes and Parse(), which applies the defaults and throws
// FormatException naming the field on anything malformed. No game types here, on purpose:
// tools/check_cutscenes.py implements the same rules for the static check, and the runner
// (WardensCutscenes.cs) is the only place that turns anchors into world positions.
//
// Unknown fields are ignored here (a scene written for a later version still plays what this
// version understands); the checker flags them, so a typo is found before an in-game run.

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Wardens
{
    /// What a keyframe (or a pointer) looks at. Start is the camera target when the scene began.
    public enum CutsceneAnchor { Start, Core, Selection, Bot, Grid, World }

    public sealed class CutsceneKeyframe
    {
        public float T;                                   // seconds from the shot's start
        public CutsceneAnchor Anchor = CutsceneAnchor.Start;
        public float X, Y, Z;                             // grid (z = height) or world, per Anchor
        public float OffsetX, OffsetY, OffsetZ;           // grid units added to the anchor (OffsetZ = height)
        public float? H, V, Zoom;                         // absolute pose values
        public float? DH, DV, DZoom;                      // relative to the pose at scene start
    }

    public sealed class CutscenePointer
    {
        public CutsceneAnchor Anchor = CutsceneAnchor.Core;   // core | grid | selection: the ones with grid coordinates
        public int X, Y, Z;
        public int OffsetX, OffsetY, OffsetZ;
        public string Message;
        public float? Seconds;                            // default: the shot's length
        public string Color;
    }

    public sealed class CutsceneShot
    {
        public string Id;
        public string Caption;                            // loc key
        public string Text;                               // literal caption; Caption wins when both are set
        public float Seconds;                             // the shot lasts at least this long (unscaled)
        public bool WaitForContinue;                      // wait: continue
        public readonly List<CutsceneKeyframe> Camera = new List<CutsceneKeyframe>();
        public CutscenePointer Point;
        public string Toast;
        public string Say;
    }

    public sealed class CutsceneScene
    {
        public string Id;
        public readonly List<string> Triggers = new List<string>();
        public bool Pause = true;
        public bool LeavePaused;
        public bool RestoreCamera;
        public bool Letterbox = true;
        public bool Skippable = true;
        public string Say;
        public readonly List<CutsceneShot> Shots = new List<CutsceneShot>();
        public string File;                               // file name it was read from, for the log and the tool

        /// The sum of the shots' minimum lengths, in seconds.
        public float Seconds
        {
            get
            {
                float s = 0f;
                foreach (var shot in Shots) s += shot.Seconds;
                return s;
            }
        }

        public bool HasTrigger(string trigger)
        {
            foreach (var t in Triggers)
                if (string.Equals(t, trigger, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    public static class WardensCutsceneScript
    {
        public const string TriggerNewGame = "new_game";
        public const string TriggerChapterPrefix = "chapter:";
        public const string TriggerTutorialPrefix = "tutorial:";
        public const string WaitTime = "time";
        public const string WaitContinue = "continue";

        public static CutsceneScene Parse(JObject o)
        {
            if (o == null) throw new FormatException("scene: not a JSON object");
            var scene = new CutsceneScene();
            scene.Id = Str(o, "id", "scene");
            if (string.IsNullOrEmpty(scene.Id)) throw new FormatException("scene.id: required");
            foreach (var trigger in Strings(o, "on", "scene.on"))
            {
                ValidateTrigger(trigger);
                scene.Triggers.Add(trigger);
            }
            scene.Pause = Bool(o, "pause", "scene", true);
            scene.LeavePaused = Bool(o, "leave_paused", "scene", false);
            scene.RestoreCamera = Bool(o, "restore_camera", "scene", false);
            scene.Letterbox = Bool(o, "letterbox", "scene", true);
            scene.Skippable = Bool(o, "skippable", "scene", true);
            scene.Say = Str(o, "say", "scene");
            if (!(o["shots"] is JArray shots) || shots.Count == 0)
                throw new FormatException("scene.shots: at least one shot required");
            for (int i = 0; i < shots.Count; i++)
            {
                if (!(shots[i] is JObject so)) throw new FormatException($"shots[{i}]: not an object");
                scene.Shots.Add(ParseShot(so, i));
            }
            return scene;
        }

        /// new_game | chapter:<Id> | tutorial:<Id>. Whether the id exists is the checker's job.
        public static void ValidateTrigger(string trigger)
        {
            if (trigger == TriggerNewGame) return;
            if (trigger.StartsWith(TriggerChapterPrefix, StringComparison.Ordinal) && trigger.Length > TriggerChapterPrefix.Length) return;
            if (trigger.StartsWith(TriggerTutorialPrefix, StringComparison.Ordinal) && trigger.Length > TriggerTutorialPrefix.Length) return;
            throw new FormatException($"scene.on: '{trigger}' (new_game | chapter:<Id> | tutorial:<Id>)");
        }

        private static CutsceneShot ParseShot(JObject o, int index)
        {
            string where = $"shots[{index}]";
            var shot = new CutsceneShot();
            shot.Id = Str(o, "id", where) ?? index.ToString(CultureInfo.InvariantCulture);
            shot.Caption = Str(o, "caption", where);
            shot.Text = Str(o, "text", where);
            var wait = Str(o, "wait", where) ?? WaitTime;
            if (wait == WaitContinue) shot.WaitForContinue = true;
            else if (wait != WaitTime) throw new FormatException($"{where}.wait: '{wait}' (time | continue)");

            var camera = o["camera"];
            if (camera != null && camera.Type != JTokenType.Null)
            {
                if (!(camera is JArray keys)) throw new FormatException($"{where}.camera: not an array");
                for (int i = 0; i < keys.Count; i++)
                {
                    if (!(keys[i] is JObject ko)) throw new FormatException($"{where}.camera[{i}]: not an object");
                    shot.Camera.Add(ParseKeyframe(ko, $"{where}.camera[{i}]"));
                }
                shot.Camera.Sort((a, b) => a.T.CompareTo(b.T));
            }
            float last = 0f;
            foreach (var k in shot.Camera) if (k.T > last) last = k.T;
            shot.Seconds = Num(o, "seconds", where) ?? last;
            if (shot.Seconds < 0f) throw new FormatException($"{where}.seconds: negative");

            var point = o["point"];
            if (point != null && point.Type != JTokenType.Null)
            {
                if (!(point is JObject po)) throw new FormatException($"{where}.point: not an object");
                shot.Point = ParsePointer(po, where + ".point");
            }
            shot.Toast = Str(o, "toast", where);
            shot.Say = Str(o, "say", where);
            return shot;
        }

        private static CutsceneKeyframe ParseKeyframe(JObject o, string where)
        {
            var k = new CutsceneKeyframe();
            k.T = Num(o, "t", where) ?? 0f;
            if (k.T < 0f) throw new FormatException($"{where}.t: negative");
            k.Anchor = ParseAnchor(Str(o, "anchor", where), where);
            var x = Num(o, "x", where);
            var y = Num(o, "y", where);
            var z = Num(o, "z", where);
            if ((k.Anchor == CutsceneAnchor.Grid || k.Anchor == CutsceneAnchor.World) && (x == null || y == null))
                throw new FormatException($"{where}: anchor {AnchorName(k.Anchor)} needs x and y");
            k.X = x ?? 0f;
            k.Y = y ?? 0f;
            k.Z = z ?? 0f;
            Offset(o, where, out k.OffsetX, out k.OffsetY, out k.OffsetZ);
            k.H = Num(o, "h", where);
            k.V = Num(o, "v", where);
            k.Zoom = Num(o, "zoom", where);
            k.DH = Num(o, "dh", where);
            k.DV = Num(o, "dv", where);
            k.DZoom = Num(o, "dzoom", where);
            return k;
        }

        private static CutscenePointer ParsePointer(JObject o, string where)
        {
            var p = new CutscenePointer();
            p.Anchor = ParseAnchor(Str(o, "anchor", where) ?? "core", where);
            if (p.Anchor != CutsceneAnchor.Core && p.Anchor != CutsceneAnchor.Grid && p.Anchor != CutsceneAnchor.Selection)
                throw new FormatException($"{where}.anchor: '{AnchorName(p.Anchor)}' (core | grid | selection: the anchors with grid coordinates)");
            var x = Num(o, "x", where);
            var y = Num(o, "y", where);
            var z = Num(o, "z", where);
            if (p.Anchor == CutsceneAnchor.Grid && (x == null || y == null))
                throw new FormatException($"{where}: anchor grid needs x and y");
            p.X = (int)Math.Round(x ?? 0f);
            p.Y = (int)Math.Round(y ?? 0f);
            p.Z = (int)Math.Round(z ?? 0f);
            Offset(o, where, out float ox, out float oy, out float oz);
            p.OffsetX = (int)Math.Round(ox);
            p.OffsetY = (int)Math.Round(oy);
            p.OffsetZ = (int)Math.Round(oz);
            p.Message = Str(o, "message", where);
            p.Seconds = Num(o, "seconds", where);
            p.Color = Str(o, "color", where);
            return p;
        }

        public static string AnchorName(CutsceneAnchor anchor) => anchor.ToString().ToLowerInvariant();

        private static CutsceneAnchor ParseAnchor(string s, string where)
        {
            switch ((s ?? "start").Trim().ToLowerInvariant())
            {
                case "start": return CutsceneAnchor.Start;
                case "core": return CutsceneAnchor.Core;
                case "selection": return CutsceneAnchor.Selection;
                case "bot": return CutsceneAnchor.Bot;
                case "grid": return CutsceneAnchor.Grid;
                case "world": return CutsceneAnchor.World;
                default: throw new FormatException($"{where}.anchor: '{s}' (start | core | selection | bot | grid | world)");
            }
        }

        // ---- JSON helpers: absent or null means "not given"; a wrong type is an error ----------

        private static string Str(JObject o, string key, string where)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type != JTokenType.String) throw new FormatException($"{where}.{key}: not a string");
            return (string)t;
        }

        private static bool Bool(JObject o, string key, string where, bool fallback)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Boolean) throw new FormatException($"{where}.{key}: not a boolean");
            return (bool)t;
        }

        private static float? Num(JObject o, string key, string where)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new FormatException($"{where}.{key}: not a number");
            return (float)t;
        }

        private static float NumAt(JArray arr, int i, string where)
        {
            var t = arr[i];
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new FormatException($"{where}[{i}]: not a number");
            return (float)t;
        }

        private static List<string> Strings(JObject o, string key, string where)
        {
            var list = new List<string>();
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null) return list;
            if (!(t is JArray arr)) throw new FormatException($"{where}: not an array");
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i].Type != JTokenType.String) throw new FormatException($"{where}[{i}]: not a string");
                list.Add((string)arr[i]);
            }
            return list;
        }

        // "offset": [dx, dy] or [dx, dy, dz], grid units.
        private static void Offset(JObject o, string where, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            var t = o["offset"];
            if (t == null || t.Type == JTokenType.Null) return;
            if (!(t is JArray arr) || arr.Count < 2 || arr.Count > 3)
                throw new FormatException($"{where}.offset: [dx, dy] or [dx, dy, dz]");
            x = NumAt(arr, 0, where + ".offset");
            y = NumAt(arr, 1, where + ".offset");
            if (arr.Count == 3) z = NumAt(arr, 2, where + ".offset");
        }
    }
}
