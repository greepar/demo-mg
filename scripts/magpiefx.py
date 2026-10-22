"""MagpieFX v4 build-time front end shared by Vulkan adapters.

Parses metadata and preserves HLSL bodies. This is not a general HLSL compiler or
runtime shader loader; DXC and a backend for the parsed resource graph are separate.
"""
from dataclasses import dataclass
import math
import re
import struct

FORMATS = {
    'R32G32B32A32_FLOAT': (4,16), 'R16G16B16A16_FLOAT': (4,8),
    'R16G16B16A16_UNORM': (4,8), 'R16G16B16A16_SNORM': (4,8),
    'R32G32_FLOAT': (2,8), 'R10G10B10A2_UNORM': (4,4), 'R11G11B10_FLOAT': (3,4),
    'R8G8B8A8_UNORM': (4,4), 'R8G8B8A8_SNORM': (4,4),
    'R16G16_FLOAT': (2,4), 'R16G16_UNORM': (2,4), 'R16G16_SNORM': (2,4),
    'R32_FLOAT': (1,4), 'R8G8_UNORM': (2,2), 'R8G8_SNORM': (2,2),
    'R16_FLOAT': (1,2), 'R16_UNORM': (1,2), 'R16_SNORM': (1,2),
    'R8_UNORM': (1,1), 'R8_SNORM': (1,1),
}
IDENTIFIER = r'[A-Za-z_]\w*'
DIRECTIVE = re.compile(r'^[ \t]*//![ \t]*(\w+)(?:[ \t]+([^\r\n]*))?[ \t]*$',re.M)
BLOCK = re.compile(r'^[ \t]*//![ \t]*(PARAMETER|TEXTURE|SAMPLER|COMMON|PASS)(?:[ \t]+([^\r\n]*))?[ \t]*$',re.M|re.I)

class FxError(ValueError):
    def __init__(self,source,line,message):
        super().__init__(f'{source}:{line}: {message}')
        self.source,self.line,self.message=source,line,message

@dataclass(frozen=True)
class Parameter:
    name: str
    kind: str
    label: str
    default: float | int
    minimum: float | int
    maximum: float | int
    step: float | int
    declaration: str

@dataclass(frozen=True)
class Texture:
    name: str
    width: str = ''
    height: str = ''
    format: str = ''
    source: str = ''

@dataclass(frozen=True)
class Sampler:
    name: str
    filter: str
    address: str

@dataclass(frozen=True)
class Pass:
    number: int
    inputs: tuple[str,...]
    outputs: tuple[str,...]
    style: str
    block: tuple[int,int]
    threads: tuple[int,int,int]
    description: str
    code: str
    line: int

@dataclass(frozen=True)
class Effect:
    name: str
    sort_name: str
    uses: tuple[str,...]
    capabilities: tuple[str,...]
    parameters: tuple[Parameter,...]
    textures: tuple[Texture,...]
    samplers: tuple[Sampler,...]
    common: str
    passes: tuple[Pass,...]
    warnings: tuple[str,...]

    def texture(self,name):
        return next(t for t in self.textures if t.name==name)


def remove_comments(source,name='<memory>'):
    """Preserve offsets/newlines/strings and real //! lines; ignore fake metadata in /* */."""
    out=list(source);i=0
    while i<len(source):
        if source[i] in ('"',"'"):
            quote=source[i];i+=1
            while i<len(source):
                if source[i]=='\\':i+=2;continue
                if source[i]==quote:i+=1;break
                i+=1
        elif source.startswith('/*',i):
            end=source.find('*/',i+2)
            if end<0:raise FxError(name,source.count('\n',0,i)+1,'Unterminated block comment')
            for j in range(i,end+2):
                if out[j] not in '\r\n':out[j]=' '
            i=end+2
        elif source.startswith('//',i):
            end=source.find('\n',i)
            if end<0:end=len(source)
            if not source.startswith('//!',i):
                for j in range(i,end):
                    if out[j]!='\r':out[j]=' '
            i=end
        else:i+=1
    return ''.join(out)


