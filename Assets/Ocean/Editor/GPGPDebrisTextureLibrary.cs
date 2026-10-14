#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Builds reusable URP materials and prefabs from the supplied debris texture packs.</summary>
public static class GPGPDebrisTextureLibrary
{
    const string PreparedRoot = "Assets/Debris/Prepared";
    const string MaterialRoot = "Assets/Debris/Materials";
    const string MatteOwner = "GPGP_DEBRIS_MATTE";
    static readonly string[] Categories = { "Microplastic", "Mesoplastic", "Macroplastic", "Megaplastic" };
    static GameObject[] extraMega = Array.Empty<GameObject>();
    static readonly Dictionary<string, GameObject> matteModels = new Dictionary<string, GameObject>();
    static readonly Dictionary<Material, Material> matteMaterials = new Dictionary<Material, Material>();

    sealed class Surface
    {
        public string key, albedo, normal, metallic, roughness, occlusion, orm, specular;
        public bool transparent;
        public bool normalDirectX;
        public int textureMaxSize;
    }

    sealed class Source
    {
        public string model, alias;
        public int category;
        public Surface main, rope;
        public bool centerModel;
    }

    static Surface Maps(string key, string folder, string albedo, string normal, string roughness,
        string metallic = null, string occlusion = null, bool transparent = false,
        bool normalDirectX = false, int textureMaxSize = 0)
    {
        return new Surface { key = key, albedo = folder + albedo, normal = folder + normal,
            roughness = roughness == null ? null : folder + roughness,
            metallic = metallic == null ? null : folder + metallic,
            occlusion = occlusion == null ? null : folder + occlusion, transparent = transparent,
            normalDirectX = normalDirectX, textureMaxSize = textureMaxSize };
    }

