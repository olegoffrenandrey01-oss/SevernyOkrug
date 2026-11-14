"""Original procedural art. Run: blender --background --python-exit-code 1 --python Tools/art/build_v14_weapons.py
Exports are checked with Blender's independent native FBX importer. Renders are Blender previews, not Unity screenshots.
"""
import bpy, math, random, json, sys, os
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from art_png import mark
REPO=Path(__file__).resolve().parents[2]
ASSETS=REPO/'UnityProject/Assets/SevernyOkrug/Resources/SevernyModels'
UI=REPO/'UnityProject/Assets/SevernyOkrug/Resources/SevernyUI/V14'
OUT=Path(os.environ.get('WORKSPACE_ROOT',str(REPO)))/'output/v14-art'
for p in [ASSETS/'Textures',UI,OUT,REPO/'ArtSource']:p.mkdir(parents=True,exist_ok=True)
# Ubuntu Blender 5 / Python 3.14 needs eager annotation evaluation in its bundled FBX operators.
# Adapt the module in memory; do not patch the installed Blender files.
if sys.version_info >= (3,14):
 import ast, io_scene_fbx
 for cls in reversed(io_scene_fbx.classes):bpy.utils.unregister_class(cls)
 class EagerAnnotations(ast.NodeTransformer):
  def visit_ClassDef(self,node):
   self.generic_visit(node)
   body=[ast.parse('__annotations__ = {}').body[0]]
   for stmt in node.body:
    if isinstance(stmt,ast.AnnAssign) and isinstance(stmt.target,ast.Name) and stmt.value is None:
     body.append(ast.Assign(targets=[ast.Subscript(value=ast.Name(id='__annotations__',ctx=ast.Load()),slice=ast.Constant(stmt.target.id),ctx=ast.Store())],value=stmt.annotation))
    else:body.append(stmt)
   node.body=body;return node
 path=io_scene_fbx.__file__;tree=EagerAnnotations().visit(ast.parse(open(path).read()));ast.fix_missing_locations(tree)
 exec(compile(tree,path,'exec'),io_scene_fbx.__dict__)
 for cls in io_scene_fbx.classes:bpy.utils.register_class(cls)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;scene['generatedBy']='CodeRabbit; original v14 weapon art'
materials={}
for key,color,metal,smooth in [('V14Metal',(.12,.135,.145),True,.42),('V14Wood',(.28,.13,.05),False,.28),('V14Polymer',(.055,.065,.06),False,.18),('V14Steel',(.38,.4,.41),True,.55)]:
 mat=bpy.data.materials.new('M_'+key);mat.use_nodes=True
 node=mat.node_tree.nodes.get('Principled BSDF');node.inputs['Metallic'].default_value=.8 if metal else 0;node.inputs['Roughness'].default_value=1-smooth
 im=bpy.data.images.new('T_'+key+'_BC',256,256,alpha=False);rng=random.Random(key);pix=[]
 for y in range(256):
  for x in range(256):
   noise=rng.uniform(-.035,.035);scratch=.10 if metal and rng.random()<.002 else 0
   variation=noise+scratch+(math.sin(y*.12)*.025 if not metal else 0)
   pix.extend([max(.005,min(1,c+variation)) for c in color]+[1])
 im.pixels=pix;im.filepath_raw=str(ASSETS/'Textures'/('T_'+key+'_BC.png'));im.file_format='PNG';im.save()
 ms=bpy.data.images.new('T_'+key+'_MS',256,256,alpha=True);ms.colorspace_settings.name='Non-Color'
 ms.pixels=[.8 if metal else 0,1,0,smooth]*(256*256);ms.filepath_raw=str(ASSETS/'Textures'/('T_'+key+'_MS.png'));ms.file_format='PNG';ms.save()
 tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;mat.node_tree.links.new(tex.outputs['Color'],node.inputs['Base Color']);materials[key]=mat

def coord(v):return (v[0],-v[2],v[1])
def empty(name,parent=None,loc=(0,0,0)):
 anchor = name in ['Mag','Bolt','muzzle','sight','front']
 o=bpy.data.objects.new((parent.name+'_'+name) if anchor else name,None);bpy.context.collection.objects.link(o);o.parent=parent;o.location=coord(loc)
 if anchor:o['anchor']=name
 return o
def finish(o,name,parent,mat,bevel=.002):
 o.name=name;o.parent=parent;o.data.materials.append(materials[mat])
 bpy.context.view_layer.objects.active=o;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  mod=o.modifiers.new('Machined edge','BEVEL');mod.width=bevel;mod.segments=2;bpy.ops.object.modifier_apply(modifier=mod.name)
 for p in o.data.polygons:p.use_smooth=False
 return o
