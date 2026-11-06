# Synthetic FREQ-a compatibility cases for the exact report adaptation.
# Usage: CPython 3.12 generate_x23se_vectors.py trusted-reference.exe output.json
import sys
base=sys.argv[0].replace('\\','/').rsplit('/',1)[0]+'/generate_vectors.py'
bootstrap=open(base,encoding='utf-8-sig').read().split('# Synthetic inputs')[0]
exec(compile(bootstrap,base,'exec'))
import json
_,at,length=toc['wireless_x23_family']
module=marshal.loads(zlib.decompress(b[at:at+length]))
code={c.co_name:c for c in module.co_consts if isinstance(c,types.CodeType)}
globals_={'InvalidReply':QueryError,'MetadataFailed':QueryError,'Reading':protocols.Reading,'__builtins__':__builtins__}
decode=types.FunctionType(code['freq_decode'],globals_)
# 65/65 + dual uses a. Probe the actual reference transaction with an in-memory link.
writes=[];drains=[];responses=[]
def raw(percent,charge,marker=0xAA,offset=0,input_id=7):
    b=bytearray(65);b[0]=input_id;b[1+offset]=marker;b[2+offset]=0x30;b[9+offset]=percent;b[10+offset]=charge;return bytes(b)
def wait(h,size,predicate,timeout=1.2):
    data=responses.pop(0)
    assert len(data)==size and predicate(data)
    return data
globals_.update(freq_decode=decode,drain=lambda h,*args:drains.append(1),write=lambda h,packet:writes.append(bytes(packet)),_wait=wait)
read=types.FunctionType(code['_freq_read'],globals_)
responses.extend([raw(17,0),raw(42,1)])
records=[]
result=read(object(),{'freq_scheme':'dual'},{(8,7):65,(9,42):65},records)
assert result.percent==42 and result.state=='charging' and len(writes)==2 and writes[0]==writes[1]
vectors=[]
def add(data,label):
    try:
        r=decode(data,'a',7,65)
        accepted=True;level=r.percent;charge=r.state=='charging'
    except QueryError:
        accepted=False;level=-1;charge=False
    vectors.append(dict(hex=bytes(data).hex().upper(),label=label,accepted=accepted,level=level,charging=charge))
for marker in (0xAA,0xF0):
    for offset in (0,1):
        for percent in range(102):
            for charge in (0,1,2):
                add(raw(percent,charge,marker,offset),f'marker-{marker}-offset-{offset}-level-{percent}-charge-{charge}')
for index in (0,1,2,9,10):
    for value in (0,1,7,100,101,255):
        data=bytearray(raw(42,1));data[index]=value;add(data,f'byte-{index}-{value}')
for length in (0,1,2,10,64):add(raw(42,1)[:length],f'short-{length}')
add(writes[0],'request-echo')
output=dict(sourceSha256=expected,sourceModule='wireless_x23_family.py',
            evidence='Upstream module explicitly says protocol-supported, not physically verified. All inputs here are synthetic.',
            inputId=7,outputId=42,inputSize=65,outputSize=65,requestHex=writes[0].hex().upper(),
            transactionWrites=len(writes),transactionResult=result.percent,vectors=vectors)
with open(sys.argv[2],'w',encoding='utf8') as file:
    file.write(json.dumps({k:v for k,v in output.items() if k!='vectors'},ensure_ascii=False,indent=2)[:-2]+',\n  "vectors": [\n')
    for i,vector in enumerate(vectors):file.write('    '+json.dumps(vector,ensure_ascii=False,separators=(',',':'))+(',\n' if i+1<len(vectors) else '\n'))
    file.write('  ]\n}\n')
print('FREQ-a original oracle:',len(vectors),'reports; transaction writes:',len(writes))