def parse(source,name='<memory>'):
    if len(source)>32*1024*1024:raise FxError(name,1,'Effect exceeds source size limit')
    if source.startswith('\ufeff'):source=source[1:]
    source=source.replace('\r\n','\n').replace('\r','\n')
    if '\0' in source:raise FxError(name,1,'NUL in effect source')
    clean=remove_comments(source,name)
    warnings=[]
    def fail(offset,message):raise FxError(name,clean.count('\n',0,offset)+1,message)
    markers=list(BLOCK.finditer(clean))
    first=markers[0].start() if markers else len(clean)
    def options(start,end,allowed):
        values={};position=start
        while position<end:
            blank=re.match(r'\s*',clean[position:end]);position+=blank.end()
            match=DIRECTIVE.match(clean,position)
            if not match or match.end()>end:break
            key=match[1].upper();value=(match[2] or '').strip()
            if key in values:fail(match.start(),f'Duplicate {key} directive')
            values[key]=value
            if key not in allowed:warnings.append(f'{name}:{clean.count(chr(10),0,match.start())+1}: Unknown {key} directive')
            position=match.end()
        return values,position
    header,position=options(0,first,{'MAGPIE','VERSION','SORT_NAME','USE','CAPABILITY'})
    if header.get('MAGPIE')!='EFFECT':fail(0,'Expected //!MAGPIE EFFECT')
    if header.get('VERSION')!='4':fail(0,'Only MagpieFX VERSION 4 is supported')
    header_code=clean[position:first].strip()
    if header_code and not re.fullmatch(r'#include\s+"[^"\n]+"',header_code):fail(position,'Header only permits an editor include')
    def flags(value):
        if not value:return ()
        items=tuple(v.strip().upper() for v in value.split(','))
        if not all(items) or len(set(items))!=len(items):fail(0,'Empty or duplicate capability/USE flag')
        return items
    uses,capabilities=flags(header.get('USE','')),flags(header.get('CAPABILITY',''))
    for flag in uses:
        if flag not in ('MULADD','_DYNAMIC'):warnings.append(f'{name}: Unknown USE flag {flag}')
    for flag in capabilities:
        if flag!='FP16':warnings.append(f'{name}: Unknown CAPABILITY flag {flag}')
    parameters=[];textures={'INPUT':Texture('INPUT','INPUT_WIDTH','INPUT_HEIGHT','R8G8B8A8_UNORM'),'OUTPUT':Texture('OUTPUT',format='R8G8B8A8_UNORM')};samplers=[];passes=[];common='';explicit=set();symbols={'INPUT','OUTPUT'}
    def required(values,keys,offset):
        missing=set(keys)-values.keys()
        if missing:fail(offset,'Missing '+', '.join(sorted(missing)))
    def declaration(code,kind,offset):
        match=re.fullmatch(r'\s*'+kind+r'\s+('+IDENTIFIER+r')\s*;\s*',code)
        if not match:fail(offset,'Invalid '+kind+' declaration')
        return match[1]
    def numbers(value,maximum,offset):
        items=value.split(',')
        if not 1<=len(items)<=maximum or any(not re.fullmatch(r'\s*\d{1,10}\s*',s) for s in items):fail(offset,'Invalid dispatch dimensions')
        dims=tuple(int(s) for s in items)
        if any(n<1 or n>0xffffffff for n in dims):fail(offset,'Dispatch dimensions must be positive uint32')
        return dims
    for index,marker in enumerate(markers):
        tag=marker[1].upper();argument=(marker[2] or '').strip();end=markers[index+1].start() if index+1<len(markers) else len(clean)
        if tag!='PASS' and argument:fail(marker.start(),f'{tag} takes no arguments')
        allowed={'PARAMETER':{'LABEL','DEFAULT','MIN','MAX','STEP'},'TEXTURE':{'WIDTH','HEIGHT','FORMAT','SOURCE'},'SAMPLER':{'FILTER','ADDRESS'},'PASS':{'IN','OUT','STYLE','BLOCK_SIZE','NUM_THREADS','DESC'},'COMMON':set()}[tag]
        values,body_start=options(marker.end(),end,allowed)
        code=clean[body_start:end].strip();body=source[body_start:end].strip()
        if tag=='PARAMETER':
            required(values,{'DEFAULT','MIN','MAX','STEP'},marker.start())
            match=re.fullmatch(r'(float|int)\s+('+IDENTIFIER+r')\s*;',code)
            if not match:fail(body_start,'Parameters must be scalar float or int declarations')
            kind,ident=match.groups();parsed=[]
            for key in ('DEFAULT','MIN','MAX','STEP'):
                text=values[key]
                pattern=r'[+-]?\d+' if kind=='int' else r'[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?'
                if not re.fullmatch(pattern,text):fail(marker.start(),f'Invalid {kind} {key}')
                try:
                    number=int(text) if kind=='int' else struct.unpack('f',struct.pack('f',float(text)))[0]
                except (ValueError,OverflowError,struct.error):fail(marker.start(),f'Out of range {key}')
                if kind=='int' and not -2147483648<=number<=2147483647 or kind=='float' and not math.isfinite(number):fail(marker.start(),f'Out of range {key}')
                parsed.append(number)
            default,low,high,step=parsed
            if not low<=default<=high:fail(marker.start(),'Parameter default is outside MIN/MAX')
            if ident in symbols:fail(body_start,'Duplicate symbol '+ident)
            symbols.add(ident);parameters.append(Parameter(ident,kind,values.get('LABEL',ident),default,low,high,step,code))
        elif tag=='TEXTURE':
            ident=declaration(code,'Texture2D',body_start)
            if ident in explicit or ident in symbols and ident not in ('INPUT','OUTPUT'):fail(body_start,'Duplicate texture/symbol '+ident)
            explicit.add(ident);symbols.add(ident)
            width,height=values.get('WIDTH',''),values.get('HEIGHT','');fmt=values.get('FORMAT','');external=values.get('SOURCE','')
            if bool(width)!=bool(height):fail(marker.start(),'WIDTH and HEIGHT must appear together')
            if fmt and fmt not in FORMATS:fail(marker.start(),'Unknown texture FORMAT '+fmt)
            if ident=='INPUT' and any(values.get(k) for k in ('WIDTH','HEIGHT','FORMAT','SOURCE')):fail(marker.start(),'INPUT cannot have texture options')
            if ident=='OUTPUT' and (fmt or external):fail(marker.start(),'OUTPUT only supports WIDTH/HEIGHT')
            if external and (width or height):fail(marker.start(),'SOURCE cannot have WIDTH/HEIGHT')
            if ident not in ('INPUT','OUTPUT') and not (fmt or external):fail(marker.start(),'Intermediate texture needs FORMAT or SOURCE')
            if ident=='OUTPUT' and re.search(r'\bOUTPUT_(?:WIDTH|HEIGHT)\b',width+' '+height):fail(marker.start(),'OUTPUT size cannot refer to itself')
            textures[ident]=Texture(ident,width or ('INPUT_WIDTH' if ident=='INPUT' else ''),height or ('INPUT_HEIGHT' if ident=='INPUT' else ''),fmt or ('R8G8B8A8_UNORM' if ident in ('INPUT','OUTPUT') else ''),external)
        elif tag=='SAMPLER':
            required(values,{'FILTER'},marker.start());ident=declaration(code,'SamplerState',body_start)
            if ident in symbols:fail(body_start,'Duplicate symbol '+ident)
            filtering=values['FILTER'];address=values.get('ADDRESS','CLAMP')
            if filtering not in ('POINT','LINEAR') or address not in ('CLAMP','WRAP'):fail(marker.start(),'Invalid sampler FILTER/ADDRESS')
            symbols.add(ident);samplers.append(Sampler(ident,filtering,address))
        elif tag=='COMMON':
            common+=('\n' if common else '')+body
        else:
            if not re.fullmatch(r'[1-9]\d{0,9}',argument):fail(marker.start(),'PASS requires a positive index')
            required(values,{'IN','OUT'},marker.start())
            def resources(key):
                items=tuple(v.strip() for v in values[key].split(','))
                if not all(re.fullmatch(IDENTIFIER,v) for v in items) or len(set(items))!=len(items):fail(marker.start(),'Invalid/duplicate '+key+' texture')
                return items
            inputs,outputs=resources('IN'),resources('OUT');style=values.get('STYLE','CS')
            if style=='PS':
                if 'BLOCK_SIZE' in values or 'NUM_THREADS' in values:fail(marker.start(),'PS style must not specify dispatch dimensions')
                block,threads=(16,16),(64,1,1)
            elif style=='CS':
                required(values,{'BLOCK_SIZE','NUM_THREADS'},marker.start())
                block=numbers(values['BLOCK_SIZE'],2,marker.start());block=(block*2)[:2]
                threads=numbers(values['NUM_THREADS'],3,marker.start());threads=threads+(1,)*(3-len(threads))
                if math.prod(threads)>1024 or threads[2]>64:fail(marker.start(),'Thread group exceeds HLSL limits')
            else:fail(marker.start(),'STYLE must be PS or CS')
            if not code:fail(body_start,'Empty pass body')
            passes.append(Pass(int(argument),inputs,outputs,style,block,threads,values.get('DESC',f'Pass {argument}'),body,clean.count('\n',0,body_start)+1))
    passes.sort(key=lambda p:p.number)
    if not passes or [p.number for p in passes]!=list(range(1,len(passes)+1)):fail(0,'PASS indices must be contiguous from 1')
    for index,p in enumerate(passes):
        def bad(message):raise FxError(name,p.line,message)
        if any(t not in textures for t in p.inputs+p.outputs):bad('Pass refers to an undeclared texture')
        if 'OUTPUT' in p.inputs or 'INPUT' in p.outputs:bad('INPUT is read-only and OUTPUT is write-only')
        if set(p.inputs)&set(p.outputs):bad('Pass input/output textures must be distinct')
        if any(textures[t].source for t in p.outputs):bad('SOURCE textures are read-only')
        if len(p.outputs)>8:bad('Pass has more than 8 outputs')
        if index==len(passes)-1:
            if p.outputs!=('OUTPUT',):bad('Last pass must write only OUTPUT')
        elif 'OUTPUT' in p.outputs:bad('Only the last pass can write OUTPUT')
    return Effect(name,header.get('SORT_NAME',name),uses,capabilities,tuple(parameters),tuple(textures.values()),tuple(samplers),common,tuple(passes),tuple(warnings))


