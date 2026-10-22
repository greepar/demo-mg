"""Execute the original SMAA shader mathematics on the CPU via a small GLM adapter.
This translator is reference-test tooling, not the production MagpieFX compiler.
"""
import re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
COMPONENTS={c:i for alphabet in ('xyzw','rgba') for i,c in enumerate(alphabet)}
def args(text):
 result=[];depth=0;start=0
 for i,c in enumerate(text):
  if c in '([':depth+=1
  if c in ')]':depth-=1
  if c==',' and depth==0:result.append(text[start:i]);start=i+1
 result.append(text[start:]);return result

def transform(source):
 source=re.sub(r'/\*.*?\*/|//[^\n]*','',source,flags=re.S)
 source=re.sub(r'\b(inout|out)\s+(float[234]?)\s+(\w+)',r'\2\& \3',source).replace('\\&','&')
 # C++ references cannot bind a temporary swizzle to an HLSL inout argument.
 call=re.compile(r'\b(SMAAMovc|SMAADetectHorizontalCornerPattern|SMAADetectVerticalCornerPattern)\(([^;{}]+)\);')
 def inout(match):
  values=args(match[2]);target=values[1].strip();sw=re.fullmatch(r'(\w+)\.([xyzwrgba]{2,4})',target)
  if not sw:return match[0]
  values[1]='part';return '{ float'+str(len(sw[2]))+' part='+target+'; '+match[1]+'('+','.join(values)+'); '+target+'=part; }'
 source=call.sub(inout,source)
 def assignment(match):
  base,sw,op,value=match.groups();indices=','.join(str(COMPONENTS[c]) for c in sw)
  rhs=value if op=='=' else f'get<{indices}>({base}){op[0]}({value})'
  return f'set<{indices}>({base},({rhs}));'
 source=re.sub(r'\b(\w+)\.([xyzwrgba]{2,4})\s*(\*=|\+=|-=|/=|=(?!=))([^;]+);',assignment,source)
 # Replace rvalue swizzles, including those on texture sampling expressions.
 pattern=re.compile(r'\.([xyzwrgba]{2,4})\b')
 while match:=pattern.search(source):
  end=match.start();start=end-1;depth=0
  while start>=0:
   c=source[start]
   if c in ')]':depth+=1
   elif c in '([':
    depth-=1
    if depth<0:break
   elif depth==0 and not(c.isalnum() or c in '_.'):break
   start-=1
  start+=1;value=source[start:end];indices=','.join(str(COMPONENTS[c]) for c in match[1])
  source=source[:start]+f'(get<{indices}>({value}))'+source[match.end():]
 source=source.replace('#define SMAA_SEARCHTEX_SELECT(sample) sample.r','#define SMAA_SEARCHTEX_SELECT(sample) sample')
 source=re.sub(r'\bround\(', 'roundEven(', source)
 source=re.sub(r'\[(?:flatten|branch|unroll|loop)\]','',source)
 # HLSL unsuffixed floating constants are float32, unlike C++ double literals.
 source=re.sub(r'(?<![\w.])((?:\d+\.\d*|\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)(?![\w.])',r'\1f',source)
 return source

