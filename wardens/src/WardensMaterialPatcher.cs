// WardensMaterialPatcher.cs. Faction skins for materials vanilla offers no JSON hook for.
//
// FactionSpec.Textures/ChildTextures swap the beaver skins, but the bot model, the carried-goods
// models and the zipline cable use shared Materials from the game bundles. Leaf Coats /
// Emberpelts patch those through the BobingaboutScriptPack; this is the same idea in 60 lines,
// so the Wardens have no dependency: when a Wardens game loads, find the material by name,
// remember its original texture, assign ours (loaded through the game's IAssetLoader from the
// mod's Materials/ folder), and put the original back when the game unloads so an Iron Teeth
// game started afterwards keeps its own bots.

using System;
using System.Collections.Generic;
using Timberborn.AssetSystem;
using Timberborn.GameFactionSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensMaterialPatcher : ILoadableSingleton, IUnloadableSingleton
    {
        private struct Patch
        {
            public string MaterialName;
            public string Property;
            public string TexturePath;
        }

        private static readonly Patch[] Patches =
        {
            new Patch { MaterialName = "Bot.IronTeeth", Property = "_BaseMap", TexturePath = "Materials/Bots/Wardens/Bot.Wardens" },
            new Patch { MaterialName = "BeaverCarryingModels", Property = "_MainTex", TexturePath = "Materials/Textures/BeaverCarryingModels.Wardens" },
            new Patch { MaterialName = "ZiplineCable", Property = "_MainTex", TexturePath = "Materials/Textures/ZiplineCable.Wardens" },
        };

        private readonly FactionService _factionService;
        private readonly IAssetLoader _assetLoader;
        private readonly List<(Material material, string property, Texture original)> _applied =
            new List<(Material, string, Texture)>();

        public WardensMaterialPatcher(FactionService factionService, IAssetLoader assetLoader)
        {
            _factionService = factionService;
            _assetLoader = assetLoader;
        }

        public void Load()
        {
            if (_factionService.Current?.Id != WardensStartingPopulation.FactionId) return;
            Material[] all;
            try { all = Resources.FindObjectsOfTypeAll<Material>(); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] material scan failed: " + ex.Message); return; }

            foreach (var patch in Patches)
            {
                Texture2D texture;
                try { texture = _assetLoader.LoadSafe<Texture2D>(patch.TexturePath); }
                catch (Exception ex) { Debug.LogWarning($"[Wardens] texture {patch.TexturePath}: {ex.Message}"); continue; }
                if (texture == null) { Debug.LogWarning($"[Wardens] texture not found: {patch.TexturePath}"); continue; }

                int hits = 0;
                foreach (var material in all)
                {
                    if (material == null || material.name != patch.MaterialName) continue;
                    if (!material.HasProperty(patch.Property)) continue;
                    _applied.Add((material, patch.Property, material.GetTexture(patch.Property)));
                    material.SetTexture(patch.Property, texture);
                    hits++;
                }
                Debug.Log($"[Wardens] material {patch.MaterialName}.{patch.Property} <- {patch.TexturePath} ({hits} material(s))");
            }
        }

        public void Unload()
        {
            foreach (var (material, property, original) in _applied)
            {
                try { if (material != null) material.SetTexture(property, original); } catch { }
            }
            _applied.Clear();
        }
    }
}
