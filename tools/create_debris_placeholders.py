"""One-time creation of editable square debris in the ocean scene."""
from pathlib import Path
import math
import random
import re
import uuid

path = Path('Assets/Scenes/CC_LAVA_LED_WALL_SCENE.unity')
scene = path.read_text()
assert 'm_Name: Floating Debris Placeholders' not in scene, 'Placeholders already exist'
parts = re.split(r'(?=^--- !u!)', scene, flags=re.M)
def document(file_id):
    return next(p for p in parts if re.match(r'--- !u!\d+ &' + str(file_id) + r'\n', p))
common = '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
def game_object(i, name, components):
    return f'--- !u!1 &{i}\nGameObject:\n'+common+'  serializedVersion: 6\n  m_Component:\n'+''.join(f'  - component: {{fileID: {c}}}\n' for c in components)+f'  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n'
def transform(i, go, parent, position, scale, yaw=0, children=()):
    x,y,z=position; sx,sy,sz=scale
    return f'--- !u!4 &{i}\nTransform:\n'+common+f'  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: {math.sin(yaw/2):.7f}, z: 0, w: {math.cos(yaw/2):.7f}}}\n  m_LocalPosition: {{x: {x:.5f}, y: {y:.5f}, z: {z:.5f}}}\n  m_LocalScale: {{x: {sx:.5f}, y: {sy:.5f}, z: {sz:.5f}}}\n  m_ConstrainProportionsScale: 0\n'+('  m_Children:\n'+''.join(f'  - {{fileID: {c}}}\n' for c in children) if children else '  m_Children: []\n')+f'  m_Father: {{fileID: {parent}}}\n  m_LocalEulerAnglesHint: {{x: 0, y: {math.degrees(yaw):.4f}, z: 0}}\n'

folder=Path('Assets/Ocean/Materials'); folder.mkdir(exist_ok=True)
materials=[]
for name,color in [('Ivory',(0.85,0.82,0.7)),('Orange',(0.95,0.32,0.08)),('Blue',(0.12,0.46,0.75)),('Yellow',(0.92,0.73,0.12))]:
    guid=uuid.uuid4().hex; materials.append(guid)
    r,g,b=color
    (folder/f'Debris_{name}.mat').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n'+common+f'  m_Name: Debris_{name}\n  m_Shader: {{fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}}\n  m_ValidKeywords: []\n  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 1\n  m_CustomRenderQueue: -1\n  stringTagMap: {{}}\n  disabledShaderPasses: []\n  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs: []\n    m_Ints: []\n    m_Floats:\n    - _Surface: 0\n    - _Blend: 0\n    - _SrcBlend: 1\n    - _DstBlend: 0\n    - _ZWrite: 1\n    - _Cull: 2\n    - _Metallic: 0\n    - _Smoothness: 0.25\n    m_Colors:\n    - _BaseColor: {{r: {r}, g: {g}, b: {b}, a: 1}}\n')
    (folder/f'Debris_{name}.mat.meta').write_text(f'fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 2100000\n')

ocean_match=re.search(r'--- !u!114 &(\d+)\nMonoBehaviour:\n(?:(?!--- !u!).)*?guid: 84da860b1ab44bb4b7b39e24977d01fa',scene,re.S)
added=[]
if ocean_match:
    ocean_id=int(ocean_match[1])
else:
    ocean_id=923080678
    scene=scene.replace('  - component: {fileID: 923080674}', '  - component: {fileID: 923080674}\n  - component: {fileID: 923080678}')
    added.append(f'--- !u!114 &{ocean_id}\nMonoBehaviour:\n'+common+'  m_GameObject: {fileID: 923080673}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: 84da860b1ab44bb4b7b39e24977d01fa, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: \n  subdivisions: 128\n  circulationSpeed: 0.4\n  convergenceRate: 0.01\n  accumulationRadius: 12\n')
collider=document(923080674)
scene=scene.replace(collider,collider.replace('m_Enabled: 1','m_Enabled: 0'))
root=1900000000
assert not re.search(r'&190000\d+',scene), 'Reserved IDs already used'
added += [game_object(root,'Floating Debris Placeholders',[root+1]), transform(root+1,root,0,(0,0,0),(1,1,1),children=[root+11+i*10 for i in range(60)])]
rng=random.Random(485)
renderer_template=document(923080675)
positions=[]
for index in range(60):
    base=root+10+index*10
    size=rng.uniform(.4,2.4)
    thickness=rng.uniform(.10,.24)
    # Rejection sampling keeps the initial squares from overlapping.
    for attempt in range(10000):
        angle=rng.uniform(0,math.tau); radius=32*math.sqrt(rng.random())
        x,z=radius*math.cos(angle),radius*math.sin(angle)
        if all(math.hypot(x-px,z-pz) > (size+ps)*.72+.3 for px,pz,ps in positions): break
    else: raise RuntimeError('Could not place debris')
    positions.append((x,z,size))
    offset=thickness*.25
    height=.2*math.sin(x/20+z/20)+offset
    added += [game_object(base,f'Debris Square {index+1:02d}',[base+1,base+2,base+3,base+4]),transform(base+1,base,root+1,(x,height,z),(size,thickness,size),rng.uniform(0,math.tau))]
    added.append(f'--- !u!33 &{base+2}\nMeshFilter:\n'+common+f'  m_GameObject: {{fileID: {base}}}\n  m_Mesh: {{fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}}\n')
    renderer=renderer_template.replace('&923080675',f'&{base+3}').replace('fileID: 923080673',f'fileID: {base}').replace('guid: feb8bd559863c8a48bc11f59139eb9a4',f'guid: {materials[index%4]}')
    added.append(renderer)
    added.append(f'--- !u!114 &{base+4}\nMonoBehaviour:\n'+common+f'  m_GameObject: {{fileID: {base}}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: 17f74c4a233e4f23ba4b6a477a4b1eb1, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n  ocean: {{fileID: {ocean_id}}}\n  waterlineOffset: {offset:.5f}\n  currentMultiplier: {rng.uniform(.8,1.2):.5f}\n  alignmentSpeed: 4\n')
scene=scene.replace('  m_Roots:\n','  m_Roots:\n  - {fileID: 1900000001}\n')
scene=scene.replace('--- !u!1660057539', ''.join(added)+'--- !u!1660057539')
ids=re.findall(r'^--- !u!\d+ &(\d+)',scene,re.M)
assert len(ids)==len(set(ids)), 'Duplicate serialized IDs'
assert scene.count('m_Name: Debris Square ')==60
assert scene.count(f'  ocean: {{fileID: {ocean_id}}}')==60
path.write_text(scene)
print('Created 60 square debris objects, four materials, and verified scene IDs and ocean references.')
