#!/usr/bin/env python3
"""Generate developer export labels/texture identities from the original MagpieFX corpus."""
from pathlib import Path
import json
from effect_catalog import ROOT,registered
from magpiefx import parse

FORMATS={'R16G16B16A16_FLOAT':0,'R8G8B8A8_UNORM':1,'R16_FLOAT':2,'R16G16_FLOAT':3,'R8_UNORM':4,'R8G8_UNORM':5}
lines=['// Generated from original pass descriptions and output names. Do not edit.','namespace Magpie.Desktop.Core;','internal static partial class PassExportCatalog','{','    internal static readonly IReadOnlyDictionary<uint,PassExportDescription[]> All=new Dictionary<uint,PassExportDescription[]>','    {']
quote=lambda text:json.dumps(text,ensure_ascii=True)
for row in registered('graph'):
    source=ROOT/'src/Effects'/(row['path']+'.hlsl');effect=parse(source.read_text(encoding='utf-8-sig'),str(source))
    resources=[texture for texture in effect.textures if texture.name not in ('INPUT','OUTPUT')]
    passes=[]
    for item in effect.passes:
        outputs=[]
        for name in item.outputs:
            if name=='OUTPUT':index=-1;fmt=1
            else:
                index=next(i for i,t in enumerate(resources) if t.name==name);fmt=FORMATS[resources[index].format]
            outputs.append(f'new({quote(name)},{index},{fmt})')
        passes.append('new('+quote(item.description or 'Pass '+str(item.number))+',['+','.join(outputs)+'])')
    lines.append('        ['+str(row['id'])+']=['+','.join(passes)+'],')
lines+=['    };','}'];text='\n'.join(lines)+'\n';target=ROOT/'src/Magpie.Desktop/Core/PassExportCatalog.Generated.cs'
if not target.exists() or target.read_text(encoding='utf-8')!=text:target.write_text(text,encoding='utf-8')
print('PASS: original developer pass descriptions and output identities generated for '+str(len(registered('graph')))+' graphs.')
