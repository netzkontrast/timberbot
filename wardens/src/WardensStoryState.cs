// WardensStoryState.cs. The story record: the choices the player made and the marks the scenes set.
//
// design/wardens-cutscenes.md §3.5. A choice card records its pick under a key (default
// <Scene>.<shot>); a shot with `mark` records the day under a name; captions read both back through
// `choice:<key>` and `mark:<name>` args, and a shot's `when` condition plays it only for one answer.
// The record lives in story.json next to settings.json in the mod folder, outside any save, the way
// the campaign design keeps campaign.json (a save is a level; the story spans saves) and the way
// Timberbot keeps state.json. One file per mod folder; `reset` (the MCP tool) archives it. Read
// lazily on first use, written whole on every change (temp file, then copy over), never fatal:
// a missing or broken file is an empty record and one log line.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Wardens
{
    public class WardensStoryState
    {
        public const string FileName = "story.json";
        public const int Version = 1;

        private readonly Dictionary<string, string> _choices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _marks = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _loaded;

        public static string Path =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", FileName);

        /// The recorded pick under `key`, or null.
        public string Choice(string key)
        {
            EnsureLoaded();
            return key != null && _choices.TryGetValue(key, out var id) ? id : null;
        }

        public void SetChoice(string key, string id)
        {
            EnsureLoaded();
            _choices[key] = id;
            Save();
        }

        /// The recorded day under `name`, or null.
        public int? Mark(string name)
        {
            EnsureLoaded();
            return name != null && _marks.TryGetValue(name, out var day) ? (int?)day : null;
        }

        public void SetMark(string name, int day)
        {
            EnsureLoaded();
            _marks[name] = day;
            Save();
        }

        /// Archives the file as story.<timestamp>.json and starts over (dev/testing).
        public string Reset()
        {
            EnsureLoaded();
            _choices.Clear();
            _marks.Clear();
            string archived = null;
            try
            {
                if (System.IO.File.Exists(Path))
                {
                    archived = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? "",
                        $"story.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                    System.IO.File.Copy(Path, archived, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] story.json archive: " + ex.Message);
            }
            Save();
            return archived;
        }

        public JObject ToJson()
        {
            EnsureLoaded();
            var choices = new JObject();
            foreach (var kv in _choices) choices[kv.Key] = kv.Value;
            var marks = new JObject();
            foreach (var kv in _marks) marks[kv.Key] = kv.Value;
            return new JObject { ["version"] = Version, ["choices"] = choices, ["marks"] = marks };
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!System.IO.File.Exists(Path)) return;
                var json = JObject.Parse(System.IO.File.ReadAllText(Path));
                if (json["choices"] is JObject choices)
                    foreach (var kv in choices)
                        if (kv.Value != null && kv.Value.Type == JTokenType.String) _choices[kv.Key] = (string)kv.Value;
                if (json["marks"] is JObject marks)
                    foreach (var kv in marks)
                        if (kv.Value != null && kv.Value.Type == JTokenType.Integer) _marks[kv.Key] = (int)kv.Value;
                Debug.Log($"[Wardens] story.json: {_choices.Count} choices, {_marks.Count} marks");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] story.json: " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                var text = ToJson().ToString(Formatting.Indented);
                var tmp = Path + ".tmp";
                System.IO.File.WriteAllText(tmp, text);
                System.IO.File.Copy(tmp, Path, true);
                System.IO.File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] story.json write: " + ex.Message);
            }
        }
    }
}
