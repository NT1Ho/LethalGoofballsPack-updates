"""Inventory exact Item prefab names and serialized submission components, without exporting assets."""
import sys,pathlib,json,hashlib
sys.path.insert(0,r'D:/LC Universal Wrench/tools/asset-inspection')
import UnityPy
here=pathlib.Path(__file__).resolve().parent
dstdir=here/'quest-cache'; dstdir.mkdir(exist_ok=True)
packs=json.loads((here/'packages.lock.json').read_text())
def read_bundle(path,digest):
    dest=dstdir/(digest+'.json')
    if dest.exists(): return
    env=UnityPy.load(str(path)); rows=[];bytype={}
    def ref(o,d):
        fid=d['m_FileID']; pid=d['m_PathID']
        cab=o.assets_file.name if fid==0 else o.assets_file.externals[fid-1].path.replace('\\','/').split('/')[-1]
        return [cab,pid]
    def refname(o,pp):
        if not pp['m_PathID']:return ''
        if pp['m_FileID']==0:
            obj=o.assets_file.objects.get(pp['m_PathID'])
            if obj is not None: return obj.peek_name() or ''
        return ''
    for o in env.objects:
        if o.type.name!='MonoBehaviour' or not o.serialized_type.node: continue
        st=id(o.serialized_type)
        if st not in bytype:
            ns={n.m_Name for n in o.serialized_type.node.m_Children}
            bytype[st]='item' if {'itemName','spawnPrefab','isScrap'}<=ns else 'cassette' if {'LevelSceneName','LoaderName','unlockLevel'}<=ns else 'consume' if {'validItems','validItemFilters','OnSuccess'}<=ns else None
        k=bytype[st]
        if not k: continue
        t=o.read_typetree();base=dict(kind=k,id=[o.assets_file.name,o.path_id],go=ref(o,t['m_GameObject']),name=refname(o,t['m_GameObject']))
        if k=='item':
            base.update(item=t['itemName'],prefab=ref(o,t['spawnPrefab']),prefabName=refname(o,t['spawnPrefab']),scrap=t['isScrap'])
        if k=='cassette':
            base.update(loader=t['LoaderName'],scene=t['LevelSceneName'],unlocks=t['unlockLevel'])
        if k=='consume':
            base.update(exact=t['checkExact'],accept=[ref(o,x) for x in t['validItems']],filters=t['validItemFilters'],events=[x.get('m_MethodName','') for x in t['OnSuccess']['m_PersistentCalls']['m_Calls']])
        rows.append(base)
    dest.write_text(json.dumps(dict(bundle=path.name,rows=rows),ensure_ascii=False),encoding='utf8')
for v in packs.values():
    if not v['package'].startswith('Magic_Wesley'):continue
    for entry in v['files']:
        p=pathlib.Path(entry['path'])
        if p.suffix!='.lethalbundle':continue
        try:read_bundle(here.parent/'candidate'/p,entry['sha256'])
        except Exception as ex:print('Failed',p,str(ex),flush=True)
