"""Read-only local driver/catalogue audit. Never imports or executes driver code.
Usage: python audit_drivers.py REPO OUTPUT REFERENCE_JSON EVIDENCE_ROOT
Output is local evidence, not a claim of physical-device compatibility.
"""
import csv, hashlib, json, os, pathlib, sys

repo, output, reference, evidence = map(pathlib.Path, sys.argv[1:5])
output.mkdir(parents=True, exist_ok=True)
roots = [pathlib.Path('D:/') / name for name in
         ['飓风','HyperX','狼蛛','雷柏','罗技','罗技耳机驱动','迈从','瓦尔基里','维迦','西伯利亚','英菲克','AJAZZ','ATK','VGN VHUB','rainy 75']]
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest()
def write_csv(name,rows,fields):
    with (output/name).open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fields);w.writeheader();w.writerows(rows)

inventory=[];summaries=[]
skip={'.git','obj','node_modules','__pycache__','3-MouseDock 源码','3-MouseDock源码'}
for root in roots:
    total=stubs=pe=0
    if root.exists():
        for base,dirs,files in os.walk(root):
            dirs[:]=[d for d in dirs if d not in skip]
            for name in files:
                p=pathlib.Path(base)/name
                if p.suffix.lower() not in {'.exe','.dll','.asar','.js','.json','.ini','.xml','.bin','.rfw','.zip'}:continue
                try:
                    size=p.stat().st_size;total+=1
                    state='可读数据；未证明协议兼容'
                    if size<=1:state='空文件/占位，不能验证';stubs+=1
                    elif p.suffix.lower() in {'.exe','.dll'}:
                        with p.open('rb') as f:header=f.read(2)
                        if header==b'MZ':state='MZ头可读；未验证完整PE结构/未执行';pe+=1
                        else:state='非有效PE头，不能验证'
                    inventory.append(dict(root=str(root),path=str(p),bytes=size,status=state))
                except OSError as e:inventory.append(dict(root=str(root),path=str(p),bytes='',status=type(e).__name__))
    summaries.append(dict(root=str(root),files=total,placeholders=stubs,readable_pe=pe))
write_csv('D盘驱动文件清单.csv',inventory,['root','path','bytes','status'])

# Revalidate retained REA target identities; local paths/hashes remain outside git.
checks=[];seen=set()
for p in evidence.rglob('*.json'):
    if p.stat().st_size>12*1024*1024 or any(x in skip or x in {'bin','packages','.store'} for x in p.parts):continue
    try:
        data=json.loads(p.read_text(encoding='utf-8-sig'))
        if not isinstance(data,dict):continue
        subject=data.get('subject',{});digest=subject.get('digest',{}).get('sha256')
        path=subject.get('local_path')
        if not digest or not path or (path,digest) in seen:continue
        seen.add((path,digest));target=pathlib.Path(path)
        actual=sha(target) if target.is_file() else ''
        checks.append(dict(target=path,expected=digest,actual=actual,status='同一目标哈希匹配' if actual.lower()==digest.lower() else '缺失/哈希不匹配',record=str(p)))
    except (ValueError,OSError,AttributeError):continue
known={
 'D:/ATK/official-web-20261105/filter-X9KWEZ6_.js':'cb83c8da792647a89696d45334dbfbdb1f9fdd90eeb9e35a3757aaabd908b034',
 'D:/ATK/official-web-20261105/keyboard-info.util-vnAWcNN2.js':'f3589c6c805856a2f0f4b197534dd187b18ec63ca9a4cfaf7a8eed19d8aee640',
 'D:/迈从/官网网页驱动/main-DCRwBIvm.js':'405102805e2adeb93073ee2186f6d0193ca1317a6b4657d636e4e611bd25a446',
 'D:/迈从/官网网页驱动/gmouse-610.4e361f3235ebcfe0.js':'23380f0f753ee06f92d153afeb891cac385b12f860185cb3f4ac8f0b95e99017',
 'D:/罗技/GPW/pro_wireless_mouse_depot.depot':'38ee94842a6d385e8a2ac7aa2e66ca35a60488c8eef2260b10cff004eb627714',
 'D:/rainy 75/official-download-20261105/Rainy75-RGB-Driver-beta.exe':'eebb52118d66766b37b5d0c1da2c416482cbfc60764170383f10788509fd2383',
 'D:/飓风/本机-NGENUITY-3.1.0/current/Assets/Services/Libs/Cloud3S/AirohaHidCoreDll.dll':'fc59cdf51e9376f28cf9442392f6a98654f3405e67e9b8a56e3db655761102b6',
}
for path,expected in known.items():
    target=pathlib.Path(path);actual=sha(target) if target.is_file() else ''
    checks.append(dict(target=path,expected=expected,actual=actual,status='同一目标哈希匹配' if actual==expected else '缺失/哈希不匹配',record='既有官方来源记录/本项目协议证据'))
write_csv('既有分析目标哈希复核.csv',checks,['target','expected','actual','status','record'])

db=json.loads((repo/'docs/device-db/mouse-db.json').read_text(encoding='utf-8-sig'))
groups={'merged':json.loads((repo/'docs/device-db/devices-merged.json').read_text(encoding='utf-8-sig'))}
groups.update({k:db[k] for k in ['devices','oss','receivers','readers']})
if reference.is_file():
    ref=json.loads(reference.read_text(encoding='utf-8-sig'))
    for k in ['devices','wireless_keyboards','wireless_receivers','wireless_usb']:
        if isinstance(ref.get(k),list):groups['reference-1.0.2/'+k]=ref[k]
def number(x):
    if isinstance(x,int):return x
    try:return int(str(x),16)
    except (ValueError,TypeError):return 0
rows=[]
for source,items in groups.items():
    for i,item in enumerate(items):
        if not isinstance(item,dict):continue
        vid=item.get('vid',item.get('vendor_id',item.get('vendor')))
        pid=item.get('pid',item.get('product_id',item.get('product')))
        pairs=[(vid,pid)]
        if isinstance(item.get('products'),list):pairs=[(vid,p) for p in item['products']]
        elif isinstance(item.get('vid_pid'),list) and item['vid_pid']:
            pairs=[(p.get('vid'),p.get('pid')) for p in item['vid_pid'] if isinstance(p,dict)] or pairs
        for v,p in pairs:
            rows.append(dict(source=source,index=i,name=item.get('name') or item.get('device') or item.get('model') or item.get('kind') or item.get('protocol',''),
                vid=number(v),pid=number(p),declared_protocol=item.get('battery') or item.get('protocol') or item.get('driver',''),
                upstream_declared_verified=bool(item.get('verified',False))))
(output/'catalogue-input.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
(output/'审计库存摘要.json').write_text(json.dumps(dict(drivers=summaries,source_rows={k:len(v) for k,v in groups.items()},expanded_cases=len(rows),retained_targets=len(checks),matching_targets=sum(r['status']=='同一目标哈希匹配' for r in checks)),ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(dict(files=len(inventory),placeholders=sum(r['placeholders'] for r in summaries),catalogue_rows=len(rows),target_checks=len(checks),target_matches=sum(r['status']=='同一目标哈希匹配' for r in checks)),ensure_ascii=False))
