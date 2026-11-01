from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from collections import defaultdict
import json,subprocess,hashlib,sys
HERE=Path(__file__).resolve().parent
INFO=Path(r'D:/STEAM/steamapps/common/Lethal Company/BepInEx/pack-info')
meta=HERE/'meta'; cache=HERE/'cache'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def ver(v): return tuple(map(int,v.split('.')))
def download(url,p):
    part=p.with_suffix(p.suffix+'.part')
    subprocess.run(['curl.exe','--retry','2','--connect-timeout','15','-fsSL','-C','-','-o',str(part),url],check=True)
    part.replace(p)
def getjson(url,p):
    if not p.exists(): download(url,p)
    return read(p)
available={p.parent.name.lower():(read(p),p.parent.name) for p in INFO.glob('*/manifest.json') if 'version_number' in read(p)}
sources={i['package'].lower():i for i in read(INFO/'download-sources.json')}
roots=['Magic_Wesley-'+s for s in ('Wesleys_Moons','WesleysInteriors','Facto','Decrepit_Store')]
plan={};pending=dict.fromkeys(roots,'0.0.0')
while pending:
    def resolve(row):
        key,required=row
        owner,name=key.split('-',1)
        upstream=getjson(f'https://thunderstore.io/api/experimental/package/{owner}/{name}/',meta/(key+'.json'))
        if upstream['is_deprecated']: print('DEPRECATED',key,flush=True)
        newest=upstream['latest']
        local,orig=available.get(key.lower(),({},''))
        have=local.get('version_number')
        if have and ver(have)>=ver(required) and key not in roots:
            version=have;deps=local['dependencies'];kind='reuse';url=f'https://thunderstore.io/package/download/{owner}/{name}/{have}/'
        else:
            version=newest['version_number'];deps=newest['dependencies'];kind='add' if not have else 'replace';url=newest['download_url']
            if ver(version)<ver(required):raise Exception((key,version,required))
        return key,dict(package=key,version=version,official_latest=newest['version_number'],installed=have,action=kind,dependencies=deps,url=url,page=f'https://thunderstore.io/c/lethal-company/p/{owner}/{name}/',source=newest.get('website_url',''),archive_sha256=sources.get(key.lower(),{}).get('sha256') if kind=='reuse' and sources.get(key.lower(),{}).get('version')==version else None)
    new=dict(ThreadPoolExecutor(6).map(resolve,pending.items()))
    plan.update(new); pending={}
    for item in plan.values():
        for d in item['dependencies']:
            key,want=d.rsplit('-',1)
            if key not in plan or ver(plan[key]['version'])<ver(want):
                pending[key]=max((want,pending.get(key,'0.0.0')),key=ver)
    print('Resolved',[(p,v['version'],v['action']) for p,v in new.items()],flush=True)
out=HERE/'plan.json';out.write_text(json.dumps(plan,indent=2,ensure_ascii=False),encoding='utf-8')
def fetch(row):
    key,x=row
    if x['action']=='reuse': return
    dest=cache/(key+'-'+x['version']+'.zip')
    if not dest.exists():download(x['url'],dest)
    print('READY',dest.name,dest.stat().st_size,flush=True)
list(ThreadPoolExecutor(5).map(fetch,plan.items()))
