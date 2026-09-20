"""Build the ocean graph using the installed Shader Graph node serialization."""
import copy
import json
from pathlib import Path
import uuid

def decode(path):
    text = path.read_text(encoding='utf-8-sig')
    decoder = json.JSONDecoder()
    result = []
    while text.strip():
        text = text.lstrip()
        obj, end = decoder.raw_decode(text)
        result.append(obj)
        text = text[end:]
    return result

templates = {}
for path in Path('Library/PackageCache').rglob('*.shadergraph'):
    try:
        data = decode(path)
        lookup = {o['m_ObjectId']: o for o in data}
        for obj in data:
            key = obj['m_Type'].split('.')[-1]
            if key == 'BlockNode': key = obj['m_SerializedDescriptor']
            if key not in templates: templates[key] = (obj, lookup)
    except (ValueError, KeyError):
        continue

uid = lambda: uuid.uuid4().hex
objects = []
def clone(key):
    obj = copy.deepcopy(templates[key][0])
    obj['m_ObjectId'] = uid()
    objects.append(obj)
    return obj

g = clone('GraphData')
for key in ['m_Properties', 'm_Keywords', 'm_Dropdowns', 'm_CategoryData', 'm_Nodes', 'm_GroupDatas', 'm_StickyNoteDatas', 'm_Edges', 'm_ActiveTargets']:
    g[key] = []
g['m_Path'] = 'Shader Graphs'
g['m_GraphPrecision'] = 1
g['m_PreviewData'] = {'serializedMesh': {'m_SerializedMesh': '', 'm_Guid': ''}, 'preventRotation': False}
g['m_VertexContext'] = {'m_Position': {'x': 1550, 'y': 0}, 'm_Blocks': []}
g['m_FragmentContext'] = {'m_Position': {'x': 1550, 'y': 550}, 'm_Blocks': []}

nodes = {}
def node(label, key, x, y):
    n = clone(key)
    n['m_Group'] = {'m_Id': ''}
    n['m_DrawState'] = {'m_Expanded': True, 'm_Position': {'serializedVersion': '2', 'x': x, 'y': y, 'width': 208, 'height': 200}}
    n['m_CustomColors'] = {'m_SerializableColors': []}
    lookup = templates[key][1]
    for ref in n['m_Slots']:
        slot = copy.deepcopy(lookup[ref['m_Id']])
        slot['m_ObjectId'] = uid()
        ref['m_Id'] = slot['m_ObjectId']
        objects.append(slot)
    g['m_Nodes'].append({'m_Id': n['m_ObjectId']})
    nodes[label] = n
    return n

def slot(label, port):
    refs = {r['m_Id'] for r in nodes[label]['m_Slots']}
    return next(o for o in objects if o['m_ObjectId'] in refs and o['m_Id'] == port)

def value(label, port, val):
    s = slot(label, port)
    if isinstance(s['m_Value'], dict):
        for k in s['m_Value']: s['m_Value'][k] = val
    else: s['m_Value'] = val

def edge(a, out, b, inp):
    g['m_Edges'].append({'m_OutputSlot': {'m_Node': {'m_Id': nodes[a]['m_ObjectId']}, 'm_SlotId': out},
                         'm_InputSlot': {'m_Node': {'m_Id': nodes[b]['m_ObjectId']}, 'm_SlotId': inp}})

for label, descriptor in [('positionOut','VertexDescription.Position'), ('normalOut','VertexDescription.Normal'), ('tangentOut','VertexDescription.Tangent'), ('colorOut','SurfaceDescription.BaseColor'), ('rippleOut','SurfaceDescription.NormalTS'), ('smoothOut','SurfaceDescription.Smoothness'), ('alphaOut','SurfaceDescription.Alpha'), ('metalOut','SurfaceDescription.Metallic')]:
    n = node(label, descriptor, 1550, 0)
    context = 'm_VertexContext' if descriptor.startswith('Vertex') else 'm_FragmentContext'
    g[context]['m_Blocks'].append({'m_Id': n['m_ObjectId']})
value('metalOut',0,0)

for i, (name, val) in enumerate([('Water_Color', {'r':.023529412,'g':.27058825,'b':.32941177,'a':1}), ('Wave_Height',.2), ('Water_Smoothness',.85), ('Water_Alpha',.82), ('Ripple_Speed',.15)]):
    p = clone('ColorShaderProperty' if isinstance(val, dict) else 'Vector1ShaderProperty')
    p.update(m_Name=name, m_DefaultReferenceName='_'+name, m_OverrideReferenceName='_'+name, m_RefNameGeneratedByDisplayName=name, m_Value=val, m_GeneratePropertyBlock=True, m_Hidden=False)
    p['m_Guid'] = {'m_GuidSerialized': str(uuid.uuid4())}
    if not isinstance(val,dict): p['m_FloatType'] = 0
    g['m_Properties'].append({'m_Id': p['m_ObjectId']})
    n = node(name,'PropertyNode',-600, i*220)
    n['m_Property'] = {'m_Id': p['m_ObjectId']}
    s = slot(name,0)
    s.update(m_Type='UnityEditor.ShaderGraph.Vector4MaterialSlot' if isinstance(val,dict) else 'UnityEditor.ShaderGraph.Vector1MaterialSlot', m_Value={'x':0,'y':0,'z':0,'w':0} if isinstance(val,dict) else 0, m_DefaultValue={'x':0,'y':0,'z':0,'w':0} if isinstance(val,dict) else 0, m_DisplayName=name, m_ShaderOutputName=name)

