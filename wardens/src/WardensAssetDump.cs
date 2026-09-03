// WardensAssetDump.cs. Let the game do the asset extraction.
//
// Mod asset bundles built with Unity 6000.5 cannot be opened by the offline tools we tried, but
// the running game has already loaded every blueprint, texture and material from every enabled
// mod. This singleton writes them out on request (MCP tool `dump_assets`):
//   <Mods/Wardens/dump>/blueprints/<path>.json   every blueprint the SpecService knows, as the
//                                                 game merged it (vanilla + all mods + modifiers)
//   <dump>/materials.json                          every Material: shader, colors, texture names
//   <dump>/textures/<name>.png                     Texture2D whose name matches the filter, via a
//                                                 RenderTexture blit (GPU-only textures included)
// Enable the Leaf Coats mods for one game session, run the tool, disable them again: the port
// plan then has the real building blueprints and atlas textures to work from.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Timberborn.BlueprintSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensAssetDump
    {
        private readonly BlueprintFileBundleLoader _bundleLoader;

        public WardensAssetDump(BlueprintFileBundleLoader bundleLoader)
        {
            _bundleLoader = bundleLoader;
        }

        public static string DumpRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Timberborn", "Mods", "Wardens", "dump");

        public JObject DumpBlueprints(string pathFilter)
        {
            var root = Path.Combine(DumpRoot, "blueprints");
            Directory.CreateDirectory(root);
            int written = 0, skipped = 0;
            foreach (var bundle in _bundleLoader.GetBundles(""))
            {
                var path = bundle.Path ?? "";
                if (!string.IsNullOrEmpty(pathFilter) && path.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) < 0) { skipped++; continue; }
                var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar) + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var sb = new StringBuilder();
                for (int i = 0; i < bundle.Jsons.Length; i++)
                {
                    if (i > 0) sb.AppendLine().AppendLine("// ---- merged with: " + (i < bundle.Sources.Length ? bundle.Sources[i] : "?"));
                    else sb.AppendLine("// source: " + (bundle.Sources.Length > 0 ? bundle.Sources[0] : "?"));
                    sb.AppendLine(bundle.Jsons[i]);
                }
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                written++;
            }
            return new JObject { ["written"] = written, ["skipped"] = skipped, ["folder"] = root };
        }

        public JObject DumpMaterials(string nameFilter)
        {
            Directory.CreateDirectory(DumpRoot);
            var list = new JArray();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null) continue;
                if (!string.IsNullOrEmpty(nameFilter) && m.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var o = new JObject { ["name"] = m.name, ["shader"] = m.shader != null ? m.shader.name : null };
                var textures = new JObject();
                var colors = new JObject();
                var floats = new JObject();
                var shader = m.shader;
                if (shader != null)
                {
                    int count = shader.GetPropertyCount();
                    for (int i = 0; i < count; i++)
                    {
                        var prop = shader.GetPropertyName(i);
                        switch (shader.GetPropertyType(i))
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                var t = m.GetTexture(prop);
                                if (t != null) textures[prop] = t.name + " " + t.width + "x" + t.height;
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Color:
                                var c = m.GetColor(prop);
                                colors[prop] = $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Float:
                            case UnityEngine.Rendering.ShaderPropertyType.Range:
                                floats[prop] = m.GetFloat(prop);
                                break;
                        }
                    }
                }
                o["textures"] = textures; o["colors"] = colors; o["floats"] = floats;
                list.Add(o);
            }
            var file = Path.Combine(DumpRoot, "materials.json");
            File.WriteAllText(file, list.ToString(), new UTF8Encoding(false));
            return new JObject { ["materials"] = list.Count, ["file"] = file };
        }

        public JObject DumpTextures(string nameFilter, int maxCount)
        {
            var root = Path.Combine(DumpRoot, "textures");
            Directory.CreateDirectory(root);
            int written = 0, failed = 0;
            var names = new JArray();
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex == null || string.IsNullOrEmpty(tex.name)) continue;
                if (!string.IsNullOrEmpty(nameFilter) && tex.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (written >= maxCount) break;
                try
                {
                    var png = ReadableCopy(tex).EncodeToPNG();
                    var safe = tex.name;
                    foreach (var ch in Path.GetInvalidFileNameChars()) safe = safe.Replace(ch, '_');
                    File.WriteAllBytes(Path.Combine(root, safe + ".png"), png);
                    names.Add(tex.name + " " + tex.width + "x" + tex.height);
                    written++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Debug.LogWarning($"[Wardens] dump texture {tex.name}: {ex.Message}");
                }
            }
            return new JObject { ["written"] = written, ["failed"] = failed, ["folder"] = root, ["names"] = names };
        }

        // GPU-only textures cannot be read directly; blit through a temporary RenderTexture.
        private static Texture2D ReadableCopy(Texture2D source)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
