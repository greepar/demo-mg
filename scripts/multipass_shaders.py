"""Compile original CNN pass/resource graphs; retain original RGBA16F / RGBA8 intermediates."""
import re
import struct
import subprocess
from pathlib import Path
from magpiefx import parse, FORMATS
from dds_source import read as read_dds
from effect_catalog import registered

_registered=registered('graph')
EFFECTS=[r['path'] for r in _registered]
EFFECT_IDS=[r['id'] for r in _registered]
# Format code, HLSL component type, SPIR-V image format. Runtime maps the code to VkFormat.
STORAGE_FORMATS={
    'R16G16B16A16_FLOAT':(0,'float4','rgba16f'),
    'R8G8B8A8_UNORM':(1,'float4','rgba8'),
    'R16_FLOAT':(2,'float','r16f'),
    'R16G16_FLOAT':(3,'float2','rg16f'),
    'R8_UNORM':(4,'float','r8'),
    'R8G8_UNORM':(5,'float2','rg8'),
}

def native_half_source(shader):
    # SM 6.2 half with -enable-16bit-types uses real 16-bit arithmetic.
    # Resource and push-constant declarations retain float32 and the old ABI.
    shader=re.sub(r'(#define MF\w* )float',r'\1half',shader)
    def separate_half_mac(match):
        columns=match[1];body=match[2];index=0
        def product(term):
            nonlocal index
            name=f'mpHalfProduct{index}';index+=1
            return f'precise MF{columns} {name}={term[1]}*{term[2]};\na={name}+a;'
        body=re.sub(r'a=mad\(([^,\n]+),([^,\n]+),a\);',product,body)
        return f'precise MF{columns} MulAdd'+body
    # GLSL Fma may be fused or separate across implementations. Specify two
    # actual half operations with NoContraction, keeping half rounding at each
    # step, so the original MulAdd helper has the same semantics on every GPU.
    shader=re.sub(r'MF([234]) MulAdd(\([^{}]+\) \{[^{}]+\})',separate_half_mac,shader)
    return '#define MP_FP16\n'+shader