for label,key,x,y in [('position','PositionNode',-1000,0),('split','SplitNode',-750,0),('xz','AddNode',-400,0),('time','TimeNode',-750,500),('phase','AddNode',-150,0),('sin','SineNode',100,0),('height','MultiplyNode',350,0),('offset','CombineNode',600,0),('displaced','AddNode',900,0),('cos','CosineNode',100,350),('slope','MultiplyNode',350,350),('negative','MultiplyNode',600,350),('normal','CombineNode',900,350),('tangent','CombineNode',1150,350)]:
    node(label,key,x,y)
nodes['position']['m_Space']=0
nodes['position']['m_PositionSource']=0
for label in ['offset','normal','tangent']:
    for port in range(4): value(label,port,0)
value('normal',1,1)
value('tangent',0,1)
value('negative',1,-1)
for a,o,b,i in [('position',0,'split',0),('split',1,'xz',0),('split',3,'xz',1),('xz',2,'phase',0),('time',0,'phase',1),('phase',2,'sin',0),('sin',1,'height',0),('Wave_Height',0,'height',1),('height',2,'offset',1),('position',0,'displaced',0),('offset',5,'displaced',1),('displaced',2,'positionOut',0),('phase',2,'cos',0),('cos',1,'slope',0),('Wave_Height',0,'slope',1),('slope',2,'negative',0),('negative',2,'normal',0),('negative',2,'normal',2),('normal',5,'normalOut',0),('slope',2,'tangent',1),('tangent',5,'tangentOut',0),('Water_Color',0,'colorOut',0),('Water_Smoothness',0,'smoothOut',0),('Water_Alpha',0,'alphaOut',0)]: edge(a,o,b,i)
node('rippleTime','MultiplyNode',-750,1000)
edge('time',0,'rippleTime',0)
edge('Ripple_Speed',0,'rippleTime',1)
for index, speed, scale in [(1,.05,500),(2,-.03,150)]:
    s=str(index)
    node('scroll'+s,'MultiplyNode',-400,800+index*300)
    value('scroll'+s,1,speed)
    node('uv'+s,'TilingAndOffsetNode',-100,800+index*300)
    slot('uv'+s,0)['m_Channel'] = 0
    value('uv'+s,1,1)
    node('noise'+s,'NoiseNode',200,800+index*300)
    value('noise'+s,1,scale)
    edge('rippleTime',2,'scroll'+s,0); edge('scroll'+s,2,'uv'+s,2); edge('uv'+s,3,'noise'+s,0)
node('noiseSum','AddNode',500,1200)
node('rippleNormal','NormalFromHeightNode',800,1200)
nodes['rippleNormal']['m_OutputSpace']=0
value('rippleNormal',2,.03)
edge('noise1',2,'noiseSum',0); edge('noise2',2,'noiseSum',1)
edge('noiseSum',2,'rippleNormal',0); edge('rippleNormal',1,'rippleOut',0)
target=clone('UniversalTarget'); sub=clone('UniversalLitSubTarget')
target.update(m_Datas=[],m_ActiveSubTarget={'m_Id':sub['m_ObjectId']},m_SurfaceType=1,m_ZTestMode=4,m_ZWriteControl=0,m_AlphaMode=0,m_AlphaClip=False,m_CastShadows=False,m_ReceiveShadows=True,m_AllowMaterialOverride=False,m_RenderFace=2)
sub.update(m_WorkflowMode=1,m_NormalDropOffSpace=0,m_ClearCoat=False,m_BlendModePreserveSpecular=True)
g['m_ActiveTargets']=[{'m_Id':target['m_ObjectId']}]
category = clone('CategoryData')
category['m_Name'] = 'Ocean'
category['m_ChildObjectList'] = copy.deepcopy(g['m_Properties'])
g['m_CategoryData'] = [{'m_Id':category['m_ObjectId']}]

# Validate every serialized reference and every edge endpoint before replacing the asset.
lookup={o['m_ObjectId']:o for o in objects}
assert len(lookup)==len(objects)
for e in g['m_Edges']:
    for key in ['m_OutputSlot','m_InputSlot']:
        endpoint=e[key]; n=lookup[endpoint['m_Node']['m_Id']]
        assert any(lookup[r['m_Id']]['m_Id']==endpoint['m_SlotId'] for r in n['m_Slots'])
Path('Assets/Ocean_Shader.shadergraph').write_text('\n\n'.join(json.dumps(o,indent=4) for o in objects)+'\n',encoding='utf-8')
print(f'Validated {len(g["m_Nodes"])} nodes and {len(g["m_Edges"])} connections.')