    static Source[] Sources()
    {
        return new[]
        {
            new Source { category = 2, model = "Assets/Debris/Macroplastic/Plastic_Bucket.fbx",
                alias = "Assets/Debris/plastic-bucket/source/Plastic_Bucket/Plastic_Bucket/Plastic_Bucket.fbx",
                main = Maps("Plastic_Bucket", "Assets/Debris/plastic-bucket/textures/", "Plastic_Bucket_Albedo.png",
                    "Plastic_Bucket_Normal.png", "Plastic_Bucket_Roughness.png", "Plastic_Bucket_Metallic.png", "Plastic_Bucket_AO.png") },
            new Source { category = 2, model = "Assets/Debris/Macroplastic/plastic-water-bottle.fbx",
                alias = "Assets/Debris/low-poly-dirty-plastic-bottle-game-ready/source/plastic-water-bottle/plastic-water-bottle/plastic-water-bottle.fbx",
                main = Maps("Plastic_Water_Bottle", "Assets/Debris/low-poly-dirty-plastic-bottle-game-ready/textures/",
                    "plastic-water_BaseColor.png", "plastic-water_NormaMap-OpenGL.png", "plastic-water_Roughness.png",
                    "plastic-water_Metallic.png", "plastic-water_AmbientOclusion.png", true) },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/lifebuoy.fbx",
                alias = "Assets/Debris/lifebuoy/source/lifebuoy/lifebuoy/lifebuoy.fbx",
                main = Maps("Lifebuoy", "Assets/Debris/lifebuoy/textures/", "lifebuoy_Albedo.png",
                    "lifebuoy_Normal.png", "lifebuoy_Roughness.png", "lifebuoy_Metalness.png", "lifebuoy_AO.png"),
                rope = Maps("Lifebuoy_Rope", "Assets/Debris/lifebuoy/textures/", "rope_diffuse.png",
                    "rope_normal.png", "rope_rougness.png") },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/PlasticCrate.fbx",
                alias = "Assets/Debris/plastic-crate/source/PlasticCrate.fbx",
                main = Maps("Plastic_Crate", "Assets/Debris/plastic-crate/textures/", "Crate_Base_color.png",
                    "Crate_Normal.png", "Crate_Roughness.png") },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/PlasticJerrycan.FBX",
                alias = "Assets/Debris/plastic-jerrycan/source/PlasticJerrycan/PlasticJerrycan.FBX",
                main = Maps("Plastic_Jerrycan", "Assets/Debris/plastic-jerrycan/textures/", "PlasticJerrycan_Albedo.jpeg",
                    "PlasticJerrycan_Normal.png", "PlasticJerrycan_Roughness.png", null, "PlasticJerrycan_AO.png") },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/plastic_barrel.FBX",
                alias = "Assets/Debris/plastic_barrel/plastic_barrel.FBX",
                main = new Surface { key = "Plastic_Barrel", albedo = "Assets/Debris/plastic_barrel/diffus.tga",
                    normal = "Assets/Debris/plastic_barrel/normal.tga", specular = "Assets/Debris/plastic_barrel/specular.tga" } },
            new Source { category = 3, model = "Assets/Debris/plastic-crate-01/source/plastic_crate_1.fbx",
                main = new Surface { key = "Plastic_Crate_01", albedo = "Assets/Debris/plastic-crate-01/textures/base_color.jpg",
                    normal = "Assets/Debris/plastic-crate-01/textures/normal.jpg",
                    // The channel content is consistent with ORM: R=AO, G=roughness, B=metallic.
                    orm = "Assets/Debris/plastic-crate-01/textures/ao_metallic_roughness.jpg" } },
            new Source { category = 2, model = "Assets/Debris/Macroplastic/PlasticCanister.fbx",
                alias = "Assets/Debris/7-plastic-canister-lowpoly/source/canister-7.fbx", centerModel = true,
                main = Maps("Plastic_Canister", "Assets/Debris/7-plastic-canister-lowpoly/textures/",
                    "canister-7_diffuse.png", "canister-7_Normal_DirectX.png", "canister-7_roughness.png",
                    occlusion: "canister-7_AO.png", normalDirectX: true, textureMaxSize: 2048) },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/PlasticGardenChair.fbx",
                alias = "Assets/Debris/aged-plastic-garden-chair/source/PlasticChair_LP.fbx", centerModel = true,
                main = Maps("Plastic_Garden_Chair", "Assets/Debris/aged-plastic-garden-chair/textures/",
                    "PlasticChair_LP_V2_BaseColor.default.png", "PlasticChair_LP_V2_Normal.default.png",
                    "PlasticChair_LP_V2_Roughness.default.png", occlusion: "PlasticChair_LP_V2_AO.default.png",
                    textureMaxSize: 2048) },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/TruckTire.fbx",
                alias = "Assets/Debris/dirty-truck-tire/source/truck tire.fbx", centerModel = true,
                main = Maps("Truck_Tire", "Assets/Debris/dirty-truck-tire/textures/",
                    "truck_tire_tire__BaseColor.png", "truck_tire_tire__Normal.png", "truck_tire_tire__Roughness.png",
                    metallic: "truck_tire_tire__Metallic.png", textureMaxSize: 2048) },
            new Source { category = 3, model = "Assets/Debris/Megaplastic/TirePile.fbx",
                alias = "Assets/Debris/dirty-old-tires/source/TirePile.fbx", centerModel = true,
                main = Maps("Tire_Pile", "Assets/Debris/dirty-old-tires/textures/",
                    "Tire01_Low_Tire01_BaseColor.png", "Tire01_Low_Tire01_Normal.png", "Tire01_Low_Tire01_Roughness.png",
                    occlusion: "GrayscaleTire01_Low_Tire01_AO.png", textureMaxSize: 2048) }
        };
    }

    /// <summary>Maps each available raw category FBX and its package alias to a shared textured prefab.</summary>
    public static Dictionary<GameObject, GameObject> PrepareModels()
    {
        matteModels.Clear();
        matteMaterials.Clear();
        var prepared = new Dictionary<GameObject, GameObject>();
        var additions = new List<GameObject>();
        extraMega = Array.Empty<GameObject>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader || !shader.isSupported)
        {
            Debug.LogWarning("GPGP debris textures: URP/Lit is unavailable; retaining the existing palette models.");
            return prepared;
        }
        foreach (Source source in Sources())
        {
            if (source.centerModel)
            {
                PrepareSourceModel(source.model);
                PrepareSourceModel(source.alias);
            }
            GameObject raw = AssetDatabase.LoadAssetAtPath<GameObject>(source.model);
            if (!raw || !File.Exists(source.main.albedo)) continue;
            Material main = PrepareMaterial(source.main, shader);
            if (!main) continue;
            Material rope = source.rope == null ? null : PrepareMaterial(source.rope, shader);
            string folder = PreparedRoot + "/" + Categories[source.category];
            EnsureFolder(folder);
            string path = folder + "/" + source.main.key + ".prefab";
            var temporary = new GameObject(raw.name);
            try
            {
                GameObject instance = Object.Instantiate(raw, temporary.transform);
                instance.name = raw.name;
                if (source.centerModel)
                {
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) throw new InvalidOperationException("Imported debris has no renderers: " + source.model);
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    // Imported chair/tire meshes have distant pivots. Center the
                    // wrapper without rewriting their geometry or unit conversion.
                    instance.transform.localPosition -= temporary.transform.InverseTransformPoint(bounds.center);
                }
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] slots = renderer.sharedMaterials;
                    if (slots.Length == 0) slots = new Material[1];
                    for (int index = 0; index < slots.Length; index++)
                    {
                        bool isRope = slots[index] && slots[index].name.IndexOf("rope", StringComparison.OrdinalIgnoreCase) >= 0;
                        // When part of a pack is absent, leave that part for the palette fallback.
                        slots[index] = isRope && source.rope != null ? rope ?? slots[index] : main;
                    }
                    renderer.sharedMaterials = slots;
                }
                // Overwriting the same path preserves the existing prefab's GUID and scene references.
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, path);
                if (!prefab) throw new InvalidOperationException("Could not prepare debris prefab: " + path);
                prepared[raw] = prefab;
                if (!string.IsNullOrEmpty(source.alias))
                {
                    GameObject alias = AssetDatabase.LoadAssetAtPath<GameObject>(source.alias);
                    if (alias) prepared[alias] = prefab;
                }
                if (source.model.Contains("/plastic-crate-01/")) additions.Add(prefab);
            }
            finally { Object.DestroyImmediate(temporary); }
        }
        extraMega = additions.ToArray();
        AssetDatabase.SaveAssets();
        return prepared;
    }

    static void PrepareSourceModel(string path)
    {
        if (!FileExists(path)) return;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!importer) throw new InvalidOperationException("Expected an FBX model importer: " + path);
        bool changed = !importer.useFileUnits || !importer.useFileScale || importer.globalScale != 1f ||
            importer.importCameras || importer.importLights;
        // Honor centimetre/millimetre FBX units consistently on a fresh import.
        // Raw meshes and the original archives remain unchanged.
        importer.useFileUnits = true;
        importer.useFileScale = true;
        importer.globalScale = 1f;
        importer.importCameras = false;
        importer.importLights = false;
        if (changed) importer.SaveAndReimport();
    }

    /// <summary>Call after PrepareModels. The additional crate belongs to Megaplastic (category 3).</summary>
    public static GameObject[] AdditionalModelsForCategory(int category)
    {
        return category == 3 ? (GameObject[])extraMega.Clone() : Array.Empty<GameObject>();
    }

    /// <summary>Preserves authored URP appearance using reusable matte copies, without editing source assets.</summary>
    public static GameObject PrepareMatteModel(GameObject model, int category)
    {
        if (!model || !model.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials).Any(NeedsMatteCopy)) return model;
        if (category < 0 || category >= Categories.Length)
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(model, out string guid, out long localId) || string.IsNullOrEmpty(guid))
            return model;
        string cacheKey = category + ":" + guid + ":" + localId;
        if (matteModels.TryGetValue(cacheKey, out GameObject cached) && cached) return cached;
        string folder = PreparedRoot + "/" + Categories[category] + "/Matte";
        EnsureFolder(folder);
        string sourcePath = AssetDatabase.GetAssetPath(model);
        // Already prepared models normally return above. If their source is edited,
        // update this owned prefab rather than nesting another prepared copy.
        bool ownedPrepared = sourcePath.StartsWith(folder + "/", StringComparison.Ordinal) && IsOwnedMatteAsset(sourcePath);
        string safeName = new string(model.name.Select(character =>
            char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_').ToArray());
        if (string.IsNullOrEmpty(safeName)) safeName = "Debris_Model";
        string path = ownedPrepared ? sourcePath : folder + "/" + safeName + "_" + guid + ".prefab";
        if (File.Exists(path) && !IsOwnedMatteAsset(path))
            throw new InvalidOperationException("A custom asset occupies the generated matte prefab path: " + path);
        var temporary = Object.Instantiate(model);
        temporary.name = model.name;
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(temporary))
                PrefabUtility.UnpackPrefabInstance(temporary, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (Renderer renderer in temporary.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < slots.Length; index++)
                    if (NeedsMatteCopy(slots[index]))
                    {
                        slots[index] = PrepareMatteMaterial(slots[index]);
                        changed = true;
                    }
                if (changed) renderer.sharedMaterials = slots;
            }
            GameObject result = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            if (!result) throw new InvalidOperationException("Could not save matte debris prefab: " + path);
            MarkOwnedMatteAsset(path);
            matteModels[cacheKey] = result;
            return result;
        }
        finally { Object.DestroyImmediate(temporary); }
    }

    static bool NeedsMatteCopy(Material material)
    {
        if (!material || !material.shader || !material.shader.isSupported) return false;
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline && material == pipeline.defaultMaterial) return false;
        string tag = material.GetTag("RenderPipeline", false, string.Empty);
        if (tag != "UniversalPipeline" && !(string.IsNullOrEmpty(tag) &&
            material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))) return false;
        return (material.HasProperty("_SpecularHighlights") &&
                (material.GetFloat("_SpecularHighlights") != 0f || !material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"))) ||
            (material.HasProperty("_EnvironmentReflections") &&
                (material.GetFloat("_EnvironmentReflections") != 0f || !material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"))) ||
            (material.HasProperty("_EmissionColor") && HasEmission(material.GetColor("_EmissionColor"))) ||
            (material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap")) || material.IsKeywordEnabled("_EMISSION");
    }

    static Material PrepareMatteMaterial(Material source)
    {
        if (matteMaterials.TryGetValue(source, out Material cached) && cached) return cached;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId) || string.IsNullOrEmpty(guid))
            throw new InvalidOperationException("Authored debris materials must be persistent assets: " + source.name);
        string folder = MaterialRoot + "/Matte";
        EnsureFolder(folder);
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string path = sourcePath.StartsWith(folder + "/", StringComparison.Ordinal) && IsOwnedMatteAsset(sourcePath)
            ? sourcePath : folder + "/" + guid + "_" + localId + ".mat";
        if (File.Exists(path) && !IsOwnedMatteAsset(path))
            throw new InvalidOperationException("A custom asset occupies the generated matte material path: " + path);
        Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!copy)
        {
            copy = new Material(source) { name = source.name + " (Debris Matte)" };
            AssetDatabase.CreateAsset(copy, path);
            MarkOwnedMatteAsset(path);
        }
        else if (copy != source)
        {
            copy.shader = source.shader;
            copy.CopyPropertiesFromMaterial(source);
        }
        if (copy.HasProperty("_SpecularHighlights"))
        {
            copy.SetFloat("_SpecularHighlights", 0f);
            SetKeyword(copy, "_SPECULARHIGHLIGHTS_OFF", true);
        }
        if (copy.HasProperty("_EnvironmentReflections"))
        {
            copy.SetFloat("_EnvironmentReflections", 0f);
            SetKeyword(copy, "_ENVIRONMENTREFLECTIONS_OFF", true);
        }
        if (copy.HasProperty("_EmissionMap")) copy.SetTexture("_EmissionMap", null);
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
        SetKeyword(copy, "_EMISSION", false);
        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        copy.enableInstancing = true;
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssetIfDirty(copy);
        matteMaterials[source] = copy;
        return copy;
    }

    static bool HasEmission(Color color) => color.r > 0f || color.g > 0f || color.b > 0f;

    static bool IsOwnedMatteAsset(string path)
    {
        AssetImporter importer = AssetImporter.GetAtPath(path);
        return importer && importer.userData == MatteOwner;
    }

    static void MarkOwnedMatteAsset(string path)
    {
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (!importer) throw new InvalidOperationException("Missing importer for generated matte asset: " + path);
        importer.userData = MatteOwner;
        AssetDatabase.WriteImportSettingsIfDirty(path);
    }

    static Material PrepareMaterial(Surface surface, Shader shader)
    {
        Texture2D albedo = ImportTexture(surface.albedo, true, false, maxSize: surface.textureMaxSize);
        if (!albedo) return null;
        Texture2D normal = ImportTexture(surface.normal, false, true, surface.normalDirectX, surface.textureMaxSize);
        bool specular = !string.IsNullOrEmpty(surface.specular) && File.Exists(surface.specular);
        Texture2D mask = PackMask(surface, specular);
        EnsureFolder(MaterialRoot);
        string path = MaterialRoot + "/" + surface.key + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = surface.key };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        material.enableInstancing = true;
        material.SetTexture("_BaseMap", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_WorkflowMode", specular ? 0f : 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetColor("_SpecColor", new Color(.04f, .04f, .04f, 1f));
        material.SetTexture("_MetallicGlossMap", specular ? null : mask);
        material.SetTexture("_SpecGlossMap", specular ? mask : null);
        material.SetFloat("_Smoothness", mask ? 1f : .45f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        // Debris must read as ordinary plastic under the gallery's bright lights.
        // Keep its source roughness maps, but stop sharp highlights/reflections
        // from feeding the bloom that belongs to the exhibit's cyan frame.
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        material.SetTexture("_EmissionMap", null);
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        // URP reads occlusion from G; metallic masks below carry AO in that channel.
        material.SetTexture("_OcclusionMap", !specular && mask &&
            (FileExists(surface.occlusion) || FileExists(surface.orm)) ? mask : null);
        material.SetFloat("_OcclusionStrength", 1f);
        material.SetFloat("_Surface", surface.transparent ? 1f : 0f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetFloat("_ZWrite", surface.transparent ? 0f : 1f);
        material.SetFloat("_SrcBlend", surface.transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        material.SetFloat("_DstBlend", surface.transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", surface.transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.SetOverrideTag("RenderType", surface.transparent ? "Transparent" : "Opaque");
        material.renderQueue = surface.transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
        SetKeyword(material, "_NORMALMAP", normal);
        SetKeyword(material, "_METALLICSPECGLOSSMAP", mask);
        SetKeyword(material, "_SPECULAR_SETUP", specular);
        SetKeyword(material, "_OCCLUSIONMAP", material.GetTexture("_OcclusionMap"));
        SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", surface.transparent);
        SetKeyword(material, "_SPECULARHIGHLIGHTS_OFF", true);
        SetKeyword(material, "_ENVIRONMENTREFLECTIONS_OFF", true);
        SetKeyword(material, "_EMISSION", false);
        material.SetShaderPassEnabled("ShadowCaster", !surface.transparent);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D ImportTexture(string path, bool color, bool normal, bool flipGreen = false, int maxSize = 0)
    {
        if (!FileExists(path)) return null;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) return null;
        bool changed = importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
            importer.textureShape != TextureImporterShape.Texture2D ||
            importer.sRGBTexture != color || (normal && (importer.convertToNormalmap || importer.flipGreenChannel != flipGreen)) ||
            (maxSize > 0 && importer.maxTextureSize != maxSize);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = color;
        if (normal) { importer.convertToNormalmap = false; importer.flipGreenChannel = flipGreen; }
        if (maxSize > 0) importer.maxTextureSize = maxSize;
        if (changed) importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    sealed class Pixels
    {
        public int width, height;
        public Color32[] values;
        public Color32 At(int x, int y, int targetWidth, int targetHeight)
        {
            return values[Mathf.Min(height - 1, y * height / targetHeight) * width +
                Mathf.Min(width - 1, x * width / targetWidth)];
        }
    }

    static Pixels Read(string path, bool color = false, int maxSize = 0)
    {
        Texture2D texture = ImportTexture(path, color, false, maxSize: maxSize);
        if (!texture) return null;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool wasReadable = importer.isReadable;
        TextureImporterCompression compression = importer.textureCompression;
        try
        {
            // Read source channels without lossy block compression, then restore normal memory settings.
            if (!wasReadable || compression != TextureImporterCompression.Uncompressed)
            {
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return new Pixels { width = texture.width, height = texture.height, values = texture.GetPixels32() };
        }
        finally
        {
            if (importer.isReadable != wasReadable || importer.textureCompression != compression)
            {
                importer.isReadable = wasReadable;
                importer.textureCompression = compression;
                importer.SaveAndReimport();
            }
        }
    }

    static Texture2D PackMask(Surface surface, bool specular)
    {
        Pixels orm = Read(surface.orm, maxSize: surface.textureMaxSize);
        Pixels metallic = Read(surface.metallic, maxSize: surface.textureMaxSize);
        Pixels roughness = Read(surface.roughness, maxSize: surface.textureMaxSize);
        Pixels occlusion = Read(surface.occlusion, maxSize: surface.textureMaxSize);
        Pixels spec = specular ? Read(surface.specular, true, surface.textureMaxSize) : null;
        Pixels[] maps = new[] { orm, metallic, roughness, occlusion, spec }.Where(map => map != null).ToArray();
        if (maps.Length == 0) return null;
        int width = maps.Max(map => map.width), height = maps.Max(map => map.height);
        var output = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color32 combined = orm == null ? new Color32(255, 140, 0, 255) : orm.At(x, y, width, height);
                byte rough = roughness == null ? combined.g : roughness.At(x, y, width, height).r;
                byte metal = metallic == null ? (orm == null ? (byte)0 : combined.b) : metallic.At(x, y, width, height).r;
                byte ao = occlusion == null ? (orm == null ? (byte)255 : combined.r) : occlusion.At(x, y, width, height).r;
                Color32 result = spec == null ? new Color32(metal, ao, 0, (byte)(255 - rough)) : spec.At(x, y, width, height);
                // No barrel roughness texture was supplied: retain a conservative constant smoothness.
                if (spec != null) result.a = roughness == null ? (byte)115 : (byte)(255 - rough);
                output[y * width + x] = result;
            }
        EnsureFolder(MaterialRoot + "/Textures");
        string path = MaterialRoot + "/Textures/" + surface.key + (specular ? "_SpecularSmoothness.png" : "_MetallicOcclusionSmoothness.png");
        var packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            packed.SetPixels32(output);
            packed.Apply();
            byte[] png = packed.EncodeToPNG();
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png)) File.WriteAllBytes(path, png);
        }
        finally { Object.DestroyImmediate(packed); }
        // Specular RGB is a color; its smoothness alpha is unaffected by sRGB sampling.
        return ImportTexture(path, specular, false, maxSize: surface.textureMaxSize);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    static bool FileExists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
    }
}
#endif