def half_round_to_even(data):
    # SPV_KHR_float_controls: OpCapability RoundingModeRTE (4467),
    # OpExecutionMode RoundingModeRTE (4462), width=16. No new result IDs.
    words=list(struct.unpack('<'+'I'*(len(data)//4),data));instructions=[];pos=5;entry=None
    while pos<len(words):
        count=words[pos]>>16;assert count
        instruction=words[pos:pos+count];instructions.append(instruction)
        if instruction[0]&65535==15:entry=instruction[2]
        pos+=count
    assert entry is not None
    end=max(i for i,ins in enumerate(instructions) if ins[0]&65535==17)+1
    extension=b'SPV_KHR_float_controls\0';extension+=b'\0'*((-len(extension))%4)
    packed=list(struct.unpack('<'+'I'*(len(extension)//4),extension))
    instructions[end:end]=[[(2<<16)|17,4467],[((len(packed)+1)<<16)|10,*packed]]
    end=max(i for i,ins in enumerate(instructions) if ins[0]&65535==16)+1
    instructions.insert(end,[(4<<16)|16,entry,4462,16])
    result=words[:5]+[word for ins in instructions for word in ins]
    return struct.pack('<'+'I'*len(result),*result)

def compile_pass(dxc,root,output,name,stem,shader,*,fp16=False):
    if fp16:
        shader=native_half_source(shader);stem+='_fp16'
        if name.startswith('Anime4K/') and 'INPUT.SampleLevel(sam1,' in shader:
            # The RGB skip connection is interpolated at quarter texels. Some
            # software samplers quantize the UNORM interpolation before adding
            # the half residual, causing a second output code of error.
            marker='[[vk::binding(0,0)]] Texture2D<float4> INPUT;\n'
            assert marker in shader
            shader=shader.replace(marker,marker+'''float4 SampleInputLinear(float2 uv,float lod) {
    float2 pixel=uv*float2(settings.inputSize)-0.5;
    int2 base=int2(floor(pixel));float2 fraction=frac(pixel);
    int2 maximum=int2(settings.inputSize)-1;
    float4 a=INPUT.Load(int3(clamp(base,int2(0,0),maximum),0));
    float4 b=INPUT.Load(int3(clamp(base+int2(1,0),int2(0,0),maximum),0));
    float4 c=INPUT.Load(int3(clamp(base+int2(0,1),int2(0,0),maximum),0));
    float4 d=INPUT.Load(int3(clamp(base+int2(1,1),int2(0,0),maximum),0));
    return lerp(lerp(a,b,fraction.x),lerp(c,d,fraction.x),fraction.y);
}
''')
            shader=shader.replace('INPUT.SampleLevel(sam1,','SampleInputLinear(')
    hlsl,spirv=output/(stem+'.hlsl'),output/(stem+'.spv');hlsl.write_text(shader)
    if re.search(r'^\s*#\s*include\b',shader,re.M):
        expanded=output/(stem+'.preprocessed.hlsl')
        subprocess.run([dxc,'-P','-Fi',str(expanded),'-I',str(root/'src/Effects'/Path(name).parent),str(hlsl)],check=True)
        shader=expanded.read_text()
        shader=re.sub(r'\bOUTPUT\[(.*?)\]\s*=\s*(.*?);',r'if(all(\1 < settings.outputSize)) StoreOutput(\1,\2);',shader,flags=re.S)
        hlsl.write_text(shader)
    language=['-HV','2018'] if name=='Jinc' else []
    subprocess.run([dxc,*language,'-spirv','-fspv-target-env=vulkan1.1','-T','cs_6_2' if fp16 else 'cs_6_0',*(['-enable-16bit-types'] if fp16 else []),'-E','main','-O3','-I',str(root/'src/Effects'/Path(name).parent),'-Fo',str(spirv),str(hlsl)],check=True)
    data=spirv.read_bytes()
    if fp16:
        data=half_round_to_even(data);spirv.write_bytes(data)
    return data

def input_factor(expression,axis):
    normalized=re.sub(r'\s+','',expression)
    if not re.fullmatch(axis+r'(?:\*[1-6])*',normalized):raise ValueError('Only integer input-relative texture dimensions are implemented')
    factor=1
    for number in re.findall(r'\*([1-6])',normalized):factor*=int(number)
    if factor>6:raise ValueError('Intermediate scale exceeds six')
    return factor

def dimension(expression):
    normalized=re.sub(r'\s+','',expression)
    match=re.fullmatch(r'(INPUT_WIDTH|INPUT_HEIGHT|OUTPUT_WIDTH|OUTPUT_HEIGHT)(?:\*[1-6])*',normalized)
    if not match:raise ValueError('Only input/output-relative integer texture dimensions are implemented')
    axis=('INPUT_WIDTH','INPUT_HEIGHT','OUTPUT_WIDTH','OUTPUT_HEIGHT').index(match[1])
    return axis,input_factor(normalized,match[1])

def size_code(spec):
    axis,factor=spec
    return f'{("settings.inputSize.x","settings.inputSize.y","settings.outputSize.x","settings.outputSize.y")[axis]}*{factor}'

def generate(root, dxc, output, helpers, *, definitions=None):
    # Standalone compilation uses the same lowering as embedded effects. IDs are
    # supplied by the caller; compiling a shader does not register it at runtime.
    definitions=list(zip(EFFECT_IDS,EFFECTS)) if definitions is None else list(definitions)
    if not definitions or len({i for i,_ in definitions})!=len(definitions):
        raise ValueError('Effect definitions require nonempty, unique IDs')
    names=[name for _,name in definitions]
    for name in names:
        path=(root/f'src/Effects/{name}.hlsl').resolve()
        if not path.is_relative_to((root/'src/Effects').resolve()):
            raise ValueError('Effect path must stay within the effects tree')
    arrays=[]; passes=[]; effects=[]; asset_cache={}
    effects_ir=[parse((root/f'src/Effects/{name}.hlsl').read_text(encoding='utf-8-sig'),name) for name in names]
    sampled=1+max(len(effect.textures)-2 for effect in effects_ir)
    storage_count=max(len(p.outputs) for effect in effects_ir for p in effect.passes)
    sampler,storage,buffer=sampled,sampled+2,sampled+2+storage_count
    max_passes=max(len(effect.passes) for effect in effects_ir)
    common='''struct Settings { uint2 inputSize; uint2 outputSize; float parameter; uint mode; float parameter2; float reserved; float4 extra[4]; };
[[vk::push_constant]] ConstantBuffer<Settings> settings;
[[vk::binding(BUFFER_BINDING,0)]] RWStructuredBuffer<uint> result;
uint2 GetInputSize() { return settings.inputSize; }
float2 GetInputPt() { return 1.0 / float2(settings.inputSize); }
float2 GetScale() { return float2(settings.outputSize)/float2(settings.inputSize); }
'''.replace('BUFFER_BINDING',str(buffer))+helpers
    # Explicit binary16 RNE before image conversion avoids implementation-defined
    # float32->image rounding. The image receives an exactly representable value.
    common+='''float MPHalfStore(float value) {
    uint bits=asuint(value),magnitude=bits&0x7fffffffu;
    if(magnitude>=0x7f800000u)return value;
    if(magnitude>=0x477ff000u)return asfloat((bits&0x80000000u)|0x7f800000u);
    if(magnitude<0x38800000u)return sign(value)*round(abs(value)*16777216.0)/16777216.0;
    uint rounded=(magnitude+4095u+((magnitude>>13)&1u))&0xffffe000u;
    return asfloat((bits&0x80000000u)|rounded);
}
float MPUnormStore(float value) { return round(saturate(value)*255.0)/255.0; }
float2 MPUnormStore(float2 v) { return float2(MPUnormStore(v.x),MPUnormStore(v.y)); }
float4 MPUnormStore(float4 v) { return float4(MPUnormStore(v.x),MPUnormStore(v.y),MPUnormStore(v.z),MPUnormStore(v.w)); }
float2 MPHalfStore(float2 v) { return float2(MPHalfStore(v.x),MPHalfStore(v.y)); }
float4 MPHalfStore(float4 v) { return float4(MPHalfStore(v.x),MPHalfStore(v.y),MPHalfStore(v.z),MPHalfStore(v.w)); }
'''
    for rows in range(2,5):
        for cols in range(2,5):
            common+=f'#define MF{rows}x{cols} float{rows}x{cols}\n'
            common+=f'MF{cols} MulAdd(MF{rows} x, MF{rows}x{cols} y, MF{cols} a) {{\n'
            for row in range(rows):
                swizzle='_'.join(f'm{row}{column}' for column in range(cols))
                common+=f'a=mad(x.{"xyzw"[row]},y._{swizzle},a);\n'
            common+='return a; }\n'
    for name,effect in zip(names,effects_ir):
        effect_id=definitions[len(effects)][0]
        legacy_extra=effect_id==12 and name=='CRT/CRT_Geom'
        resources=[t.name for t in effect.textures[2:]]
        texture_count=len(resources);bindings={name:i+1 for i,name in enumerate(resources)}
        if texture_count>32 or len(effect.passes)>16:raise ValueError(f'{name}: unsupported graph size')
        parameter_code=''
        for slot,parameter in enumerate(effect.parameters):
            if slot>=(16 if legacy_extra else 18):raise ValueError(f'{name}: parameters exceed the configured stage ABI')
            if legacy_extra:field=f'extra[{slot//4}][{slot%4}]'
            elif slot<2:field='parameter' if slot==0 else 'parameter2'
            else:field=f'extra[{(slot-2)//4}][{(slot-2)%4}]'
            parameter_code+=f'#define {parameter.name} ({parameter.kind}(settings.{field}))\n'
        output_texture=effect.texture('OUTPUT')
        fixed=0
        if output_texture.width:
            fixed=input_factor(output_texture.width,'INPUT_WIDTH')
            if fixed!=input_factor(output_texture.height,'INPUT_HEIGHT'):raise ValueError(f'{name}: fixed output needs equal integral axis factors')
        formats={};specs=[]
        for texture in effect.textures:
            if texture.name=='INPUT':continue
            if texture.name=='OUTPUT':continue
            asset=None
            if texture.source:
                source_path=(root/'src/Effects'/Path(name).parent/texture.source).resolve()
                if not source_path.is_relative_to((root/'src/Effects').resolve()):raise ValueError(f'{name}: SOURCE must stay within the effects tree')
                asset=read_dds(source_path)
                if texture.format and texture.format!=asset.format:raise ValueError(f'{name}: SOURCE format mismatch')
                factors=[(4,asset.width),(4,asset.height)]
            else:factors=[dimension(texture.width),dimension(texture.height)]
            texture_format=asset.format if asset else texture.format
            if texture_format not in STORAGE_FORMATS:raise ValueError(f'{name}: unsupported storage format {texture.format}')
            code,component,image_format=STORAGE_FORMATS[texture_format]
            formats[texture.name]=(code,component,image_format,factors)
            asset_name='nullptr';asset_bytes=0
            if asset:
                asset_bytes=len(asset.pixels)
                if source_path not in asset_cache:
                    asset_cache[source_path]=f'multiAsset{len(asset_cache)}'
                    arrays.append(f'inline constexpr uint8_t {asset_cache[source_path]}[]={{'+','.join(str(v) for v in asset.pixels)+'};\n')
                asset_name=asset_cache[source_path]
            specs.append((code,*factors[0],*factors[1],FORMATS[texture_format][1],asset_name,asset_bytes))
        for sampler_desc in effect.samplers:
            if sampler_desc.address!='CLAMP':raise ValueError(f'{name}: WRAP is not implemented')
        shared=effect.common
        if name.startswith('SMAA/'):
            include=(root/'src/Effects/SMAA/SMAA.hlsli').read_text(encoding='utf-8-sig')
            # SMAA's exact-zero edge tests amplify differences between fixed-point
            # hardware interpolation and software samplers. Canonicalize 8-bit
            # subtexel weights explicitly, preserving the original filter/search math.
            helpers_smaa=''
            for component in ('float','float2','float4'):
                helpers_smaa+=f'''{component} MPSMAALinear(Texture2D<{component}> tex,float2 uv,int2 offset) {{
    uint w,h;tex.GetDimensions(w,h);int2 maximum=int2(w,h)-1;
    float2 pixel=uv*float2(w,h)-0.5;int2 base=int2(floor(pixel))+offset;
    float2 fraction=round(frac(pixel)*256.0)/256.0;
    int2 carry=int2(floor(fraction));base+=carry;fraction-=float2(carry);
    {component} a=tex.Load(int3(clamp(base,int2(0,0),maximum),0));
    {component} b=tex.Load(int3(clamp(base+int2(1,0),int2(0,0),maximum),0));
    {component} c=tex.Load(int3(clamp(base+int2(0,1),int2(0,0),maximum),0));
    {component} d=tex.Load(int3(clamp(base+int2(1,1),int2(0,0),maximum),0));
    return lerp(lerp(a,b,fraction.x),lerp(c,d,fraction.x),fraction.y);
}}
{component} MPSMAAPoint(Texture2D<{component}> tex,float2 uv) {{
    uint w,h;tex.GetDimensions(w,h);int2 texel=int2(floor(uv*float2(w,h)));
    return tex.Load(int3(clamp(texel,int2(0,0),int2(w,h)-1),0));
}}
'''
            replacements={
                'tex.SampleLevel(SMAA_LINEAR_SAMPLER, coord, 0, offset)':'MPSMAALinear(tex, coord, offset)',
                'tex.SampleLevel(SMAA_LINEAR_SAMPLER, coord, 0)':'MPSMAALinear(tex, coord, int2(0,0))',
                'tex.SampleLevel(SMAA_POINT_SAMPLER, coord, 0)':'MPSMAAPoint(tex, coord)',
            }
            for old,new in replacements.items():
                if old not in include:raise ValueError('Unexpected SMAA sampler declaration')
                include=include.replace(old,new)
            shared=helpers_smaa+shared.replace('#include "SMAA.hlsli"',include)
        if name.startswith(('NNEDI3/','RAVU/')):
            # The original uint border arithmetic can produce normalized coordinates
            # near 2^32. Canonicalize CLAMP before sampler implementations convert
            # coordinates to signed texel indices (which differs across drivers).
            shared+='\n#undef texture\n#define texture(tex,pos) tex.SampleLevel(sam_##tex,clamp((pos),0.0,1.0),0.0)\n'
        first=len(passes)
        for p in effect.passes:
            number,body=p.number,p.code
            if name.startswith('Anime4K/Anime4K_Denoise_Bilateral_'):
                # DXC does not accept max()/ceil() in these array dimensions.
                # SPATIAL_SIGMA is an original source constant, not a user
                # parameter. Fold only the verified literal expression; keep
                # intensitySigma dynamically bound to the configured value.
                spatial=re.search(r'^#define SPATIAL_SIGMA ([^\n]+)$',body,re.M)
                if spatial is None or spatial[1].split('//',1)[0].strip()!='1.0':raise ValueError('Unexpected bilateral spatial constant')
                expression='(max(uint(ceil(SPATIAL_SIGMA * 2.0)), 1) * 2 + 1)' if name.endswith('_Mean') else 'uint(max(uint(SPATIAL_SIGMA), 1) * 2 + 1)'
                macro='#define KERNELSIZE '+expression
                if body.count(macro)!=1:raise ValueError('Unexpected bilateral kernel expression')
                body=body.replace(macro,'#define KERNELSIZE '+('5' if name.endswith('_Mean') else '3'))
            if name=='CRT/CRT_Geom':
                # Preserve the established exact symmetry-axis behavior when
                # moving the original effect from its dedicated PS adapter.
                marker='float2 ratio_scale = (xy * TextureSize - 0.5) / ilfac;'
                if body.count(marker)!=1:raise ValueError('Unexpected CRT symmetry expression')
                body=body.replace(marker,marker+'\nif ((curvature == 0 || yTilt == 0) && pos.y == 0.5) ratio_scale.y = (float(GetInputSize().y) * 0.5 - 0.5) / ilfac.y;')
            if name=='Jinc':
                marker='float2 pc = (gxy + 0.5f) * GetOutputPt() * GetInputSize();'
                if body.count(marker)!=1:raise ValueError('Unexpected Jinc source position')
                # Reciprocal rounding at the center can move floor(pc-.5)
                # across a texel boundary. Preserve the exact symmetry axis.
                body=body.replace(marker,marker+'\nif(2*gxy.x+1==settings.outputSize.x)pc.x=float(settings.inputSize.x)*0.5;\nif(2*gxy.y+1==settings.outputSize.y)pc.y=float(settings.inputSize.y)*0.5;')
            inputs,outputs=list(p.inputs),list(p.outputs)
            bx,by=p.block;tx,ty,tz=p.threads
            if p.style=='PS' and len(outputs)!=1:raise ValueError(f'{name}: PS multiple render targets are not implemented yet')
            if p.style=='PS':bx,by,tx,ty,tz=8,8,8,8,1
            if outputs!=['OUTPUT'] and len({tuple(tuple(axis) for axis in formats[r][3]) for r in outputs})!=1:raise ValueError(f'{name}: outputs need equal dimensions within a pass')
            if len(outputs)>storage_count:raise ValueError('Unsupported storage count')
            declarations='[[vk::binding(0,0)]] Texture2D<float4> INPUT;\n'
            for resource in inputs:
                if resource!='INPUT':declarations+=f'[[vk::binding({bindings[resource]},0)]] Texture2D<{formats[resource][1]}> {resource};\n'
            for sampler_desc in effect.samplers:
                binding=sampler+(sampler_desc.filter=='LINEAR')
                declarations+=f'[[vk::binding({binding},0)]] SamplerState {sampler_desc.name};\n'
            integer_gather=name=='Jinc'
            if (name.startswith('FSRCNNX/') and number==1) or integer_gather:
                # Gather coordinates sit exactly on texel boundaries. Reciprocal
                # rounding can choose a different 2x2 neighborhood across drivers.
                # This source loop's intended integer base is blockStart+pos-2.
                declarations+='''float4 MPInputGather(int2 origin,uint channel) {
    int2 maximum=int2(settings.inputSize)-1;
    float a=INPUT.Load(int3(clamp(origin+int2(0,1),int2(0,0),maximum),0))[channel];
    float b=INPUT.Load(int3(clamp(origin+int2(1,1),int2(0,0),maximum),0))[channel];
    float c=INPUT.Load(int3(clamp(origin+int2(1,0),int2(0,0),maximum),0))[channel];
    float d=INPUT.Load(int3(clamp(origin,int2(0,0),maximum),0))[channel];
    return float4(a,b,c,d);
}
'''
                if name=='Jinc':
                    marker='float2 tpos = (tc + uint2(i, j)) * inputPt;'
                    replacement='const int2 gatherBase=int2(tc)+int2(i,j)-1;'
                else:
                    marker='const float2 tpos = (blockStart + pos - 1.5f) * inputPt;'
                    replacement='const int2 gatherBase=int2(blockStart+pos)-2;'
                if body.count(marker)!=1:raise ValueError(f'{name}: unexpected integer gather coordinates')
                body=body.replace(marker,replacement)
                for channel,component in enumerate(('Red','Green','Blue')):
                    pattern=f'INPUT.Gather{component}(sam, tpos)'
                    if body.count(pattern)!=1:raise ValueError(f'{name}: unexpected integer gather expression')
                    body=body.replace(pattern,f'MPInputGather(gatherBase,{channel})')
            if name=='Jinc':
                # Correct floor(pc-.5) at every rational texel boundary, not
                # only at the center. Extended 32-bit multiplication compares
                # exact products without requiring shaderInt64 or overflowing
                # on long, narrow images.
                declarations+='''void MPJincProduct(uint a,uint b,out uint low,out uint high) {
    uint t=(a&65535u)*(b&65535u);
    uint k=(a>>16)*(b&65535u)+(t>>16);
    uint m=(a&65535u)*(b>>16)+(k&65535u);
    high=(a>>16)*(b>>16)+(k>>16)+(m>>16);
    low=(m<<16)|(t&65535u);
}
int MPJincFloor(uint index,uint inputExtent,uint outputExtent,float approximate) {
    int base=int(floor(approximate-0.5));
    uint low,high,boundLow,boundHigh;
    MPJincProduct(2*index+1,inputExtent,low,high);
    if(base>=0) {
        MPJincProduct(2*uint(base)+1,outputExtent,boundLow,boundHigh);
        if(high<boundHigh || (high==boundHigh && low<boundLow))base--;
    }
    MPJincProduct(2*uint(base+1)+1,outputExtent,boundLow,boundHigh);
    if(high>boundHigh || (high==boundHigh && low>=boundLow))base++;
    return base;
}
'''
                marker='float2 tc = floor(pc - 0.5f) + 0.5f;'
                if body.count(marker)!=1:raise ValueError('Unexpected Jinc texture floor')
                body=body.replace(marker,'float2 tc=float2(MPJincFloor(gxy.x,settings.inputSize.x,settings.outputSize.x,pc.x),MPJincFloor(gxy.y,settings.inputSize.y,settings.outputSize.y,pc.y))+0.5;')
            if name in ('SSimDownscaler','Sharpen/LumaSharpen','Sharpen/LCAS') or name.startswith(('FSRCNNX/','RAVU/')):
                # Cubic and sharpening filters amplify hardware sampler
                # subtexel rounding into visible UNORM output differences.
                # Use the same linear filter in arithmetic across Vulkan samplers
                # with differing subtexel precision, retaining the original taps.
                declarations+='''float4 SampleInputLinear(float2 uv,float lod) {
    float2 pixel=uv*float2(settings.inputSize)-0.5;
    int2 base=int2(floor(pixel));float2 fraction=frac(pixel);
    int2 maximum=int2(settings.inputSize)-1;
    float4 a=INPUT.Load(int3(clamp(base,int2(0,0),maximum),0));
    float4 b=INPUT.Load(int3(clamp(base+int2(1,0),int2(0,0),maximum),0));
    float4 c=INPUT.Load(int3(clamp(base+int2(0,1),int2(0,0),maximum),0));
    float4 d=INPUT.Load(int3(clamp(base+int2(1,1),int2(0,0),maximum),0));
    return lerp(lerp(a,b,fraction.x),lerp(c,d,fraction.x),fraction.y);
}
'''
                for linear_sampler in effect.samplers:
                    if linear_sampler.filter=='LINEAR':body=body.replace(f'INPUT.SampleLevel({linear_sampler.name},','SampleInputLinear(')
            pass_size='settings.outputSize' if outputs==['OUTPUT'] else 'uint2('+','.join(size_code(v) for v in formats[outputs[0]][3])+')'
            for slot,resource in enumerate(outputs):
                if resource!='OUTPUT':
                    declarations+=f'[[vk::binding({storage+slot},0)]] [[vk::image_format("{formats[resource][2]}")]] RWTexture2D<{formats[resource][1]}> {resource};\n'
                if p.style=='CS':
                    if resource=='OUTPUT':body=re.sub(r'OUTPUT\[(.*?)\]\s*=\s*(.*?);',r'if(all(\1 < settings.outputSize)) StoreOutput(\1,\2);',body,flags=re.S)
                    else:
                        extent='uint2('+','.join(size_code(v) for v in formats[resource][3])+')'
                        round_value=r'MPHalfStore(\2)' if formats[resource][0] in (0,2,3) else r'\2'
                        body=re.sub(r'\b'+resource+r'\[(.*?)\]\s*=\s*(.*?);',fr'if(all(\1 < {extent})) {resource}[\1]='+round_value+';',body,flags=re.S)
            if p.style=='PS':
                resource=outputs[0]
                store='StoreOutput(id.xy,color);' if resource=='OUTPUT' else f'{resource}[id.xy]='+('MPHalfStore(color);' if formats[resource][0] in (0,2,3) else 'color;')
                if resource!='OUTPUT' and name.startswith('SMAA/') and formats[resource][0] in (1,4,5):
                    store=f'{resource}[id.xy]=MPUnormStore(color);'
                color_type='float4' if resource=='OUTPUT' else formats[resource][1]
                entry=f'\n[numthreads(8,8,1)] void main(uint3 id:SV_DispatchThreadID) {{ uint2 extent={pass_size};if(any(id.xy>=extent))return;float2 pos=(float2(id.xy)+0.5)/float2(extent);{color_type} color=Pass{number}(pos);{store} }}\n'
                if name=='CRT/CRT_Geom':entry=entry.replace(f'{color_type} color=',f'if(2*id.y+1==extent.y) pos.y=0.5;{color_type} color=')
            else:entry=f'\n[numthreads({tx},{ty},{tz})] void main(uint3 group:SV_GroupID,uint3 thread:SV_GroupThreadID) {{ Pass{number}(group.xy*uint2({bx},{by}),thread); }}\n'
            shader=common+declarations+parameter_code+shared+f'\n#define MP_PS_STYLE {1 if p.style=="PS" else 0}\n#define MP_BLOCK_WIDTH {bx}\n#define MP_BLOCK_HEIGHT {by}\n#define MP_NUM_THREADS_X {tx}\n#define MP_NUM_THREADS_Y {ty}\n#define MP_NUM_THREADS_Z {tz}\n'+body+entry
            stem=name.replace('/','_')+f'_pass{number}'
            data=compile_pass(dxc,root,output,name,stem,shader)
            words=struct.unpack('<'+'I'*(len(data)//4),data);index=len(passes)
            arrays.append(f'inline constexpr uint32_t multiKernel{index}[]={{\n'+',\n'.join(','.join(f'0x{n:08x}' for n in words[i:i+8]) for i in range(0,len(words),8))+'\n};\n')
            half_name='nullptr';half_size='0'
            if 'FP16' in effect.capabilities:
                data=compile_pass(dxc,root,output,name,stem,shader,fp16=True)
                words=struct.unpack('<'+'I'*(len(data)//4),data);half_name=f'multiKernel{index}Half';half_size=f'sizeof({half_name})'
                arrays.append(f'inline constexpr uint32_t {half_name}[]={{\n'+',\n'.join(','.join(f'0x{n:08x}' for n in words[i:i+8]) for i in range(0,len(words),8))+'\n};\n')
            out=[resources.index(n) for n in outputs if n!='OUTPUT'];out+=(storage_count-len(out))*[-1]
            passes.append((index,out,outputs==['OUTPUT'],bx,by,tx,ty,tz,half_name,half_size))
        parameters=[(p.minimum,p.maximum,p.default,p.kind=='int') for p in effect.parameters]
        effects.append((effect_id,first,len(passes)-first,fixed,specs,legacy_extra,parameters))
    # C++ has no standard zero-length arrays. A standalone graph can have no
    # intermediate images, while its texture count remains zero in metadata.
    max_textures=max(1,max(len(specs) for _,_,_,_,specs,_,_ in effects))
    arrays.append('struct MultiPassKernel { const uint32_t* data; size_t size; int outputs[STORAGE_COUNT]; bool final; uint32_t blockX,blockY,threads[3]; const uint32_t* halfData; size_t halfSize; };\ninline constexpr MultiPassKernel multiPasses[]={\n')
    for i,out,final,bx,by,tx,ty,tz,half_name,half_size in passes:arrays.append(f'{{multiKernel{i},sizeof(multiKernel{i}),{{'+','.join(map(str,out))+f'}},{str(final).lower()},{bx},{by},{{{tx},{ty},{tz}}},{half_name},{half_size}}},\n')
    arrays.append('};\nstruct MultiTextureKernel { uint32_t format,widthAxis,widthFactor,heightAxis,heightFactor,bytes; const uint8_t* source; size_t sourceBytes; };\nstruct MultiParameterKernel { float minimum,maximum,defaultValue; bool integral; };\nstruct MultiEffectKernel { uint32_t id,first,count,textures; uint32_t scaleFactor; MultiTextureKernel specs[MAX_TEXTURES]; bool legacyExtra; uint32_t parameterCount; MultiParameterKernel parameters[18]; };\ninline constexpr MultiEffectKernel multiEffects[]={\n')
    for effect_id,first,count,fixed,specs,legacy_extra,parameters in effects:
        values=','.join('{'+','.join(map(str,spec))+'}' for spec in specs)
        params=','.join('{'+','.join(str(float(v))+'f' for v in (lo,hi,default))+','+str(integral).lower()+'}' for lo,hi,default,integral in parameters)
        arrays.append(f'{{{effect_id},{first},{count},{len(specs)},{fixed},{{'+values+'},'+str(legacy_extra).lower()+f',{len(parameters)},{{'+params+'}},\n')
    arrays.append('};\ninline const MultiEffectKernel* multiEffect(uint32_t id) { for(const auto& effect:multiEffects)if(effect.id==id)return &effect;return nullptr; }\n')
    return f'inline constexpr uint32_t multiSampledCount={sampled},multiSamplerBinding={sampler},multiStorageBinding={storage},multiOutputBinding={buffer},multiBindingCount={buffer+1},multiMaxPasses={max_passes},multiStorageCount={storage_count};\n'+''.join(arrays).replace('STORAGE_COUNT',str(storage_count)).replace('MAX_TEXTURES',str(max_textures))
