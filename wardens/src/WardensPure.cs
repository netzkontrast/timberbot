// WardensPure.cs. Helpers with no Unity or game dependency, so they compile in wardens/test.
//
// Keep this file free of UnityEngine and Timberborn.* usings: wardens/test/Wardens.Tests.csproj
// compiles it on net10.0 without the game's assemblies, the way timberbot/test does with
// TimberbotPure.cs. Anything that needs the game stays in the other Wardens*.cs files.

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Wardens
{
    public static class WardensPure
    {
        /// The URL the `timberbot` MCP tool calls for a GET. `path` starts with /api/ and may carry
        /// its own query ("/api/tiles?x1=1&y1=2"); entries in `query` override same-named ones from
        /// the path and are appended in order; `format=json` is added only when no `format` is
        /// present. Before this, a query inside `path` got "?format=json" appended after it, so the
        /// last parameter's value arrived as "55?format=json" and parsed as 0 (the dropped tiles
        /// bound, the ignored `offset` and the empty `name` filter of the 2026-09-10 playtest).
        public static string BuildLoopbackUrl(int port, string path, JObject query)
        {
            if (path == null || !path.StartsWith("/api/", StringComparison.Ordinal))
                throw new ArgumentException("path must start with /api/");
            int q = path.IndexOf('?');
            string route = q < 0 ? path : path.Substring(0, q);
            var pairs = new List<KeyValuePair<string, string>>();
            if (q >= 0)
            {
                foreach (var raw in path.Substring(q + 1).Split('&'))
                {
                    if (raw.Length == 0) continue;
                    int eq = raw.IndexOf('=');
                    string key = Uri.UnescapeDataString(eq < 0 ? raw : raw.Substring(0, eq));
                    string value = eq < 0 ? "" : Uri.UnescapeDataString(raw.Substring(eq + 1));
                    pairs.Add(new KeyValuePair<string, string>(key, value));
                }
            }
            if (query != null)
            {
                foreach (var kv in query)
                {
                    pairs.RemoveAll(p => p.Key == kv.Key);
                    pairs.Add(new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? ""));
                }
            }
            if (!pairs.Exists(p => p.Key == "format"))
                pairs.Add(new KeyValuePair<string, string>("format", "json"));
            var sb = new StringBuilder("http://127.0.0.1:").Append(port).Append(route);
            for (int i = 0; i < pairs.Count; i++)
                sb.Append(i == 0 ? '?' : '&')
                  .Append(Uri.EscapeDataString(pairs[i].Key)).Append('=').Append(Uri.EscapeDataString(pairs[i].Value));
            return sb.ToString();
        }
    }
}
