# Regenerates synthetic protocol fixtures from the trusted, user-supplied 1.0.2 binary.
# CPython 3.12 is required. It does not launch the application or load a HID provider.
# Usage: python3.12 generate_vectors.py reference.exe output.json
import sys,marshal,zlib,types
from _frozen_importlib import ModuleSpec
from _sha2 import sha256
if sys.version_info[:2] != (3,12):
    raise RuntimeError('Use CPython 3.12 for the reference bytecode')
if len(sys.argv) != 3:
    raise RuntimeError('Usage: generate_vectors.py reference.exe output.json')
source=open(sys.argv[1],'rb').read()
expected='DE9A0C6B8680ADE90CBC9B1A82DBDBAACAD88C302176D52099327ED75838BD8A'
if sha256(source).hexdigest().upper() != expected:
    raise RuntimeError('Reference SHA256 mismatch; refusing to execute different bytecode')
cookie=source.rfind(b'MEI\x0c\x0b\x0a\x0b\x0e')
if cookie < 0: raise RuntimeError('No PyInstaller cookie')
number=lambda offset: int.from_bytes(source[offset:offset+4],'big')
base=cookie+88-number(cookie+8)
pos=base+number(cookie+12)
end=pos+number(cookie+16)
members={}
while pos < end:
    size=number(pos)
    if size < 18 or pos+size > end: raise RuntimeError('Invalid archive inventory')
    name=source[pos+18:pos+size].rstrip(bytes([0])).decode()
    if name in ('PYZ.pyz','devices.json'):
        start=base+number(pos+4)
        raw=source[start:start+number(pos+8)]
        members[name]=zlib.decompress(raw) if source[pos+16] else raw
    pos+=size
if len(members) != 2: raise RuntimeError('Missing PYZ or device database')
b=members['PYZ.pyz']
toc=dict(marshal.loads(b[int.from_bytes(b[8:12],'big'):]))
class Loader:
    def find_spec(self,name,path=None,target=None):
        if name not in toc: return None
        return ModuleSpec(name,self,is_package=toc[name][0]==1)
    def create_module(self,spec): return None
    def exec_module(self,module):
        _,at,length=toc[module.__name__]
        module.__file__='reference/'+module.__name__+'.py'
        exec(marshal.loads(zlib.decompress(b[at:at+length])),module.__dict__)
sys.meta_path.insert(0,Loader())
query=types.ModuleType('query_errors')
class QueryError(Exception): pass
query.ShortWrite=query.InvalidReply=QueryError
query.open_device=lambda *args: (_ for _ in ()).throw(RuntimeError('Hardware disabled'))
sys.modules['query_errors']=query
hid=types.ModuleType('hid_transport')
hid.get_device=lambda: (_ for _ in ()).throw(RuntimeError('Hardware disabled'))
sys.modules['hid_transport']=hid
import protocols
# Synthetic inputs with expected results obtained from the original pure decoder.
# No device enumeration, writes, user settings, or cached readings are accessible.
import json
import dpi
database=json.loads(members['devices.json'].decode('utf-8-sig'))
vectors=[]
def add(kind, data, label):
    try:
        reading=protocols.decode(kind,bytes(data))
        accepted=True
        level=reading.percent if reading.percent is not None else -1
        state=reading.state
        charging=state=='charging'
        known=state in ('online','charging') and kind!='scyrox12'
    except ValueError:
        accepted=False;level=-1;state='invalid';charging=False;known=False
    vectors.append(dict(protocol=kind,hex=bytes(data).hex().upper(),accepted=accepted,
                        level=level,state=state,charging=charging,chargingKnown=known,label=label))
def rapoo(pct,flag=1,n=20):
    b=bytearray(n)
    if n==7:b[0]=0xBB;b[1]=0xB0;b[5]=flag;b[6]=pct
    else:b[0]=7;b[1]=1;b[7]=flag;b[8]=pct
    return b
for pct in range(102):
    for n in (7,19,20):
        for flag in (0,1,2):add('rapoo',rapoo(pct,flag,n),f'percent-{pct}-status-{flag}-length-{n}')
base=rapoo(42)
for index in (0,1,7,8):
    for value in (0,1,2,3,4,5,6,7,8,9,15,100,101,127,128,254,255):
        b=bytearray(base);b[index]=value
        add('rapoo',b,f'byte-{index}-value-{value}')
for length in (0,1,2,6,7,16,len(base)-1):
    add('rapoo',base[:length],f'truncated-{length}')
for pct in (0,42,100,255):
    add('rapoo',rapoo(pct,0),'sleep-must-not-use-battery')
output=dict(source='MouseDock-1.0.2.exe',sourceSha256='DE9A0C6B8680ADE90CBC9B1A82DBDBAACAD88C302176D52099327ED75838BD8A',
            deviceDbSha256=sha256(members['devices.json']).hexdigest().upper(),
            identityNames=database['brand_ids']['atk_identity']['compx_cid_mid'],
            verifiedProfiles=[{k:p[k] for k in ('vid','pid','page','usage','battery')} for p in database['devices'] if p.get('verified') is True],
            method='Original Rapoo decoder under bundled CPython 3.12; hardware entrypoints disabled; synthetic reports, not hardware captures.',
            vectors=vectors)
path=sys.argv[2]
with open(path,'w',encoding='utf8') as f:json.dump(output,f,ensure_ascii=False,indent=2)
print('Rapoo oracle vectors:',len(vectors))