def generate(path):
 header=(ROOT/'src/Effects/SMAA/SMAA.hlsli').read_text(encoding='utf-8-sig')
 header=header.replace('a.wz = SMAASample(blendTex, texcoord).xz;', 'a.wz = SMAASample(blendTex, texcoord).xz; if(std::getenv("MAGPIE_SMAA_CPU_A"))return a;')
 code=['#include "smaa_cpu.h"\n']
 for preset in ['LOW','MEDIUM','HIGH','ULTRA']:
  code+=[f'namespace {preset} {{\n#define SMAA_PRESET_{preset}\n',transform(header),'\n',f'#undef SMAA_PRESET_{preset}\n']
  for name in ['SMAA_THRESHOLD','SMAA_MAX_SEARCH_STEPS','SMAA_MAX_SEARCH_STEPS_DIAG','SMAA_CORNER_ROUNDING','SMAA_DISABLE_DIAG_DETECTION','SMAA_DISABLE_CORNER_DETECTION']:
   code+=[f'#undef {name}\n']
  code+=['}\n']
 code+=['''std::vector<uint8_t> render(int preset,const std::vector<uint8_t>& pixels,int w,int h,Texture2D<float4> area,Texture2D<float> search) {
    SMAA_RT_METRICS=float4(1.f/w,1.f/h,float(w),float(h));
    Texture2D<float4> input(w,h),blend(w,h);Texture2D<float2> edge(w,h);
    for(int i=0;i<w*h;i++)input.data[i]=float4(pixels[i*4],pixels[i*4+1],pixels[i*4+2],pixels[i*4+3])/255.f;
    for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        float2 uv((x+.5f)/w,(y+.5f)/h);float2 value;
        switch(preset) {
''']
 for i,preset in enumerate(['LOW','MEDIUM','HIGH','ULTRA']):code+=[f'case {i}:value={preset}::SMAALumaEdgeDetectionPS(uv,input);break;\n']
 code+=['''default:throw std::runtime_error("Invalid preset"); }
        edge.data[y*w+x]=preset<2 ? glm::roundEven(glm::clamp(value,0.f,1.f)*255.f)/255.f : half(value);
    }
    for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        float2 uv((x+.5f)/w,(y+.5f)/h);float4 value;
        switch(preset) {
''']
 for i,preset in enumerate(['LOW','MEDIUM','HIGH','ULTRA']):code+=[f'case {i}:value={preset}::SMAABlendingWeightCalculationPS(uv,edge,area,search,float4(0));break;\n']
 code+=['''default:throw std::runtime_error("Invalid preset"); }
        blend.data[y*w+x]=preset<2 ? glm::roundEven(glm::clamp(value,0.f,1.f)*255.f)/255.f : half(value);
    }
    std::vector<uint8_t> result(w*h*4);
    if(std::getenv("MAGPIE_SMAA_CPU_BLEND")) {
        for(int i=0;i<w*h;i++)for(int c=0;c<4;c++)result[i*4+c]=uint8_t(std::nearbyint(glm::clamp(blend.data[i][c],0.f,1.f)*255.f));return result;
    }
    if(std::getenv("MAGPIE_SMAA_CPU_EDGE")) {
        for(int i=0;i<w*h;i++){result[i*4]=uint8_t(edge.data[i].x*255);result[i*4+1]=uint8_t(edge.data[i].y*255);result[i*4+2]=0;result[i*4+3]=255;}return result;
    }
    for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        float2 uv((x+.5f)/w,(y+.5f)/h);float4 value;
        switch(preset) {
''']
 for i,preset in enumerate(['LOW','MEDIUM','HIGH','ULTRA']):code+=[f'case {i}:value={preset}::SMAANeighborhoodBlendingPS(uv,input,blend);break;\n']
 code+=['''default:throw std::runtime_error("Invalid preset"); }
        for(int c=0;c<4;c++)result[(y*w+x)*4+c]=uint8_t(std::nearbyint(glm::clamp(value[c],0.f,1.f)*255.f));
    }
    return result;
}
int main(int argc,char** argv) {
    try {
        if(argc!=9)throw std::runtime_error("preset width height input area search output trace");
        int preset=std::stoi(argv[1]),w=std::stoi(argv[2]),h=std::stoi(argv[3]);
        samplerPrecisionBits=std::stoi(argv[8]);if(samplerPrecisionBits<4||samplerPrecisionBits>16)throw std::runtime_error("Unsupported sampler precision");
        auto pixels=read(argv[4]);auto areaBytes=read(argv[5]),searchBytes=read(argv[6]);
        if(w<1||h<1||w>128||h>128||pixels.size()!=size_t(w*h*4)||areaBytes.size()!=160*560*4||searchBytes.size()!=64*16)throw std::runtime_error("Invalid CPU fixture");
        Texture2D<float4> area(160,560);for(int i=0;i<160*560;i++)area.data[i]=float4(areaBytes[i*4],areaBytes[i*4+1],areaBytes[i*4+2],areaBytes[i*4+3])/255.f;
        Texture2D<float> search(64,16);for(int i=0;i<64*16;i++)search.data[i]=searchBytes[i]/255.f;
        auto result=render(preset,pixels,w,h,area,search);std::ofstream output(argv[7],std::ios::binary);output.write(reinterpret_cast<const char*>(result.data()),result.size());
        if(!output)throw std::runtime_error("Could not write CPU fixture");return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<std::endl;return 1;}
}
''']
 path.write_text(''.join(code))
