"""Generate a standalone URP water graph using installed Shader Graph templates.
Run from the project root. Does not modify the legacy ocean assets.
"""
from pathlib import Path
import copy
import json
import re
import uuid

# Reuse the existing serialization helpers, stopping before its asset-building code.
helpers = Path('tools/build_ocean_graph.py').read_text().split('for label, descriptor in')[0]
exec(compile(helpers, 'tools/build_ocean_graph.py', 'exec'))
g['m_Path'] = 'GPGP'
g['m_GraphPrecision'] = 0

defaults = {
    'WaterShallowColor': (.055, .38, .48, 1),
    'WaterDeepColor': (.008, .07, .14, 1),
    'WaterOpacity': .2, 'SurfaceOpacity': .07, 'DepthStrength': .7,
    'VolumeEmissionStrength': .3,
    'LayeredTint': 0., 'LayerBlendWidth': .012,
    'MicroBoundary': .08, 'MesoBoundary': .21, 'MacroBoundary': .46,
    'MicroLayerColor': (.035,.29,.41,1), 'MesoLayerColor': (.05,.37,.49,1),
    'MacroLayerColor': (.035,.4,.42,1), 'MegaLayerColor': (.04,.32,.47,1),
    'BoundaryWidth': .018, 'BoundaryEmissionStrength': .8,
    'WaveSpeed': .65, 'WaveAmplitude': .045, 'WaveFrequency': 2.1,
    'WaveDirection': (.9, .4, 0, 0),
    'WaveAmplitude2': .012, 'WaveFrequency2': 4.4, 'WaveSpeed2': -.48,
    'WaveDirection2': (-.35, .94, 0, 0),
    'RippleSpeed': .9, 'RippleScale': 12., 'RippleStrength': .04,
    'RippleDirection': (.7, .7, 0, 0),
    'RippleSpeed2': -.65, 'RippleScale2': 19., 'RippleStrength2': .022,
    'RippleDirection2': (-.8, .3, 0, 0),
    'Smoothness': .82, 'SpecularStrength': .18,
    'SurfaceEmissionColor': (.025, .3, .42, 1), 'SurfaceEmissionStrength': .12,
}
for i, (name, val) in enumerate(defaults.items()):
    vector = isinstance(val, tuple)
    color = 'Color' in name
    p = clone('ColorShaderProperty' if color else 'Vector4ShaderProperty' if vector else 'Vector1ShaderProperty')
    keys = 'rgba' if color else 'xyzw'
    v = dict(zip(keys, val)) if vector else val
    p.update(m_Name=name, m_DefaultReferenceName='_'+name, m_OverrideReferenceName='_'+name,
             m_RefNameGeneratedByDisplayName=name, m_Value=v, m_GeneratePropertyBlock=True, m_Hidden=False)
    p['m_Guid'] = {'m_GuidSerialized': str(uuid.uuid4())}
    if not vector: p['m_FloatType'] = 0
    g['m_Properties'].append({'m_Id': p['m_ObjectId']})
    n = node(name, 'PropertyNode', -650, i*130)
    n['m_Property'] = {'m_Id': p['m_ObjectId']}
    zero = dict.fromkeys('xyzw', 0) if vector else 0
    slot(name, 0).update(m_Type='UnityEditor.ShaderGraph.Vector4MaterialSlot' if vector else 'UnityEditor.ShaderGraph.Vector1MaterialSlot',
                        m_Value=zero, m_DefaultValue=copy.deepcopy(zero), m_DisplayName=name, m_ShaderOutputName=name)

for label, desc in [('positionOut','VertexDescription.Position'), ('colorOut','SurfaceDescription.BaseColor'),
                    ('normalOut','SurfaceDescription.NormalTS'), ('alphaOut','SurfaceDescription.Alpha'),
                    ('smoothOut','SurfaceDescription.Smoothness'), ('specularOut','SurfaceDescription.Specular'),
                    ('emissionOut','SurfaceDescription.Emission')]:
    n = node(label, desc, 800, 0)
    g['m_VertexContext' if desc.startswith('Vertex') else 'm_FragmentContext']['m_Blocks'].append({'m_Id':n['m_ObjectId']})
node('position', 'PositionNode', -1000, -400)['m_Space'] = 0
node('time', 'TimeNode', -1000, -200)
node('mask', 'VertexColorNode', -1000, 0)
node('uv', 'UVNode', -1000, 200)['m_OutputChannel'] = 0
node('dimensions', 'UVNode', -1000, 400)['m_OutputChannel'] = 1
fn = node('water', 'CustomFunctionNode', 0, 0)
fn.update(m_SourceType=1, m_FunctionName='GPGPWater', m_Name='GPGPWater (Custom Function)', m_FunctionSource='', m_Precision=1)
old_slots = {s['m_Id'] for s in fn['m_Slots']}
objects[:] = [o for o in objects if o['m_ObjectId'] not in old_slots]
fn['m_Slots'] = []

