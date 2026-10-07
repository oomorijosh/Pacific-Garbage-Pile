import json
import math
from pathlib import Path

text = Path('Assets/Ocean_Shader.shadergraph').read_text()
objects = []
decoder = json.JSONDecoder()
while text.strip():
    text = text.lstrip()
    obj, end = decoder.raw_decode(text)
    objects.append(obj)
    text = text[end:]
lookup = {o['m_ObjectId']: o for o in objects}
assert len(lookup) == len(objects)
def references(obj):
    if isinstance(obj, dict):
        if set(obj) == {'m_Id'} and isinstance(obj['m_Id'], str) and obj['m_Id']:
            assert obj['m_Id'] in lookup, obj
        for v in obj.values(): references(v)
    elif isinstance(obj, list):
        for v in obj: references(v)
references(objects)
g = objects[0]
incoming = {}
for edge in g['m_Edges']:
    a, b = edge['m_OutputSlot'], edge['m_InputSlot']
    key = (b['m_Node']['m_Id'], b['m_SlotId'])
    assert key not in incoming, 'Multiple connections to one input'
    incoming[key] = (a['m_Node']['m_Id'], a['m_SlotId'])

def evaluate(node_id, port, x, z, time):
    node = lookup[node_id]
    kind = node['m_Type'].split('.')[-1]
    def inp(p):
        if (node_id,p) in incoming:
            return evaluate(*incoming[node_id,p], x,z,time)
        s = next(lookup[r['m_Id']] for r in node['m_Slots'] if lookup[r['m_Id']]['m_Id'] == p)
        v = s['m_Value']
        return next(iter(v.values())) if isinstance(v,dict) else v
    if kind == 'PositionNode': return (x,0,z)
    if kind == 'TimeNode': return time
    if kind == 'PropertyNode': return lookup[node['m_Property']['m_Id']]['m_Value']
    if kind == 'SplitNode': return inp(0)[port-1]
    if kind == 'SineNode': return math.sin(inp(0))
    if kind == 'CosineNode': return math.cos(inp(0))
    if kind == 'MultiplyNode': return inp(0)*inp(1)
    if kind == 'AddNode':
        a,b=inp(0),inp(1)
        return tuple(i+j for i,j in zip(a,b)) if isinstance(a,tuple) else a+b
    if kind == 'CombineNode': return tuple(inp(i) for i in range(3))
    if kind == 'BlockNode': return inp(0)
    raise AssertionError(kind)

blocks = {o.get('m_SerializedDescriptor'):o['m_ObjectId'] for o in objects if 'm_SerializedDescriptor' in o}
for x,z,time in [(-5,-5,0), (0,0,1), (2.3,-1.4,17), (5,5,1000)]:
    p = evaluate(blocks['VertexDescription.Position'],0,x,z,time)
    assert abs(p[1] - .2*math.sin(x+z+time)) < 1e-8
    n = evaluate(blocks['VertexDescription.Normal'],0,x,z,time)
    t = evaluate(blocks['VertexDescription.Tangent'],0,x,z,time)
    eps=1e-5
    derivative=(.2*math.sin(x+eps+z+time)-.2*math.sin(x-eps+z+time))/(2*eps)
    assert abs(n[0]+derivative)<1e-8 and abs(n[2]+derivative)<1e-8
    assert abs(sum(a*b for a,b in zip(n,t)))<1e-8
mat=Path('Assets/Ocean_Material.mat').read_text()
assert 'guid: 067d4ca256134c34693376f6c3e7bee7' in mat
assert 'fileID: -6465566751694194690' in mat
assert '_Wave_Height: 0.2' in mat
scene=Path('Assets/Scenes/CC_LAVA_LED_WALL_SCENE.unity').read_text()
assert scene.count('--- !u!114 &923080678')==1
assert 'guid: 84da860b1ab44bb4b7b39e24977d01fa' in scene
print('PASS: graph references, unique inputs, wave/CPU equation, finite-difference normals, tangent orthogonality, material and scene wiring.')
