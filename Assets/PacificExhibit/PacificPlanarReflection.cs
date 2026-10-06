using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PacificExhibit
{
    /// <summary>Prepares a separate planar floor reflection for each mono content camera.</summary>
    [ExecuteAlways, DefaultExecutionOrder(100)]
    public sealed class PacificPlanarReflection : MonoBehaviour
    {
        public Camera sourceCamera;
        [Tooltip("Additional mono cameras, such as Cyber Canoe's left and right off-axis views.")]
        public Camera[] additionalSourceCameras = new Camera[0];
        public Renderer floorRenderer;
        [Range(256, 2048)] public int textureWidth = 1024;
        [Range(0f, 1f)] public float reflectionStrength = 0.55f;

        sealed class ReflectionResources
        {
            public Camera camera;
            public RenderTexture texture;
            public Matrix4x4 viewProjection;
            public bool valid;
        }

        static readonly int ReflectionTextureId = Shader.PropertyToID("_ReflectionTex");
        static readonly int ReflectionViewProjectionId = Shader.PropertyToID("_ReflectionVP");
        static readonly int ReflectionEnabledId = Shader.PropertyToID("_ReflectionEnabled");
        static readonly int ReflectionFlipYId = Shader.PropertyToID("_ReflectionFlipY");
        static readonly int ReflectionStrengthId = Shader.PropertyToID("_ReflectionStrength");
        static readonly int ExhibitOriginId = Shader.PropertyToID("_ExhibitOrigin");
        readonly Dictionary<Camera, ReflectionResources> reflections = new Dictionary<Camera, ReflectionResources>();
        readonly List<Camera> staleCameras = new List<Camera>();
        MaterialPropertyBlock properties;
        double lastEditorUpdate;
        bool rendering;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            BindReflection(null);
        }

        void LateUpdate()
        {
            if (!floorRenderer || rendering) return;
            if (!Application.isPlaying)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - lastEditorUpdate < 0.15) return;
                lastEditorUpdate = now;
            }

            RemoveUnusedResources();
            // CC_CAMERAOFFSET updates its custom projection and view in LateUpdate
            // at order 0. Render here, before URP starts rendering the main cameras.
            if (CanRenderAutomatically(sourceCamera)) RenderReflectionFor(sourceCamera);
            if (additionalSourceCameras != null)
            {
                for (int i = 0; i < additionalSourceCameras.Length; i++)
                {
                    Camera source = additionalSourceCameras[i];
                    if (source == sourceCamera || !CanRenderAutomatically(source)) continue;
                    bool duplicate = false;
                    for (int j = 0; j < i; j++)
                        if (additionalSourceCameras[j] == source) { duplicate = true; break; }
                    if (!duplicate) RenderReflectionFor(source);
                }
            }
            // Refresh the baseline wash even when all content cameras are disabled.
            properties ??= new MaterialPropertyBlock();
            floorRenderer.GetPropertyBlock(properties);
            properties.SetVector(ExhibitOriginId, floorRenderer.bounds.center);
            floorRenderer.SetPropertyBlock(properties);
        }

        static bool CanRenderAutomatically(Camera source) =>
            source && source.enabled && source.gameObject.activeInHierarchy &&
            source.cameraType == CameraType.Game && !source.stereoEnabled;

        /// <summary>Refreshes the primary camera's reflection for standalone previews.</summary>
        public void RenderReflection() => RenderReflectionFor(sourceCamera);

        /// <summary>Also supports an explicit preview render while the source camera is disabled.</summary>
        public void RenderReflectionFor(Camera source)
        {
            if (!source || !floorRenderer || rendering || !isActiveAndEnabled ||
                source.cameraType != CameraType.Game || source.stereoEnabled ||
                !GraphicsSettings.currentRenderPipeline) return;

            ReflectionResources resources = EnsureResources(source);
            Camera reflectionCamera = resources.camera;
            resources.valid = false;
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled = false;
            reflectionCamera.targetTexture = resources.texture;
            reflectionCamera.rect = new Rect(0f, 0f, 1f, 1f);
            reflectionCamera.allowHDR = true;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.ResetCullingMatrix();
            var data = reflectionCamera.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;

            float height = floorRenderer.transform.position.y;
            Matrix4x4 mirror = Matrix4x4.identity;
            mirror.m11 = -1f;
            mirror.m13 = height * 2f;
            reflectionCamera.transform.position = mirror.MultiplyPoint(source.transform.position);
            reflectionCamera.transform.rotation = Quaternion.Euler(-source.transform.eulerAngles.x,
                source.transform.eulerAngles.y, source.transform.eulerAngles.z);
            reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * mirror;
            Vector3 planePoint = new Vector3(0f, height + 0.025f, 0f);
            Vector3 cameraPoint = reflectionCamera.worldToCameraMatrix.MultiplyPoint(planePoint);
            Vector3 cameraNormal = reflectionCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
            Vector4 clipPlane = new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z,
                -Vector3.Dot(cameraPoint, cameraNormal));
            // Preserve the source's asymmetric wall projection before applying
            // the reflected floor clip plane.
            reflectionCamera.projectionMatrix = source.projectionMatrix;
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = resources.texture };
            if (!RenderPipeline.SupportsRenderRequest(reflectionCamera, request)) return;
            bool originalCulling = GL.invertCulling;
            bool originalEnabled = floorRenderer.enabled;
            rendering = true;
            try
            {
                floorRenderer.enabled = false;
                GL.invertCulling = !originalCulling;
                RenderPipeline.SubmitRenderRequest(reflectionCamera, request);
                resources.viewProjection = GL.GetGPUProjectionMatrix(
                    reflectionCamera.projectionMatrix, true) * reflectionCamera.worldToCameraMatrix;
                resources.valid = true;
            }
            finally
            {
                GL.invertCulling = originalCulling;
                floorRenderer.enabled = originalEnabled;
                rendering = false;
            }
            BindReflection(resources);
        }

        void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // SingleCameraRequest raises this event too. This callback only binds
            // prepared data; it never submits another render request.
            if (rendering || !floorRenderer) return;
            if (camera && reflections.TryGetValue(camera, out ReflectionResources resources))
                BindReflection(resources);
            else
                BindReflection(null);
        }

        void BindReflection(ReflectionResources resources)
        {
            if (!floorRenderer) return;
            properties ??= new MaterialPropertyBlock();
            floorRenderer.GetPropertyBlock(properties);
            properties.SetVector(ExhibitOriginId, floorRenderer.bounds.center);
            bool valid = resources != null && resources.valid && resources.texture && resources.texture.IsCreated();
            properties.SetFloat(ReflectionEnabledId, valid ? 1f : 0f);
            // MaterialPropertyBlock.SetTexture rejects null. A built-in black
            // texture clears the owned render-texture reference before release.
            properties.SetTexture(ReflectionTextureId, valid ? (Texture)resources.texture : Texture2D.blackTexture);
            properties.SetMatrix(ReflectionViewProjectionId, valid ? resources.viewProjection : Matrix4x4.identity);
            properties.SetFloat(ReflectionFlipYId, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
            properties.SetFloat(ReflectionStrengthId, reflectionStrength);
            floorRenderer.SetPropertyBlock(properties);
        }

        ReflectionResources EnsureResources(Camera source)
        {
            if (!reflections.TryGetValue(source, out ReflectionResources resources))
            {
                resources = new ReflectionResources();
                reflections.Add(source, resources);
            }
            if (!resources.camera)
            {
                var helper = new GameObject("Pacific floor reflection - " + source.name)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                resources.camera = helper.AddComponent<Camera>();
                resources.camera.enabled = false;
                helper.AddComponent<UniversalAdditionalCameraData>();
            }
            float aspect = source.targetTexture
                ? (float)source.targetTexture.width / Mathf.Max(1, source.targetTexture.height)
                : source.aspect;
            int width = Mathf.Clamp(textureWidth, 256, Mathf.Max(256, SystemInfo.maxTextureSize));
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(0.1f, aspect)),
                128, Mathf.Max(128, SystemInfo.maxTextureSize));
            if (resources.texture && resources.texture.IsCreated() &&
                resources.texture.width == width && resources.texture.height == height) return resources;
            ReleaseTexture(resources);
            resources.texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = "Pacific floor reflection HDR - " + source.name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            resources.texture.Create();
            return resources;
        }

        bool IsRegistered(Camera source)
        {
            if (source == sourceCamera) return true;
            if (additionalSourceCameras != null)
                foreach (Camera additional in additionalSourceCameras)
                    if (source == additional) return true;
            return false;
        }

        void RemoveUnusedResources()
        {
            staleCameras.Clear();
            foreach (var pair in reflections)
                if (!pair.Key || !IsRegistered(pair.Key)) staleCameras.Add(pair.Key);
            foreach (Camera source in staleCameras)
            {
                ReleaseResources(reflections[source]);
                reflections.Remove(source);
            }
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            BindReflection(null);
            foreach (ReflectionResources resources in reflections.Values) ReleaseResources(resources);
            reflections.Clear();
            staleCameras.Clear();
        }

        static void ReleaseResources(ReflectionResources resources)
        {
            ReleaseTexture(resources);
            if (resources.camera) DestroyResource(resources.camera.gameObject);
            resources.camera = null;
        }

        static void ReleaseTexture(ReflectionResources resources)
        {
            resources.valid = false;
            if (!resources.texture) return;
            if (resources.camera) resources.camera.targetTexture = null;
            resources.texture.Release();
            DestroyResource(resources.texture);
            resources.texture = null;
        }

        static void DestroyResource(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