def box(name,loc,size,parent,mat='V14Metal',bevel=.002):
 bpy.ops.mesh.primitive_cube_add(size=1,location=coord(loc));o=bpy.context.object;o.scale=(size[0],size[2],size[1]);return finish(o,name,parent,mat,bevel)
def cyl(name,loc,radius,length,parent,mat='V14Metal',axis='z',vertices=20):
 bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=length,location=coord(loc));o=bpy.context.object
 if axis=='z':o.rotation_euler[0]=math.pi/2
 if axis=='x':o.rotation_euler[1]=math.pi/2
 return finish(o,name,parent,mat,.0008)
def join_parts(root):
 # Four body material groups plus separately animated magazine and bolt; retain named anchors.
 for parent in [root]+[o for o in root.children if o.type=='EMPTY' and o.get('anchor') in ['Mag','Bolt']]:
  for mat in materials.values():
   objects=[o for o in parent.children if o.type=='MESH' and o.data.materials[0]==mat]
   if not objects:continue
   if len(objects)==1:objects[0].name=parent.name+'_'+mat.name;continue
   bpy.ops.object.select_all(action='DESELECT')
   for o in objects:o.select_set(True)
   bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();objects[0].name=parent.name+'_'+mat.name

def weapon(tag):
 root=empty('W_'+tag);root['generatedBy']='CodeRabbit original procedural model';root['status']='Art preview; Unity hand/ADS validation pending'
 is_smg=tag=='PP9';is_rifle=tag=='SV762';end=.46 if is_smg else .88 if is_rifle else .78
 box('Receiver',(0,0,.08),(.052,.085,.28),root)
 box('Top rail',(0,.05,.1),(.032,.015,.24),root)
 for i in range(13):box('Rail tooth',(0,.06,-.005+i*.017),(.042,.005,.006),root,bevel=.0006)
 grip=box('Grip',(0,-.085,-.025),(.04,.115,.06),root,'V14Polymer');grip.rotation_euler[0]=-.18
 for i in range(7):box('Grip texture',(0,-.05-i*.011,-.057),(.043,.004,.006),root,'V14Polymer',.0007)
 box('Guard lower',(0,-.078,.065),(.014,.008,.1),root)
 box('Guard front',(0,-.052,.11),(.014,.06,.008),root)
 cyl('Trigger',(0,-.045,.054),.008,.016,root,'V14Steel','x',12)
 if is_smg:
  box('Folding stock beam',(0,-.008,-.24),(.022,.025,.27),root,'V14Metal')
  box('Butt',(0,-.045,-.38),(.035,.13,.025),root,'V14Polymer')
 else:
  box('Stock',(0,-.018,-.24),(.052,.085,.32),root,'V14Wood' if tag=='D12' else 'V14Polymer',.008)
  box('Recoil pad',(0,-.025,-.407),(.055,.1,.02),root,'V14Polymer',.004)
 box('Handguard',(0,-.014,.3),(.065,.065,.22 if is_smg else .25),root,'V14Polymer' if is_smg else 'V14Wood',.007)
 for side in [-1,1]:
  for i in range(9):box('Cooling slot',(side*.033,.012,.21+i*.022),(.004,.01,.01),root,'V14Metal',.001)
  for z in [-.03,.14,.3]:cyl('Screw',(side*.029,0,z),.006,.003,root,'V14Steel','x',12)
 cyl('Barrel',(0,0,(.37+end)/2),.013 if is_smg else .017,end-.37,root)
 cyl('Muzzle crown',(0,0,end),.019 if is_smg else .024,.035,root,'V14Steel')
 cyl('Bore shadow',(0,0,end+.018),.008,.001,root,'V14Polymer')
 if tag=='D12':
  cyl('Gas tube',(0,-.043,.46),.014,.32,root)
  for z in [.32,.6]:cyl('Tube support',(0,-.02,z),.025,.012,root)
 if is_rifle:
  # Iron sights remain usable; optic is a low-profile side detail, not a false magnification mechanic.
  box('Cheek rest',(0,.035,-.26),(.053,.024,.14),root,'V14Polymer',.006)
  cyl('Gas block',(0,0,.59),.023,.024,root)
 mag=empty('Mag',root,(0,-.04,.126));height=.18 if is_smg else .105
 box('Magazine',(0,-height*.5,0),(.031,.0+height,.062),mag,'V14Metal')
 for side in [-1,1]:
  for z in [-.016,.016]:box('Mag rib',(side*.016,-height*.48,z),(.003,height*.8,.003),mag,'V14Steel',.0004)
 box('Mag base',(0,-height,0),(.037,.012,.069),mag,'V14Polymer')
 bolt=empty('Bolt',root,(0,0,.09));box('Bolt slide',(.028,0,0),(.007,.025,.08),bolt,'V14Steel')
 cyl('Charging handle',(.043,0,-.02),.007,.024,bolt,'V14Metal','x')
 # Match the existing rifle sight height to preserve the shared arm/holder interface.
 for z in [.1452,end-.06]:
  box('Sight base',(0,.031,z),(.027,.02,.02),root)
  if z<.2:
   for side in [-1,1]:box('Rear notch',(side*.01,.049,z),(.006,.018,.008),root)
  else:box('Front post',(0,.047,z),(.004,.018,.006),root)
 empty('sight',root,(0,.0512,.1452));empty('front',root,(0,.0512,end-.06));empty('muzzle',root,(0,0,end+.02))
 join_parts(root);return root