def port(name, dim, output=False):
    index = len(fn['m_Slots'])
    zero = 0 if dim == 1 else dict.fromkeys('xyzw'[:dim], 0)
    s = {'m_SGVersion':0, 'm_Type':f'UnityEditor.ShaderGraph.Vector{dim}MaterialSlot', 'm_ObjectId':uid(),
         'm_Id':index, 'm_DisplayName':name, 'm_ShaderOutputName':name, 'm_SlotType':1 if output else 0,
         'm_Hidden':False, 'm_Value':zero, 'm_DefaultValue':copy.deepcopy(zero), 'm_StageCapability':3,
         'm_Labels':[]}
    objects.append(s); fn['m_Slots'].append({'m_Id':s['m_ObjectId']})
    return index

for name, dim, src in [('PositionOS',3,'position'), ('Time',1,'time'), ('Mask',4,'mask'), ('UV',4,'uv'), ('Dimensions',4,'dimensions')]:
    edge(src, 0, 'water', port(name,dim))
for name, val in defaults.items():
    edge(name,0,'water',port(name,4 if isinstance(val,tuple) else 1))
for name, dim, dest in [('Displaced',3,'positionOut'), ('Color',3,'colorOut'), ('Normal',3,'normalOut'),
                        ('Alpha',1,'alphaOut'), ('Specular',3,'specularOut'), ('Emission',3,'emissionOut'),
                        ('WaterSmoothness',1,'smoothOut')]:
    edge('water',port(name,dim,True),dest,0)
fn['m_FunctionBody'] = '''
float surface = saturate(Mask.r);
float2 p = TransformObjectToWorld(PositionOS).xz;
float2 d = WaveDirection.xy / max(length(WaveDirection.xy), 0.0001);
float phase = dot(p, d) * WaveFrequency + Time * WaveSpeed;
float2 d2 = WaveDirection2.xy / max(length(WaveDirection2.xy), .0001);
float phase2 = dot(p, d2) * WaveFrequency2 + Time * WaveSpeed2;
float2 uv = saturate(UV.xy);
// Pin the perimeter to the stable volume rim. UV spans the surface once.
float envelope = sin(uv.x * 3.14159265) * sin(uv.y * 3.14159265);
float a1 = max(WaveAmplitude, 0);
float a2 = max(WaveAmplitude2, 0);
float wave = a1*sin(phase) + a2*sin(phase2);
float3 worldOffset = float3(0, wave * envelope * surface, 0);
Displaced = TransformWorldToObject(TransformObjectToWorld(PositionOS) + worldOffset);
float2 r1 = RippleDirection.xy / max(length(RippleDirection.xy), .0001);
float2 r2 = RippleDirection2.xy / max(length(RippleDirection2.xy), .0001);
float2 extent = max(float2(length(TransformObjectToWorldDir(float3(Dimensions.x,0,0),false)),
    length(TransformObjectToWorldDir(float3(0,0,Dimensions.y),false))), .001);
float2 envelopeSlope = 3.14159265 / extent * float2(cos(uv.x*3.14159265)*sin(uv.y*3.14159265),
    sin(uv.x*3.14159265)*cos(uv.y*3.14159265));
// Product rule: normals include the pinned-edge envelope, not just the sine waves.
float2 slope = envelope * (a1*WaveFrequency*cos(phase)*d + a2*WaveFrequency2*cos(phase2)*d2)
    + wave * envelopeSlope;
slope += RippleStrength * cos(dot(p,r1)*RippleScale + Time*RippleSpeed)*r1
    + RippleStrength2 * cos(dot(p,r2)*RippleScale2 + Time*RippleSpeed2)*r2;
// Horizontal, unrotated compartment meshes use tangent X and bitangent Z.
Normal = normalize(float3(-slope.x*surface, -slope.y*surface, 1));
float depth = 1 - exp2(-2 * (1-saturate(Mask.g)) * max(DepthStrength,0));
Color = lerp(WaterShallowColor.rgb, WaterDeepColor.rgb, depth);
// Four color regions in one continuous volume; no internal water planes.
float height = saturate(Mask.g);
float blendWidth = clamp(LayerBlendWidth,.001,.12);
float3 bandColor = MicroLayerColor.rgb;
bandColor = lerp(bandColor,MesoLayerColor.rgb,smoothstep(MicroBoundary-blendWidth,MicroBoundary+blendWidth,height));
bandColor = lerp(bandColor,MacroLayerColor.rgb,smoothstep(MesoBoundary-blendWidth,MesoBoundary+blendWidth,height));
bandColor = lerp(bandColor,MegaLayerColor.rgb,smoothstep(MacroBoundary-blendWidth,MacroBoundary+blendWidth,height));
Color = lerp(Color,bandColor*lerp(1,.4,depth),saturate(LayeredTint));
// Thin surface is clearer than the body; sides tint more strongly toward the bottom.
Alpha = saturate(lerp(WaterOpacity * lerp(.5,1,depth), SurfaceOpacity, surface));
// Keep flat volume walls from looking like polished glass or blue plastic.
Specular = saturate(SpecularStrength) * .08 * lerp(.08,1,surface);
WaterSmoothness = lerp(.35,saturate(Smoothness),surface);
Emission = SurfaceEmissionColor.rgb * max(SurfaceEmissionStrength,0) * (1-depth) * (1-depth)
    * lerp(.25,1,surface);
// A restrained body tint keeps water readable against black without raising surface glare.
Emission += Color
    * max(VolumeEmissionStrength,0) * (1-surface);
// Local mesh UVs define actual water boundaries, independent of camera/eye.
float width = max(BoundaryWidth,.0001);
float sideEdge = min(uv.x,1-uv.x);
float surfaceEdge = min(sideEdge,min(uv.y,1-uv.y));
float rim = 1-smoothstep(0,width,lerp(sideEdge,surfaceEdge,surface));
float waterline = smoothstep(1-width,1,saturate(Mask.g));
rim = max(rim,waterline*(1-surface));
float boundary = rim * saturate(BoundaryEmissionStrength);
Alpha = max(Alpha,.32*boundary);
Emission += SurfaceEmissionColor.rgb * max(BoundaryEmissionStrength,0) * rim;
'''
target = clone('UniversalTarget'); sub = clone('UniversalLitSubTarget')
target.update(m_Datas=[], m_ActiveSubTarget={'m_Id':sub['m_ObjectId']}, m_SurfaceType=1,
              m_ZTestMode=4, m_ZWriteControl=2, m_AlphaMode=0, m_AlphaClip=False,
              m_CastShadows=False, m_ReceiveShadows=True, m_AllowMaterialOverride=False, m_RenderFace=2)
