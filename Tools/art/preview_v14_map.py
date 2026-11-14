"""Offline Blender visualization of supplied map geometry/materials and v14 wear.
This is not a Unity screenshot or a rendering/performance test of the game.
Run: blender --background --python-exit-code 1 --python Tools/art/preview_v14_map.py
"""
import bpy, struct, json, math, random, sys, os
from pathlib import Path
import numpy as np
from mathutils import Matrix, Vector, Quaternion
sys.path.insert(0,str(Path(__file__).resolve().parent))
from art_png import mark
ROOT=Path(__file__).resolve().parents[2];KIT=ROOT/'UnityProject/Assets/SevernyOkrug/Resources/PolygonKit'
OUT=Path(os.environ.get('WORKSPACE_ROOT',str(ROOT)))/'output/v14-art';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;scene['generatedBy']='CodeRabbit; Blender visualization only; not Unity'
doc=json.loads((KIT/'pk_map.json').read_text());raw=(KIT/'pk_geo.bytes').read_bytes();off=4

def i32():
 global off
 v=struct.unpack_from('<i',raw,off)[0];off+=4;return v
assert i32()==1;count=i32()
def array(dtype,count,pad=False):
 global off
 a=np.frombuffer(raw,dtype=dtype,count=count,offset=off);size=a.nbytes;off+=(size+3)&~3 if pad else size;return a
texpaths={p.stem:p for p in (KIT/'Textures').iterdir() if p.suffix!='.meta'}

def image(name,linear=False):
 if not name or name not in texpaths:return None
 im=bpy.data.images.load(str(texpaths[name]),check_existing=True)
 if linear:im.colorspace_settings.name='Non-Color'
 return im

def material(d):
 m=bpy.data.materials.new(d['name']);m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
 p.inputs['Roughness'].default_value=max(.08,1-d['smooth']);p.inputs['Metallic'].default_value=d['metal']
 im=image(d['baseMap'])
 if im:
  t=n.new('ShaderNodeTexImage');t.image=im
  mult=n.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=1;mult.inputs[2].default_value=tuple(d['tint']);l.new(t.outputs['Color'],mult.inputs[1]);l.new(mult.outputs[0],p.inputs['Base Color'])
  if d['mode']!='opaque':l.new(t.outputs['Alpha'],p.inputs['Alpha'])
 im=image(d['normalMap'],True)
 if im:
  t=n.new('ShaderNodeTexImage');t.image=im;normal=n.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=d['nscale'];l.new(t.outputs['Color'],normal.inputs['Color']);l.new(normal.outputs[0],p.inputs['Normal'])
 im=image(d['maskMap'],True)
 if im:
  t=n.new('ShaderNodeTexImage');t.image=im;scale=n.new('ShaderNodeMath');scale.operation='MULTIPLY';scale.inputs[1].default_value=d['smooth'];l.new(t.outputs['Alpha'],scale.inputs[0]);inv=n.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;l.new(scale.outputs[0],inv.inputs[1]);l.new(inv.outputs[0],p.inputs['Roughness'])
 if len(d.get('emit',[]))>=3:p.inputs['Emission Color'].default_value=tuple(d['emit'][:3])+ (1,)
 return m
mats=[material(d) for d in doc['materials']];meshes=[]
for _ in range(count):
 ln=i32();name=raw[off:off+ln].decode();off+=(ln+3)&~3;flags=i32();v=i32();ns=i32();pos=array('<f4',v*3).reshape((-1,3)).copy()
 uv=None
 if not flags&4:array('<i2',v*3,True);uv=array('<f4',v*2).reshape((-1,2))
 faces=[];mi=[]
 for sub in range(ns):
  mid=i32();ic=i32();inds=array('<u2' if v<=65535 else '<u4',ic,v<=65535)
  faces.append(inds.astype(np.int32));mi.extend([mid]*(ic//3))
 if flags&4:meshes.append(None);continue
 pos=pos[:,[0,2,1]];pos[:,1]*=-1
 indices=np.concatenate(faces);num=len(indices)//3
 mesh=bpy.data.meshes.new(name);mesh.vertices.add(v);mesh.vertices.foreach_set('co',pos.ravel());mesh.loops.add(len(indices));mesh.loops.foreach_set('vertex_index',indices);mesh.polygons.add(num);mesh.polygons.foreach_set('loop_start',np.arange(0,len(indices),3,dtype=np.int32));mesh.polygons.foreach_set('loop_total',np.full(num,3,dtype=np.int32))
 used=sorted(set(mi));lookup={x:i for i,x in enumerate(used)}
 for mid in used:mesh.materials.append(mats[mid])
 mesh.polygons.foreach_set('material_index',[lookup[x] for x in mi]);mesh.uv_layers.new(name='UVMap');mesh.uv_layers[0].data.foreach_set('uv',uv[indices].ravel());mesh.update();meshes.append(mesh)
print('MAP_GEOMETRY_PARSED',count,flush=True)
objects=[]
def obj(name,mesh):
 if mesh is None:return None
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);objects.append(o);return o
for i in doc['statics']:obj(meshes[i].name if meshes[i] else 'collision',meshes[i])
C=Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)));Ci=C.inverted()
for it in doc['instances']:
 proto=doc['protos'][it['p']];o=obj(proto['name'],meshes[proto['mesh']])
 if o is None:continue
 x,y,z,w=it['rot'];o.matrix_world=C@Matrix.Translation(Vector(it['pos']))@Quaternion((w,x,y,z)).to_matrix().to_4x4()@Matrix.Diagonal(Vector(it['scl']+[1]))@Ci
