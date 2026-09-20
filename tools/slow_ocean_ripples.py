"""Add a ripple speed multiplier without replacing existing graph settings."""
import copy
import json
from pathlib import Path
import uuid

path = Path('Assets/Ocean_Shader.shadergraph')
text = path.read_text()
objects = []
decoder = json.JSONDecoder()
while text.strip():
    text = text.lstrip()
    obj, end = decoder.raw_decode(text)
    objects.append(obj)
    text = text[end:]
graph = objects[0]
lookup = {o['m_ObjectId']: o for o in objects}
assert not any(o.get('m_Name') == 'Ripple_Speed' for o in objects)
def clone(obj):
    result = copy.deepcopy(obj)
    result['m_ObjectId'] = uuid.uuid4().hex
    objects.append(result)
    return result
def clone_node(obj):
    result = clone(obj)
    for ref in result['m_Slots']:
        ref['m_Id'] = clone(lookup[ref['m_Id']])['m_ObjectId']
    graph['m_Nodes'].append({'m_Id': result['m_ObjectId']})
    return result
source = next(o for o in objects if o.get('m_Name') == 'Water_Alpha')
prop = clone(source)
prop.update(m_Name='Ripple_Speed', m_DefaultReferenceName='_Ripple_Speed', m_OverrideReferenceName='_Ripple_Speed', m_RefNameGeneratedByDisplayName='Ripple_Speed', m_Value=.15)
prop['m_Guid'] = {'m_GuidSerialized': str(uuid.uuid4())}
graph['m_Properties'].append({'m_Id':prop['m_ObjectId']})
for o in objects:
    if o['m_Type'].endswith('.CategoryData'):
        o['m_ChildObjectList'].append({'m_Id':prop['m_ObjectId']})
property_node = clone_node(next(o for o in objects if o.get('m_Property',{}).get('m_Id') == source['m_ObjectId']))
property_node['m_Property'] = {'m_Id':prop['m_ObjectId']}
property_node['m_DrawState']['m_Position'].update(x=-1050,y=1050)
uv_ids = {o['m_ObjectId'] for o in objects if o['m_Type'].endswith('.TilingAndOffsetNode')}
scroll_ids = {e['m_OutputSlot']['m_Node']['m_Id'] for e in graph['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id'] in uv_ids and e['m_InputSlot']['m_SlotId']==2}
edges = [e for e in graph['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id'] in scroll_ids and e['m_InputSlot']['m_SlotId']==0]
assert len(edges)==2
time_output = copy.deepcopy(edges[0]['m_OutputSlot'])
assert all(e['m_OutputSlot']==time_output for e in edges)
multiply = clone_node(lookup[next(iter(scroll_ids))])
multiply['m_DrawState']['m_Position'].update(x=-750,y=1050)
for e in edges:
    e['m_OutputSlot'] = {'m_Node':{'m_Id':multiply['m_ObjectId']},'m_SlotId':2}
def input_slot(port): return {'m_Node':{'m_Id':multiply['m_ObjectId']},'m_SlotId':port}
graph['m_Edges'].extend([
    {'m_OutputSlot':time_output,'m_InputSlot':input_slot(0)},
    {'m_OutputSlot':{'m_Node':{'m_Id':property_node['m_ObjectId']},'m_SlotId':0},'m_InputSlot':input_slot(1)}])
path.write_text('\n\n'.join(json.dumps(o,indent=4) for o in objects)+'\n')
print('Both ripple layers now run at 15% of their previous speed.')
