#!/usr/bin/env python3
"""Original seamless surface wear. No third-party imagery; deterministic, stdlib only."""
import math, random
from pathlib import Path
from art_png import write
ROOT=Path(__file__).resolve().parents[2]
DEST=ROOT/'UnityProject/Assets/SevernyOkrug/Resources/V14Art'
DEST.mkdir(parents=True,exist_ok=True)
SIZE=512

def texture(kind):
 rng=random.Random('severny-v14-'+kind);pixels=bytearray(SIZE*SIZE*4)
 def dot(x,y,radius,color,alpha,soft=False):
  for iy in range(math.floor(y-radius),math.ceil(y+radius)+1):
   for ix in range(math.floor(x-radius),math.ceil(x+radius)+1):
    d=math.hypot(ix+.5-x,iy+.5-y)/radius
    if d>=1:continue
    a=alpha*((1-d)**1.4 if soft else min(1,(1-d)*radius));idx=((iy%SIZE)*SIZE+ix%SIZE)*4
    old=pixels[idx+3]/255;out=a+old*(1-a)
    if not out:continue
    for c in range(3):pixels[idx+c]=int((color[c]*a+pixels[idx+c]*old*(1-a))/out)
    pixels[idx+3]=min(255,int(out*255))
 def line(a,b,width,color,alpha):
  length=math.dist(a,b);n=max(1,math.ceil(length*2))
  for i in range(n+1):dot(a[0]+(b[0]-a[0])*i/n,a[1]+(b[1]-a[1])*i/n,width,color,alpha)
 if kind=='asphalt':
  for _ in range(14):
   x,y=rng.uniform(0,SIZE),rng.uniform(0,SIZE)
   dot(x,y,rng.uniform(18,60),(61,54,41),.45,True)
   dot(x+3,y+3,rng.uniform(6,17),(22,25,27),.8,True)
  def crack(x,y,angle,steps,width):
   for i in range(steps):
    angle+=rng.uniform(-.65,.65);nx=x+math.cos(angle)*rng.uniform(3,10);ny=y+math.sin(angle)*rng.uniform(3,10)
    line((x,y),(nx,ny),width,(15,17,18),.85)
    if steps>12 and i in (5,14):crack(nx,ny,angle+rng.choice([-1,1])*1.2,8,width*.65)
    x,y=nx,ny
  for _ in range(9):crack(rng.uniform(0,SIZE),rng.uniform(0,SIZE),rng.uniform(0,6.28),rng.randrange(18,35),rng.uniform(.8,1.4))
  # Small irregular repair seams rather than repeated large painted squares.
  x,y=160,210;points=[(x,y),(x+62,y+3),(x+68,y+49),(x+1,y+45),(x,y)]
  for a,b in zip(points,points[1:]):line(a,b,1.2,(23,23,22),.65)
 elif kind=='concrete':
  for _ in range(75):
   x,y=rng.uniform(0,SIZE),rng.uniform(0,SIZE)
   for j in range(rng.randrange(4,18)):dot(x+rng.uniform(-2,2),y+j*3,rng.uniform(2,7),(73,64,52),.10,True)
  for _ in range(130):dot(rng.uniform(0,SIZE),rng.uniform(0,SIZE),rng.uniform(.8,2.5),(43,40,35),.35)
 else:
  for _ in range(35):
   x,y=rng.uniform(0,SIZE),rng.uniform(0,SIZE);length=rng.randrange(10,55)
   dot(x,y,rng.uniform(2,9),(102,49,20),.6,True)
   for j in range(length):dot(x+rng.uniform(-1,1),y+j,rng.uniform(.9,2.5),(94,39,15),.10,True)
  for _ in range(70):
   x,y=rng.uniform(0,SIZE),rng.uniform(0,SIZE)
   line((x,y),(x+rng.uniform(2,10),y+rng.uniform(-1,1)),.6,(102,95,76),.5)
 return pixels
for kind in ['asphalt','concrete','metal']:
 write(DEST/('wear_'+kind+'.png'),texture(kind),SIZE,SIZE)
print('Created 3 original 512x512 repeatable RGBA textures with provenance.')
