"""Stage selected Thunderstore files without mutating Steam. Archive CRC checked while streaming."""
from pathlib import Path,PurePosixPath
from concurrent.futures import ThreadPoolExecutor
import json,hashlib,zipfile,os
HERE=Path(__file__).resolve().parent
ROOT=HERE.parent/'candidate';STEAM=Path(r'D:/STEAM/steamapps/common/Lethal Company')
def load(f):return json.loads(f.read_text(encoding='utf-8-sig'))
def sha(f):
    with f.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def target(pkg,n):
    parts=PurePosixPath(n).parts
    if not parts or any(p in ('.','..','/') or ':' in p or '\\' in p for p in parts):raise ValueError(n)
    if n.lower().startswith('bepinex/'):return Path('BepInEx',*parts[1:])
    if parts[0].lower() in ('plugins','patchers','config','core'):return Path('BepInEx',*parts)
    if len(parts)==1 and (n.lower().endswith(('.md','.txt','.png')) or n in ('LICENSE','manifest.json')):return Path('BepInEx','pack-info',pkg,n)
    return Path('BepInEx','plugins',pkg,*parts)
def run():
    plan=load(HERE/'plan.json')
    inventory=load(STEAM/'BepInEx/pack-info/original-files.json')
    claims=set()
    for k,v in plan.items():
        if v['action']=='reuse':continue
        archive=HERE/'cache'/(k+'-'+v['version']+'.zip')
        with zipfile.ZipFile(archive) as z:
            manifest=json.loads(z.read('manifest.json').decode('utf-8-sig'))
            assert manifest['version_number']==v['version'] and sorted(manifest['dependencies'])==sorted(v['dependencies']),k
            for e in z.infolist():
                if e.is_dir():continue
                rel=target(k,e.filename); p=(ROOT/rel).resolve()
                assert p.is_relative_to(ROOT.resolve()) and str(p).casefold() not in claims, str(p)
                claims.add(str(p).casefold())
                if (STEAM/rel).exists():raise ValueError(f'Baseline path already exists: {rel}')
    def process(pair):
        k,v=pair
        if v['action']=='reuse':
            v['files']=[]
            for row in inventory:
                if row['package'].casefold()!=k.casefold():continue
                n=row['path'];p=STEAM/n
                if p.is_file() and (p.suffix.lower()=='.dll' or p.name=='manifest.json'):
                    v['files'].append({'path':n,'sha256':sha(p),'size':p.stat().st_size})
            v['reason']='Existing version satisfies all manifest requirements; actual installed DLL retained.'
            return
        v['reason']='User-requested content' if k in ['Magic_Wesley-Wesleys_Moons','Magic_Wesley-WesleysInteriors','Magic_Wesley-Facto','Magic_Wesley-Decrepit_Store'] else 'Manifest dependency'
        archive=HERE/'cache'/(k+'-'+v['version']+'.zip')
        v['archive_sha256']=sha(archive);v['archive_size']=archive.stat().st_size;v['files']=[]
        with zipfile.ZipFile(archive) as z:
            for e in z.infolist():
                if e.is_dir():continue
                rel=target(k,e.filename);f=ROOT/rel;f.parent.mkdir(parents=True,exist_ok=True)
                h=hashlib.sha256();size=0
                tmp=f.with_suffix(f.suffix+'.partial')
                with z.open(e) as src,tmp.open('wb') as out:
                    while chunk:=src.read(4<<20):
                        size+=len(chunk);h.update(chunk);out.write(chunk)
                assert size==e.file_size
                os.replace(tmp,f)
                v['files'].append({'path':rel.as_posix(),'sha256':h.hexdigest(),'size':size})
        print('STAGED',k,len(v['files']),flush=True)
    list(ThreadPoolExecutor(4).map(process,plan.items()))
    (HERE/'packages.lock.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':run()
