using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Isolated exhibit-style preview for the continuous water cube.</summary>
public sealed class GPGPWaterTest : MonoBehaviour
{
    public Material[] waterMaterials;
    public Material waterSideMaterial;
    public Material visibilityMaterial;
    [Min(.1f)] public float testLightIntensity = 1.0f;
    public Vector3 cameraPosition = new Vector3(6,3.6f,-10);
    public Vector3 compartmentSize = new Vector3(4,1.1f,3.5f);
    [Tooltip("Optional diagnostic objects; off for the water-only diorama preview.")]
    public bool showVisibilityProbes = false;
    [Tooltip("Create a standalone preview camera. Disable when CC_HUB stereo cameras are in the scene.")]
    public bool createPreviewCamera = true;
    Material reflectionCardMaterial;
    Material continuousWaterMaterial;
    const int ReflectionCardLayer = 30;

    IEnumerator Start()
    {
        if (waterMaterials == null || waterMaterials.Length != 4 ||
            System.Array.Exists(waterMaterials, material => !material))
        {
            Debug.LogError("Assign Micro, Meso, Macro and Mega water materials in that order.",this);
            yield break;
        }
        // One volume and one top surface. The four original presets supply the color palette.
        continuousWaterMaterial=new Material(waterMaterials[3]) { name="Continuous water cube (preview)" };
        continuousWaterMaterial.SetFloat("_LayeredTint",1);
        continuousWaterMaterial.SetFloat("_BoundaryEmissionStrength",.25f);
        continuousWaterMaterial.SetFloat("_WaveAmplitude",.14f);
        continuousWaterMaterial.SetFloat("_WaveAmplitude2",.05f);
        continuousWaterMaterial.SetFloat("_SpecularStrength",.1f);
        continuousWaterMaterial.SetFloat("_Smoothness",.7f);
        continuousWaterMaterial.SetFloat("_SurfaceOpacity",.16f);
        continuousWaterMaterial.SetFloat("_SurfaceEmissionStrength",.24f);
        string[] colorProperties={"_MicroLayerColor","_MesoLayerColor","_MacroLayerColor","_MegaLayerColor"};
        for (int i=0; i<4; i++)
            continuousWaterMaterial.SetColor(colorProperties[i],waterMaterials[i].GetColor("_WaterShallowColor"));
        var container=new GameObject("Continuous water cube");
        container.transform.SetParent(transform,false);
        float height=Mathf.Max(.1f,compartmentSize.y);
        var compartment=container.AddComponent<GPGPWaterCompartment>();
        compartment.waterMaterial=continuousWaterMaterial;
        compartment.sideMaterial=waterSideMaterial;
        compartment.size=new Vector3(Mathf.Max(.1f,compartmentSize.x),height*4,Mathf.Max(.1f,compartmentSize.z));
        compartment.includeBottom=true;
        if (showVisibilityProbes)
        {
            for (int i=0; i<4; i++) for (int j=0; j<3; j++)
            {
                var probe=GameObject.CreatePrimitive(j==1?PrimitiveType.Sphere:PrimitiveType.Cube);
                probe.name="Layer "+i+" visibility probe "+j;
                probe.transform.SetParent(container.transform,false);
                probe.transform.localPosition=new Vector3((j-1)*.75f,(i-1.5f)*height+(j-1)*height*.25f,(j-1)*.25f);
                probe.transform.localScale=Vector3.one*.32f;
                probe.GetComponent<MeshRenderer>().sharedMaterial=visibilityMaterial;
            }
        }
        if (createPreviewCamera)
        {
            var cameraObject=new GameObject("Water test camera",typeof(Camera));
            cameraObject.transform.SetParent(transform,false);
            cameraObject.transform.localPosition=cameraPosition;
            cameraObject.transform.LookAt(transform.position);
            var camera=cameraObject.GetComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.003f,.006f,.012f);
            camera.nearClipPlane=.1f;
            camera.fieldOfView=35;
            camera.allowHDR=true;
            camera.cullingMask &= ~(1<<ReflectionCardLayer);
        }
        var lightObject=new GameObject("Water test light",typeof(Light));
        lightObject.transform.SetParent(transform,false);
        // For this camera, a light behind the samples gives readable surface highlights.
        lightObject.transform.localRotation=Quaternion.Euler(18,149,0);
        var light=lightObject.GetComponent<Light>();
        light.type=LightType.Directional; light.intensity=testLightIntensity;
        light.color=new Color(.88f,.94f,1);
        var fillObject=new GameObject("Water test soft fill",typeof(Light));
        fillObject.transform.SetParent(transform,false);
        fillObject.transform.localPosition=new Vector3(-3,3,-4);
        var fill=fillObject.GetComponent<Light>();
        fill.type=LightType.Point; fill.range=16; fill.intensity=2.5f;
        fill.color=new Color(.3f,.65f,1);

        // A one-shot local reflection environment, solely for shader validation.
        // The camera does not draw these cards; the probe captures their actual geometry.
        reflectionCardMaterial=new Material(visibilityMaterial) { name="Water test reflection cards" };
        reflectionCardMaterial.SetColor("_BaseColor",Color.black);
        reflectionCardMaterial.EnableKeyword("_EMISSION");
        reflectionCardMaterial.SetColor("_EmissionColor",new Color(.8f,1.0f,1.2f));
        AddReflectionCard(new Vector3(-3,4,7),new Vector3(10,.05f,10));
        AddReflectionCard(new Vector3(-4,2,6),new Vector3(.05f,4,8));
        var reflectionObject=new GameObject("Water test reflection probe",typeof(ReflectionProbe));
        reflectionObject.transform.SetParent(transform,false);
        reflectionObject.transform.localPosition=new Vector3(0,.8f,0);
        var reflection=reflectionObject.GetComponent<ReflectionProbe>();
        reflection.mode=ReflectionProbeMode.Realtime;
        reflection.refreshMode=ReflectionProbeRefreshMode.ViaScripting;
        reflection.timeSlicingMode=ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        reflection.resolution=128; reflection.hdr=true;
        reflection.size=new Vector3(25,15,25); reflection.intensity=.15f;
        reflection.clearFlags=ReflectionProbeClearFlags.SolidColor;
        reflection.backgroundColor=new Color(.012f,.025f,.04f);
        reflection.cullingMask=1<<ReflectionCardLayer;
        yield return null;
        reflection.RenderProbe();
    }

    void AddReflectionCard(Vector3 position, Vector3 scale)
    {
        var card=GameObject.CreatePrimitive(PrimitiveType.Cube);
        card.name="Reflection-only softbox";
        card.layer=ReflectionCardLayer;
        card.transform.SetParent(transform,false);
        card.transform.localPosition=position; card.transform.localScale=scale;
        Destroy(card.GetComponent<Collider>());
        var renderer=card.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=reflectionCardMaterial;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
    }

    void OnDestroy()
    {
        if (reflectionCardMaterial) Destroy(reflectionCardMaterial);
        if (continuousWaterMaterial) Destroy(continuousWaterMaterial);
    }
}