bpy.context.view_layer.update()
print('MAP_OBJECTS',len(objects),flush=True)
# Same material categories, normal/area filters, world projection and 1600 triangle budget as game code.
rng=random.Random(140014);selected=[];eligible=0
for o in objects:
 m=o.matrix_world
 for p in o.data.polygons:
  mat=o.data.materials[p.material_index];name=mat.name.lower();ground=any(x in name for x in ['asphalt','paving','slabs']);metal=any(x in name for x in ['corr','garagedoor']);wall=any(x in name for x in ['concrete','brick','cladding'])
  if not (ground or metal or wall):continue
  a,b,c=[m@o.data.vertices[i].co for i in p.vertices];normal=(b-a).cross(c-a)
  if normal.length<.1:continue
  normal.normalize()
  if (ground and normal.z<.9) or (not ground and abs(normal.z)>.25):continue
  face=(a,b,c,normal,'asphalt' if ground else 'metal' if metal else 'concrete');eligible+=1
  if len(selected)<1600:selected.append(face)
  else:
   slot=rng.randrange(eligible)
   if slot<1600:selected[slot]=face
wear=[]
for kind,scale in [('asphalt',8),('concrete',6),('metal',4)]:
 m=bpy.data.materials.new('v14_'+kind);m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF');t=n.new('ShaderNodeTexImage');t.image=bpy.data.images.load(str(ROOT/'UnityProject/Assets/SevernyOkrug/Resources/V14Art'/('wear_'+kind+'.png')))
 l.new(t.outputs['Color'],p.inputs['Base Color']);a=n.new('ShaderNodeMath');a.operation='MULTIPLY';a.inputs[1].default_value=.22;l.new(t.outputs['Alpha'],a.inputs[0]);l.new(a.outputs[0],p.inputs['Alpha']);p.inputs['Roughness'].default_value=.9
 vertices=[];faces=[];uv=[]
 for a,b,c,normal,k in selected:
  if k!=kind:continue
  faces.append(tuple(range(len(vertices),len(vertices)+3)))
  for v in [a,b,c]:
   vertices.append(tuple(v+normal*.006));uv.append((v.x/scale,-v.y/scale) if k=='asphalt' else (-v.y/scale,v.z/scale) if abs(normal.x)>abs(normal.y) else (v.x/scale,v.z/scale))
 mesh=bpy.data.meshes.new('v14_'+kind);mesh.from_pydata(vertices,[],faces);mesh.materials.append(m);mesh.uv_layers.new(name='UVMap');mesh.uv_layers[0].data.foreach_set('uv',np.array(uv).ravel());o=bpy.data.objects.new('v14_'+kind,mesh);bpy.context.collection.objects.link(o);wear.append(o)
scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True;scene.cycles.max_bounces=4;scene.cycles.transparent_max_bounces=8
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.56,.62,.72,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.55
bpy.ops.object.light_add(type='SUN');sun=bpy.context.object;sun.data.energy=1.4;sun.data.angle=.035;sun.rotation_euler=Vector((-.4096,.7094,-.5736)).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.lens=22
# Overview of the actual map, plus the existing R3 photographic anchor.
for label,location,target in [('volna_overview',(155,-42,70),(0,-100,0)),('volna_street',(60,56,1.7),(60,-30,3))]:
 camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
 for variant in ['before','after']:
  for o in wear:o.hide_render=variant=='before'
  path=OUT/(label+'_'+variant+'_blender.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True);mark(path)
(OUT/'map-preview.json').write_text(json.dumps({'generatedBy':'CodeRabbit','notice':'Blender approximation of actual supplied map, not Unity. Lighting/shaders/sampling differ from game. No FPS claims.','geometryMeshes':count,'objects':len(objects),'eligibleWearFaces':eligible,'selectedWearFaces':len(selected),'wearBudget':1600},indent=2))
print('MAP_PREVIEW_COMPLETE',len(objects),eligible,len(selected),flush=True)
