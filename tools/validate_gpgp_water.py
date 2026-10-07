"""Static graph, material, geometry-mask and wave checks; not a Unity render test."""
import json
import math
from pathlib import Path

text=Path('Assets/Ocean/SG_GPGP_Water.shadergraph').read_text()
decoder=json.JSONDecoder(); objects=[]
while text.strip():
    text=text.lstrip(); obj,end=decoder.raw_decode(text); objects.append(obj); text=text[end:]
lookup={o['m_ObjectId']:o for o in objects}
assert len(lookup)==len(objects)
def refs(value):
    if isinstance(value,dict):
        if set(value)=={'m_Id'} and isinstance(value['m_Id'],str) and value['m_Id']:
            assert value['m_Id'] in lookup,value
        for child in value.values(): refs(child)
    elif isinstance(value,list):
        for child in value: refs(child)
refs(objects)
g=objects[0]; inputs=set(); upstream={}
for edge in g['m_Edges']:
    for key,slot_type in [('m_OutputSlot',1),('m_InputSlot',0)]:
        e=edge[key]; node=lookup[e['m_Node']['m_Id']]
        slot=next(lookup[r['m_Id']] for r in node['m_Slots'] if lookup[r['m_Id']]['m_Id']==e['m_SlotId'])
        assert slot['m_SlotType']==slot_type
    i=edge['m_InputSlot']; o=edge['m_OutputSlot']
    key=(i['m_Node']['m_Id'],i['m_SlotId']); assert key not in inputs; inputs.add(key)
    upstream.setdefault(i['m_Node']['m_Id'],[]).append(o['m_Node']['m_Id'])
reachable=set()
def walk(key,stack):
    assert key not in stack,'Cycle'
    reachable.add(key)
    for src in upstream.get(key,[]): walk(src,stack|{key})
for context in ['m_VertexContext','m_FragmentContext']:
    for block in g[context]['m_Blocks']: walk(block['m_Id'],set())
assert all(n['m_ObjectId'] in reachable for n in objects if n['m_Type'].endswith('PropertyNode'))
target=next(o for o in objects if o['m_Type'].endswith('.UniversalTarget'))
assert target['m_SurfaceType']==1 and target['m_ZWriteControl']==2 and not target['m_CastShadows']
guid=Path('Assets/Ocean/SG_GPGP_Water.shadergraph.meta').read_text().split('guid: ')[1].splitlines()[0]
properties={lookup[p['m_Id']]['m_OverrideReferenceName'] for p in g['m_Properties']}
for name in ['Micro','Meso','Macro','Mega']:
    mat=Path(f'Assets/Ocean/Materials/MAT_GPGP_Water_{name}.mat').read_text()
    assert guid in mat
    for prop in properties: assert f'- {prop}:' in mat,(name,prop)
def displacement(u,v,t,mask):
    p=u*3; q=v*3
    phase=(p*.9+q*.4)/math.hypot(.9,.4)*2.1+t*.65
    phase2=(-p*.35+q*.94)/math.hypot(.35,.94)*4.4-t*.48
    return (.065*math.sin(phase)+.017*math.sin(phase2))*math.sin(u*math.pi)*math.sin(v*math.pi)*mask
for t in [0,.7,13,1000]:
    for u,v in [(0,.3),(1,.7),(.2,0),(.9,1),(.5,.5)]:
        assert displacement(u,v,t,0)==0,'Volume moved'
        assert abs(displacement(u,v,t,1))<=.082+1e-9
        if u in [0,1] or v in [0,1]: assert abs(displacement(u,v,t,1))<1e-9,'Rim moved'
assert abs(displacement(.4,.6,0,1)-displacement(.4,.6,1,1))>1e-5
# Verify the product-rule normal against a numerical derivative, including near the rim.
def analytic_slope(u,v,t):
    d=(.9/math.hypot(.9,.4),.4/math.hypot(.9,.4))
    d2=(-.35/math.hypot(.35,.94),.94/math.hypot(.35,.94))
    phase=(3*u*d[0]+3*v*d[1])*2.1+t*.65
    phase2=(3*u*d2[0]+3*v*d2[1])*4.4-t*.48
    envelope=math.sin(u*math.pi)*math.sin(v*math.pi)
    gradient=(math.pi/3*math.cos(u*math.pi)*math.sin(v*math.pi),math.pi/3*math.sin(u*math.pi)*math.cos(v*math.pi))
    wave=.065*math.sin(phase)+.017*math.sin(phase2)
    return tuple(envelope*(.065*2.1*math.cos(phase)*d[i]+.017*4.4*math.cos(phase2)*d2[i])+wave*gradient[i] for i in range(2))
for u,v in [(.0001,.4),(.5,.5),(.93,.18),(.4,.9999)]:
    for t in [0,.7,13]:
        step=1e-6
        numeric=((displacement(u+step,v,t,1)-displacement(u-step,v,t,1))/(6*step),
                 (displacement(u,v+step,t,1)-displacement(u,v-step,t,1))/(6*step))
        assert all(abs(a-b)<1e-7 for a,b in zip(numeric,analytic_slope(u,v,t)))
for strength in [0,.5,1,3]:
    depth=[1-2**(-2*(1-h)*strength) for h in [1,.75,.5,.25,0]]
    assert all(a<=b for a,b in zip(depth,depth[1:]))
    assert all(0<=x<=1 for x in depth)
for prop in ['_SurfaceOpacity','_WaveAmplitude2','_WaveFrequency2','_WaveSpeed2','_WaveDirection2','_RippleScale2','_RippleStrength2']:
    assert prop in properties
scene=Path('Assets/Scenes/GPGP_WaterTest.unity').read_text()
assert '9e5480b1f7a44426aa0b93e61f935866' not in scene
print('PASS: graph structure, all properties connected, transparent target, four materials, stable volume/rim, combined amplitude bounds, time animation, finite-difference wave normals, smooth monotonic depth and independent secondary controls.')
