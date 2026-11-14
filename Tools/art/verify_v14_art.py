#!/usr/bin/env python3
"""Validate generated raster payloads, provenance, alpha variants and Resources cross-references."""
import hashlib, json, re, struct, sys, zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];RES=ROOT/'UnityProject/Assets/SevernyOkrug/Resources'

def png(path):
 raw=path.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n';off=8;chunks=[];compressed=b'';provenance=False
 while off<len(raw):
  size=struct.unpack_from('>I',raw,off)[0];kind=raw[off+4:off+8];data=raw[off+8:off+8+size];crc=struct.unpack_from('>I',raw,off+8+size)[0]
  assert zlib.crc32(kind+data)&0xffffffff==crc,(path,kind,'CRC');chunks.append(kind)
  if kind==b'IHDR':w,h,depth,color,comp,filt,interlace=struct.unpack('>IIBBBBB',data)
  if kind==b'IDAT':compressed+=data
  if kind==b'tEXt' and data.startswith(b'GeneratedBy\0CodeRabbit'):provenance=True
  off+=12+size
 assert off==len(raw) and chunks[0]==b'IHDR' and chunks[-1]==b'IEND' and provenance,path
 assert depth==8 and color in [2,6] and comp==filt==interlace==0,(path,depth,color)
 channels=4 if color==6 else 3;stride=w*channels;data=zlib.decompress(compressed);assert len(data)==(stride+1)*h
 decoded=bytearray();previous=bytearray(stride)
 for y in range(h):
  f=data[y*(stride+1)];row=bytearray(data[y*(stride+1)+1:(y+1)*(stride+1)])
  for x in range(stride):
   a=row[x-channels] if x>=channels else 0;b=previous[x];c=previous[x-channels] if x>=channels else 0
   if f==1:predictor=a
   elif f==2:predictor=b
   elif f==3:predictor=(a+b)//2
   elif f==4:
    p=a+b-c;pa,pb,pc=abs(p-a),abs(p-b),abs(p-c);predictor=a if pa<=pb and pa<=pc else b if pb<=pc else c
   else:assert f==0;predictor=0
   row[x]=(row[x]+predictor)&255
  decoded.extend(row);previous=row
 return w,h,channels,decoded
files=[]
for path in sorted(list((RES/'SevernyModels/Textures').glob('T_V14*.png'))+list((RES/'SevernyUI/V14').glob('*.png'))+list((RES/'V14Art').glob('*.png'))):
 w,h,c,pixels=png(path)
 if path.parent.name=='V14':
  assert (w,h,c)==(640,320,4);alpha=pixels[3::4];assert min(alpha)==0 and max(alpha)>200
 if path.parent.name=='V14Art':
  assert (w,h,c)==(512,512,4);alpha=pixels[3::4];assert min(alpha)==0 and max(alpha)>80
 files.append({'path':path.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'dimensions':[w,h],'channels':c})
for item in ['d12','sv762','pp9']:
 a=png(RES/'SevernyUI/V14'/(item+'.png'))[3];b=png(RES/'SevernyUI/V14'/(item+'@empty.png'))[3]
 assert a!=b and sum(a[3::4])>sum(b[3::4]),item
for tag in ['D12','SV762','PP9']:
 path=RES/'SevernyModels'/('W_'+tag+'.fbx');assert path.read_bytes().startswith(b'Kaydara FBX Binary');files.append({'path':path.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
catalog=(ROOT/'UnityProject/Assets/SevernyOkrug/Scripts/Models/V14WeaponArt.cs').read_text()
for texture in re.findall(r'(?:bc|ms) = "([^\"]+)"',catalog):assert (RES/'SevernyModels/Textures'/(texture+'.png')).is_file(),texture
assert len(files)==20,len(files)
report={'generatedBy':'CodeRabbit','checks':'PNG CRC, headers, inflation, RGBA coverage, magazine icon variants, FBX binary signatures, material resource paths. Unity imports remain unverified.','files':files}
if len(sys.argv)>1:Path(sys.argv[1]).write_text(json.dumps(report,indent=2))
print('ART_DATA_CHECKS_PASSED',len(files),'assets')