class SizeExpression:
    """Bounded arithmetic dimension expressions; no Python execution or attribute access.

    The current built-in corpus only uses constants and arithmetic. Additional
    muParser syntax must be implemented explicitly, rather than guessed or executed.
    """
    def __init__(self,text,name='<expression>',line=1):
        import ast
        self.name,self.line=name,line
        if not text or len(text)>4096 or '**' in text:raise FxError(name,line,'Invalid size expression')
        try:self.tree=ast.parse(text.strip().replace('^','**'),mode='eval').body
        except (SyntaxError,RecursionError) as error:raise FxError(name,line,'Invalid size expression') from error
        nodes=list(ast.walk(self.tree))
        if len(nodes)>256:raise FxError(name,line,'Size expression is too complex')
        functions={'abs':abs,'min':lambda *values:min(values),'max':lambda *values:max(values),'floor':math.floor,'ceil':math.ceil,'sqrt':math.sqrt}
        self.functions=functions
        def check(node,depth=0):
            if depth>48:raise FxError(name,line,'Size expression is too deep')
            if isinstance(node,ast.Constant) and type(node.value) in (int,float):return
            if isinstance(node,ast.Name) and node.id in ('INPUT_WIDTH','INPUT_HEIGHT','OUTPUT_WIDTH','OUTPUT_HEIGHT'):return
            if isinstance(node,ast.UnaryOp) and isinstance(node.op,(ast.UAdd,ast.USub)):check(node.operand,depth+1);return
            if isinstance(node,ast.BinOp) and isinstance(node.op,(ast.Add,ast.Sub,ast.Mult,ast.Div,ast.Pow)):
                check(node.left,depth+1);check(node.right,depth+1);return
            if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id in functions and node.args and not node.keywords:
                if node.func.id not in ('min','max') and len(node.args)!=1:raise FxError(name,line,'Invalid function arity')
                for arg in node.args:check(arg,depth+1)
                return
            raise FxError(name,line,'Unsupported size expression syntax')
        check(self.tree)

    def evaluate(self,constants):
        import ast
        def visit(node):
            if isinstance(node,ast.Constant):value=float(node.value)
            elif isinstance(node,ast.Name):value=float(constants[node.id])
            elif isinstance(node,ast.UnaryOp):value=visit(node.operand)*(-1 if isinstance(node.op,ast.USub) else 1)
            elif isinstance(node,ast.Call):value=self.functions[node.func.id](*(visit(arg) for arg in node.args))
            else:
                a,b=visit(node.left),visit(node.right)
                if isinstance(node.op,ast.Add):value=a+b
                elif isinstance(node.op,ast.Sub):value=a-b
                elif isinstance(node.op,ast.Mult):value=a*b
                elif isinstance(node.op,ast.Div):value=a/b
                else:value=math.pow(a,b)
            if not math.isfinite(value):raise ValueError('Nonfinite size expression')
            return value
        try:value=visit(self.tree)
        except (KeyError,ValueError,TypeError,OverflowError,ZeroDivisionError) as error:raise FxError(self.name,self.line,'Size expression evaluation failed') from error
        # The renderer uses lround, not banker's rounding or truncation.
        result=math.floor(value+0.5) if value>=0 else math.ceil(value-0.5)
        if not 1<=result<=16384:raise FxError(self.name,self.line,'Texture dimension exceeds 1..16384')
        return result


def texture_sizes(effect,input_size,requested_output):
    """Resolve built-in expression syntax. SOURCE sizes require an actual asset loader."""
    if any(type(v) is not int or not 1<=v<=16384 for v in (*input_size,*requested_output)):
        raise FxError(effect.name,1,'Invalid input/requested output dimensions')
    constants=dict(zip(('INPUT_WIDTH','INPUT_HEIGHT'),input_size))
    out=effect.texture('OUTPUT')
    output=tuple(SizeExpression(e,effect.name).evaluate(constants) for e in (out.width,out.height)) if out.width else tuple(requested_output)
    constants.update(zip(('OUTPUT_WIDTH','OUTPUT_HEIGHT'),output))
    result={'INPUT':tuple(input_size),'OUTPUT':output}
    for t in effect.textures[2:]:
        result[t.name]=None if t.source else tuple(SizeExpression(e,effect.name).evaluate(constants) for e in (t.width,t.height))
    return result
