"""Stable original effect identity and metadata, shared by native and managed builds."""
import json
from pathlib import Path
import re
from magpiefx import parse,FORMATS
from dds_source import read as read_dds

ROOT=Path(__file__).resolve().parents[1]
REGISTRY=ROOT/'src/Magpie.Native/effects.json'

def registry():
    data=json.loads(REGISTRY.read_text());rows=data['effects']
    if type(data['version']) is not int or data['version']!=1:raise ValueError('Unknown effect registry version')
    ids=[r['id'] for r in rows];paths=[r['path'] for r in rows]
    if len(ids)!=len(set(ids)) or len(paths)!=len(set(paths)) or 6 in ids or any(type(i) is not int or i<0 or i>0xffffffff for i in ids):raise ValueError('Invalid or duplicate effect identities')
    actual={p.relative_to(ROOT/'src/Effects').with_suffix('').as_posix() for p in (ROOT/'src/Effects').rglob('*.hlsl')}
    if set(paths)!=actual:raise ValueError('Effect registry must explicitly cover the original corpus; preserve existing IDs when adding new entries')
    for row in rows:
        if row['backend'] not in ('scalar','graph') or type(row['registered']) is not bool or type(row['order']) is not int or row['order']<0:raise ValueError('Invalid effect registration/backend')
    for backend in ('scalar','graph'):
        orders=[r['order'] for r in rows if r['backend']==backend]
        if len(orders)!=len(set(orders)):raise ValueError('Duplicate backend order')
    scalar=[r for r in rows if r['backend']=='scalar']
    if {r['id'] for r in scalar}!=set(range(6)) or any(not r['registered'] or r['order']!=r['id'] for r in scalar):raise ValueError('Legacy scalar ABI IDs and order must remain 0..5')
    return rows

def registered(backend):return sorted((r for r in registry() if r['registered'] and r['backend']==backend),key=lambda r:r['order'])

def dimension(expression):
    value=re.sub(r'\s+','',expression)
    m=re.fullmatch(r'(INPUT_WIDTH|INPUT_HEIGHT|OUTPUT_WIDTH|OUTPUT_HEIGHT)((?:\*[1-6])*)',value)
    if not m:raise ValueError('Unsupported catalog dimension: '+expression)
    factor=1
    for number in re.findall(r'\*([1-6])',m[2]):factor*=int(number)
    if factor>6:raise ValueError('Catalog dimension factor exceeds six')
    return [('INPUT_WIDTH','INPUT_HEIGHT','OUTPUT_WIDTH','OUTPUT_HEIGHT').index(m[1]),factor]

def catalog():
    effects=[]
    for row in registry():
        name=row['path'];source=ROOT/f'src/Effects/{name}.hlsl';effect=parse(source.read_text(encoding='utf-8-sig'),name)
        output=effect.texture('OUTPUT');scale=0
        if output.width:
            width=dimension(output.width);height=dimension(output.height)
            if width[0]!=0 or height[0]!=1 or width[1]!=height[1]:raise ValueError('Unsupported catalog fixed output: '+name)
            scale=width[1]
        textures=[]
        for texture in effect.textures[2:]:
            fmt=texture.format
            if texture.source:
                path=(source.parent/texture.source).resolve()
                if not path.is_relative_to((ROOT/'src/Effects').resolve()):raise ValueError('SOURCE escapes original corpus')
                asset=read_dds(path);fmt=asset.format;w,h=[4,asset.width],[4,asset.height]
                if texture.format and texture.format!=fmt:raise ValueError('SOURCE format differs from catalog declaration')
            else:w,h=dimension(texture.width),dimension(texture.height)
            textures.append({'name':texture.name,'format':fmt,'width':w,'height':h,'bytes':FORMATS[fmt][1],'source':texture.source})
        effects.append(row|{'fp16':'FP16' in effect.capabilities,'scale':scale,'passes':len(effect.passes),'parameters':[{'name':p.name,'label':p.label,'default':p.default,'minimum':p.minimum,'maximum':p.maximum,'step':p.step,'integer':p.kind=='int'} for p in effect.parameters],'textures':textures})
    return effects