sub.update(m_WorkflowMode=0, m_NormalDropOffSpace=0, m_ClearCoat=False, m_BlendModePreserveSpecular=True)
g['m_ActiveTargets'] = [{'m_Id':target['m_ObjectId']}]
category = clone('CategoryData'); category.update(m_Name='Water',m_ChildObjectList=copy.deepcopy(g['m_Properties']))
g['m_CategoryData'] = [{'m_Id':category['m_ObjectId']}]

def meta(path, importer='DefaultImporter', extra=''):
    path = Path(str(path)+'.meta')
    if path.exists(): return path.read_text().split('guid: ')[1].splitlines()[0]
    guid = uuid.uuid4().hex
    path.write_text(f'fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n'+extra)
    return guid

graph = Path('Assets/Ocean/SG_GPGP_Water.shadergraph')
graph.write_text('\n\n'.join(json.dumps(o,indent=4) for o in objects)+'\n')
guid = meta(graph, 'ScriptedImporter', '  serializedVersion: 2\n  script: {fileID: 11500000, guid: 625f186215c104763be7675aa2d941aa, type: 3}\n')
presets = {
    'Micro': {'WaterShallowColor':(.035,.29,.41,1),'WaterDeepColor':(.005,.045,.11,1),'WaveAmplitude':.035,'WaveAmplitude2':.009,'RippleScale':15.,'RippleStrength':.03,'WaterOpacity':.21},
    'Meso': {'WaterShallowColor':(.05,.37,.49,1),'WaveAmplitude':.045,'WaterOpacity':.2},
    'Macro': {'WaterShallowColor':(.035,.4,.42,1),'WaterDeepColor':(.005,.075,.105,1),'WaveAmplitude':.05,'WaterOpacity':.18},
    'Mega': {'WaterShallowColor':(.04,.32,.47,1),'WaterDeepColor':(.006,.05,.13,1),'WaveAmplitude':.065,'WaveAmplitude2':.017,'RippleStrength':.045,'SurfaceEmissionColor':(.08,.3,.43,1)}
}
for name, overrides in presets.items():
    props = defaults | overrides
    path = Path(f'Assets/Ocean/Materials/MAT_GPGP_Water_{name}.mat')
    floats = ''.join(f'    - _{k}: {v}\n' for k,v in props.items() if not isinstance(v,tuple))
    colors = ''.join(f'    - _{k}: {{r: {v[0]}, g: {v[1]}, b: {v[2]}, a: {v[3]}}}\n' for k,v in props.items() if isinstance(v,tuple))
    if path.exists():
        # Retain Unity's importer metadata and unrelated saved properties.
        saved = path.read_text()
        for key, val in props.items():
            vector = isinstance(val,tuple)
            literal = f'{{r: {val[0]}, g: {val[1]}, b: {val[2]}, a: {val[3]}}}' if vector else str(val)
            line = f'    - _{key}: {literal}'
            pattern = rf'^    - _{re.escape(key)}: .*?$'
            if re.search(pattern,saved,re.M): saved = re.sub(pattern,line,saved,flags=re.M)
            else:
                section = '    m_Colors:\n' if vector else '    m_Floats:\n'
                assert section in saved
                saved = saved.replace(section,section+line+'\n',1)
        path.write_text(saved)
        continue
    path.write_text(f'''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_Name: MAT_GPGP_Water_{name}
  m_Shader: {{fileID: -6465566751694194690, guid: {guid}, type: 3}}
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: 3000
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs: []
    m_Ints: []
    m_Floats:
{floats}    m_Colors:
{colors}''')
    meta(path,'NativeFormatImporter','  mainObjectFileID: 2100000\n')
print(f'Created water graph ({len(g["m_Nodes"])} nodes) and four materials.')
