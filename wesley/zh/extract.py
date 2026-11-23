"""Read only user-facing fields from installed Unity bundles. Cache by content digest."""
import argparse, collections, hashlib, json, pathlib, re, sys
ap=argparse.ArgumentParser()
ap.add_argument('plugins',type=pathlib.Path);ap.add_argument('cache',type=pathlib.Path)
ap.add_argument('--unitypy',type=pathlib.Path)
ap.add_argument('--media-fingerprints', action='store_true', help='Include versioned VideoClip and AudioClip identities for subtitle coverage.')
args=ap.parse_args()
if args.unitypy: sys.path.insert(0,str(args.unitypy))
import UnityPy
from UnityPy.helpers.ResourceReader import get_resource_data
args.cache.mkdir(parents=True,exist_ok=True)
def sha(path):
    with path.open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()
fields={'m_text','headerText','subText','hoverTip','holdTip','disabledHoverTip','dialogue','caption','subtitle','hintText','promptText','description','displayText','creatureFile','infoNodeDescription','terminalInfoOverride','levelDescription','PlanetName','itemName','enemyName','toolTips','dungeonName','dungeonDisplayName','DungeonName','overrideDungeonName','loreText','dialogueText','messageText','weatherDisplayName'}
low={x.lower() for x in fields}
extra={"triggerdisplaytext","enabledtriggername","disabledtriggername","triggerstring","cursedmessages","cursedmessagesspawned","modifiermessages","header","message","weathername"}
def fieldname(k):return k[1:-16] if k.startswith("<") and k.endswith(">k__BackingField") else k
def allowed(k):
 k=fieldname(k)
 return k.lower() in low|extra or bool(re.search(r'(?:tooltip|description|subtitle|dialogue|hovertip|caption|hinttext|displayname|holdtip|messagetext)$',k,re.I))
def visit(v,k='',trail=''):
    if isinstance(v,dict):
        for key,value in v.items(): yield from visit(value,fieldname(key),trail+'.'+fieldname(key))
    elif isinstance(v,list):
        for i,value in enumerate(v):yield from visit(value,k,trail+'[]')
    elif isinstance(v,str) and allowed(k) and v.strip() and re.search('[A-Za-z]',v):yield trail.lstrip('.'),v
def fingerprint(o):
    t=o.type.name
    media=o.read_typetree()
    if t=='VideoClip':
        identity=media['m_FrameCount']
        milliseconds=round(identity*1000/media['m_FrameRate'])
        resource=media['m_ExternalResources']
    else:
        identity=milliseconds=round(media['m_Length']*1000)
        resource=media['m_Resource']
    raw=(get_resource_data(resource['m_Source'],o.assets_file,resource['m_Offset'],resource['m_Size'])
        if resource['m_Size'] else bytes(media.get('m_AudioData',[])))
    return [t[0],media['m_Name'],identity,milliseconds,hashlib.sha256(raw).hexdigest()]

def parse(p,rel):
    sig=sha(p);dest=args.cache/(sig+'.json')
    if dest.exists() and (not args.media_fingerprints or 'clips' in json.loads(dest.read_text('utf8'))):return
    if p.suffix=='.json':
        f=json.loads(p.read_text('utf-8-sig'));rows=[]
        for i,obj in f['logs'].items():
            for k in ('name','text'): rows.append([str(i),k,obj[k]])
        out=dict(rows=rows,media=[],errors=[])
    else:
        rows=[];media=[];clips=[];errors=collections.Counter();cache={}
        env=UnityPy.load(str(p))
        for o in env.objects:
            t=o.type.name
            if t in ['VideoClip','AudioClip','Texture2D','TextAsset']:
                name=o.peek_name() or ''
                media.append([t,str(o.path_id),name])
                if args.media_fingerprints and t in ('VideoClip','AudioClip'):
                    try:clips.append(fingerprint(o))
                    except Exception as e:errors['media '+str(type(e).__name__)]+=1
                if t=='TextAsset':
                    # Leave non-UI or opaque data out of the runtime dictionary.
                    pass
            if t!='MonoBehaviour':continue
            st=o.serialized_type
            if st is None or not st.node: errors['missing type tree']+=1;continue
            key=id(st)
            if key not in cache:cache[key]=any(allowed(n.m_Name) for n in st.node.traverse())
            if not cache[key]: continue
            try:d=o.read_typetree()
            except Exception as e: errors[str(type(e).__name__)]+=1;continue
            for k,v in visit(d):
                if k.endswith('EnabledTriggerName'):v=v+' : [E]'
                if k.endswith('TriggerString') and isinstance(d.get('InteractEntries'),list):
                    count=len(d['InteractEntries'])
                    for step in range(count+1):rows.append([str(o.path_id),k+' (progress)',f'{v} ({step}/{count}) : [E]'])
                else:rows.append([str(o.path_id),k,v])
        out=dict(rows=rows,media=media,errors=errors)
        if args.media_fingerprints:out['clips']=clips
    out.update(source=rel,sha256=sig)
    dest.with_suffix('.tmp').write_text(json.dumps(out,ensure_ascii=False),encoding='utf8');dest.with_suffix('.tmp').replace(dest)
    print(rel,len(out['rows']),flush=True)
files=sorted(args.plugins.glob('CustomStoryLogs/logs/*.json'))
dirs=sorted(args.plugins.glob('Magic_Wesley-*'),key=lambda p:('Moons' in p.name,p.name))
for d in dirs:
    for p in sorted(d.rglob('*')):
        if p.is_file() and p.suffix.lower() not in ('.dll','.json','.md','.cfg','.txt','.png','.xml','.ogg','.wav','.jpg','.pdb'):
            with p.open('rb') as f:magic=f.read(7)
            if magic==b'UnityFS':files.append(p)
for p in files:
    try:parse(p,str(p.relative_to(args.plugins)).replace('\\','/'))
    except Exception as e:print('FAIL',p,repr(e),flush=True)