roots=[weapon(t) for t in ['D12','SV762','PP9']]
report=[]
for root in roots:
 bpy.ops.object.select_all(action='DESELECT');root.select_set(True)
 for o in root.children_recursive:o.select_set(True)
 path=ASSETS/(root.name+'.fbx')
 anchors=[o for o in root.children_recursive if o.get('anchor')]
 for o in anchors:o.name=o['anchor']
 bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
 for o in anchors:o.name=root.name+'_'+o['anchor']
 report.append({'model':root.name,'meshes':sum(o.type=='MESH' for o in root.children_recursive),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in root.children_recursive if o.type=='MESH')})
bpy.ops.file.pack_all()
# Save editable sources before adding render-only lighting/cameras.
bpy.ops.wm.save_as_mainfile(filepath=str(REPO/'ArtSource/v14_weapons.blend'))
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=640;scene.render.resolution_y=320;scene.render.resolution_percentage=100
scene.render.film_transparent=True;scene.world.color=(.35,.35,.35)
scene.view_settings.view_transform='AgX'
bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=1.5
camera.location=(1.3,-.85,.55);target=Vector((0,-.18,0));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
for location,power,size in [((1,-1,2),180,2),((-1,0,1),100,2),((0,2,1),180,1)]:
 bpy.ops.object.light_add(type='AREA',location=location);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
for root,item in zip(roots,['d12','sv762','pp9']):
 for other in roots:
  for obj in [other]+list(other.children_recursive):obj.hide_render=other!=root
 for suffix in ['', '@empty']:
  for obj in root.children_recursive:
   if obj.parent and obj.parent.get('anchor')=='Mag':obj.hide_render=suffix!=''
  scene.render.filepath=str(UI/(item+suffix+'.png'));bpy.ops.render.render(write_still=True)
 for obj in root.children_recursive:obj.hide_render=False
# Side-by-side asset inspection sheet: clearly named as a Blender render in provenance and accompanying report.
scene.render.resolution_x=1400;scene.render.resolution_y=900;camera.data.ortho_scale=1.95
for idx,root in enumerate(roots):
 root.location.z=(1-idx)*.42
 for obj in [root]+list(root.children_recursive):obj.hide_render=False
camera.location=(1.8,-.85,.9);camera.rotation_euler=(Vector((0,-.18,0))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(OUT/'weapons_blender.png');bpy.ops.render.render(write_still=True)
# Independent native importer round-trip.
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
for entry in report:
 bpy.ops.wm.fbx_import(filepath=str(ASSETS/(entry['model']+'.fbx')))
 objects=list(bpy.context.selected_objects);names={o.name for o in objects}
 for anchor in ['muzzle','sight','front','Mag','Bolt']:assert anchor in names,(entry,anchor,names)
 assert sum(o.type=='MESH' for o in objects)==entry['meshes']
 assert all(len(o.data.materials)>0 for o in objects if o.type=='MESH')
 muzzle=bpy.data.objects['muzzle'].matrix_world.translation; sight=bpy.data.objects['sight'].matrix_world.translation; front=bpy.data.objects['front'].matrix_world.translation
 assert abs(sight.z-.0512)<.0001 and abs(front.z-sight.z)<.0001, (sight,front)
 assert muzzle.y<front.y<sight.y and abs(muzzle.x)<.0001, (muzzle,front,sight)
 entry['nativeFBXReimport']='passed';bpy.ops.object.delete(use_global=False)
manifest={'generatedBy':'CodeRabbit','notice':'Original art. Blender 5 renders, not Unity screenshots. Game integration and hand/ADS alignment still unverified.','models':report}
for p in list((ASSETS/'Textures').glob('T_V14*.png'))+list(UI.glob('*.png'))+[OUT/'weapons_blender.png']:mark(p)
(OUT/'art-validation.json').write_text(json.dumps(manifest,indent=2));print('ART_CHECKS_PASSED',json.dumps(report))
