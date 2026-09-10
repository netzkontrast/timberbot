// TimberbotErrors.cs. Structured error contract (docs/spec/error-contract.md).
//
// Unity-free. Maps the legacy `{"error":"<prefix>: <reason>. <hint>", ...}`
// payloads produced by TimberbotJw.Error / PlaceBuildingResult into
// `{ok:false, code, reason, hint, at:{x,y,z}, details}` so a model gets a
// stable code, a reason and a next step instead of one free-text string.
// The REST surface keeps the legacy shape; the MCP surface uses this.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Timberbot
{
    public sealed class TimberbotErrorInfo
    {
        public string Code;
        public string Reason;
        public string Hint;
        public int? X, Y, Z;
        public JObject Details = new JObject();
        public string LegacyMessage;

        public JObject ToJson()
        {
            var o = new JObject { ["ok"] = false, ["code"] = Code, ["reason"] = Reason };
            if (!string.IsNullOrEmpty(Hint)) o["hint"] = Hint;
            if (X.HasValue && Y.HasValue)
            {
                var at = new JObject { ["x"] = X.Value, ["y"] = Y.Value };
                if (Z.HasValue) at["z"] = Z.Value;
                o["at"] = at;
            }
            if (Details != null && Details.Count > 0) o["details"] = Details;
            return o;
        }
    }

    public static class TimberbotErrors
    {
        // Ordered: snake_case prefixes first (matched as "<prefix>:" or "<prefix>"),
        // then free-text placement patterns (matched as "starts with").
        private static readonly (string prefix, string code)[] PrefixMap =
        {
            ("not_found", "NOT_FOUND"),
            ("invalid_param", "INVALID_PARAM"),
            ("invalid_mode", "INVALID_PARAM"),
            ("invalid_prompt", "INVALID_PARAM"),
            ("invalid_message", "INVALID_PARAM"),
            ("invalid_ready", "INVALID_PARAM"),
            ("invalid_type", "INVALID_TYPE"),
            ("invalid_prefab", "INVALID_PREFAB"),
            ("not_unlocked", "NOT_UNLOCKED"),
            ("insufficient_science", "INSUFFICIENT_SCIENCE"),
            ("no_population", "NO_POPULATION"),
            ("operation_failed", "OPERATION_FAILED"),
            ("refresh_timeout", "REFRESH_TIMEOUT"),
            ("unknown_endpoint", "UNKNOWN_ENDPOINT"),
            ("internal_error", "INTERNAL_ERROR"),
            ("game_not_ready", "GAME_NOT_READY"),
            ("unauthorized", "UNAUTHORIZED"),
            ("invalid_body", "INVALID_BODY"),
            ("body_too_large", "BODY_TOO_LARGE"),
            ("disabled", "DISABLED"),
        };

        private static readonly (string pattern, string code)[] PatternMap =
        {
            ("occupied by", "PLACEMENT_OCCUPIED"),
            ("terrain conflict", "PLACEMENT_TERRAIN"),
            ("blocked above", "PLACEMENT_BLOCKED_ABOVE"),
            ("blocked below", "PLACEMENT_BLOCKED_BELOW"),
            ("out of map", "PLACEMENT_OUT_OF_MAP"),
            ("underground conflict", "PLACEMENT_UNDERGROUND"),
            ("not underground", "PLACEMENT_NOT_UNDERGROUND"),
            ("placement invalid", "PLACEMENT_INVALID"),
            ("no placeable spec", "PLACEMENT_INVALID"),
        };

        // True when `payload` is a legacy error object (has a string "error" key).
        public static bool TryParse(JToken payload, out TimberbotErrorInfo info)
        {
            info = null;
            var obj = payload as JObject;
            if (obj == null) return false;
            var err = obj["error"];
            if (err == null || err.Type != JTokenType.String) return false;
            info = FromLegacy(obj);
            return true;
        }

        public static bool TryParse(string json, out TimberbotErrorInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(json)) return false;
            JToken tok;
            try { tok = JToken.Parse(json); } catch { return false; }
            return TryParse(tok, out info);
        }

        public static TimberbotErrorInfo FromLegacy(JObject obj)
        {
            var message = obj.Value<string>("error") ?? "";
            var info = new TimberbotErrorInfo { LegacyMessage = message };
            string rest;
            info.Code = CodeFor(message, out rest);
            SplitReasonHint(rest, out info.Reason, out info.Hint);
            // 409 gate payload carries its hint as a separate key.
            var hintKey = obj["hint"];
            if (hintKey != null && hintKey.Type == JTokenType.String && string.IsNullOrEmpty(info.Hint))
                info.Hint = hintKey.Value<string>();

            foreach (var prop in obj.Properties())
            {
                switch (prop.Name)
                {
                    case "error":
                    case "hint":
                        break;
                    case "x": info.X = AsInt(prop.Value); break;
                    case "y": info.Y = AsInt(prop.Value); break;
                    case "z": info.Z = AsInt(prop.Value); break;
                    default: info.Details[prop.Name] = prop.Value.DeepClone(); break;
                }
            }
            // Only surface `at` when both x and y were present; keep partial coords in details.
            if (!(info.X.HasValue && info.Y.HasValue))
            {
                if (info.X.HasValue) info.Details["x"] = info.X.Value;
                if (info.Y.HasValue) info.Details["y"] = info.Y.Value;
                if (info.Z.HasValue) info.Details["z"] = info.Z.Value;
                info.X = info.Y = info.Z = null;
            }
            return info;
        }

        // Maps a legacy message to a stable code. `rest` is the message with a
        // recognised "<prefix>: " stripped (or the full message otherwise).
        public static string CodeFor(string message, out string rest)
        {
            rest = message ?? "";
            var msg = rest.TrimStart();
            foreach (var (prefix, code) in PrefixMap)
            {
                if (msg.StartsWith(prefix + ":", StringComparison.Ordinal))
                {
                    rest = msg.Substring(prefix.Length + 1).TrimStart();
                    return code;
                }
                if (string.Equals(msg, prefix, StringComparison.Ordinal))
                {
                    rest = "";
                    return code;
                }
            }
            foreach (var (pattern, code) in PatternMap)
            {
                if (msg.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return code;
            }
            return "UNKNOWN_ERROR";
        }

        // "why. what to try" -> ("why", "what to try"). No ". " -> hint null.
        public static void SplitReasonHint(string text, out string reason, out string hint)
        {
            reason = (text ?? "").Trim();
            hint = null;
            int idx = reason.IndexOf(". ", StringComparison.Ordinal);
            if (idx > 0 && idx < reason.Length - 2)
            {
                hint = reason.Substring(idx + 2).Trim();
                reason = reason.Substring(0, idx).Trim();
            }
            if (string.IsNullOrEmpty(reason) && hint != null) { reason = hint; hint = null; }
        }

        public static TimberbotErrorInfo Make(string code, string reason, string hint = null)
            => new TimberbotErrorInfo { Code = code, Reason = reason, Hint = hint, LegacyMessage = reason };

        private static int? AsInt(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            if (t.Type == JTokenType.Float) return (int)Math.Round(t.Value<double>());
            if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out var v)) return v;
            return null;
        }
    }
}
