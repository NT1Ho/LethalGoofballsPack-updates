"""Inventory serialized Wesley submissions, unlocks and direct item spawns. No Unity/game assets are exported."""
import argparse,collections,gc,hashlib,json,pathlib,sys
ap=argparse.ArgumentParser()
ap.add_argument("plugins",type=pathlib.Path);ap.add_argument("output",type=pathlib.Path)
ap.add_argument("--unitypy",default="D:/LC Universal Wrench/tools/asset-inspection")
args=ap.parse_args();sys.path.insert(0,args.unitypy)
import UnityPy
from UnityPy.classes.PPtr import PPtr
def resolve(o,d):return PPtr(**d,assetsfile=o.assets_file).deref()
def deref_name(o,d):
    if not d or not d.get("m_PathID"):return ""
    try:return resolve(o,d).read_typetree()["m_Name"]
    except (ValueError,FileNotFoundError,KeyError):return ""
def file_sha(p):
    with p.open("rb") as f:return hashlib.file_digest(f,"sha256").hexdigest()
def collect(p):
    env=UnityPy.load(str(p));rows=[];cache={}
    for o in env.objects:
        if o.type.name!="MonoBehaviour":continue
        node=o.serialized_type.node
        if not node:continue
        if id(node) not in cache:cache[id(node)]={c.m_Name for c in node.m_Children}
        keys=cache[id(node)]
        if not keys.intersection({"IntegratedLevels","LevelSceneName","TapeFinishedEvents","itemName","PlanetName","validItems","SourcePool","SceneName","NumberlessPlanetName","InsertedApps","sceneName","DungeonName","RequiredItems","unlockLevel","hasUnlocked","ProgressionKey","HideAndLockMoons","routeWord"}):continue
        t=o.read_typetree()
        if "validItems" in t and "OnSuccess" in t:
            needs=[]
            for ref in t["validItems"]:
                try:
                    it=resolve(o,ref);d=it.read_typetree()
                    needs.append({"item":d.get("itemName"),"prefab":deref_name(it,d.get("spawnPrefab")),"key":d.get("m_Name")})
                except Exception as ex: needs.append({"unresolved":str(ex)[:110]})
            calls=[c["m_TargetAssemblyTypeName"].split(",")[0]+"."+c["m_MethodName"] for c in t["OnSuccess"]["m_PersistentCalls"]["m_Calls"]]
            rows.append({"kind":"consumer","object":deref_name(o,t.get("m_GameObject")),"needs":needs,"filters":t.get("validItemFilters"),"success":calls,"allClients":t.get("runEventOnAllClients"),"exact":t.get("checkExact")})
        elif "spawnPrefab" in t and "itemName" in t:
            components=[];clips=[];pf=t["spawnPrefab"]
            try:
                pr=resolve(o,pf);g=pr.read_typetree()
                for c in g["m_Component"]:
                    co=resolve(pr,c["component"])
                    if co.type.name=="MonoBehaviour":
                        name=co.parse_monobehaviour_head().m_Script.read().m_ClassName;components.append(name)
                        if name=="LevelCassetteItem":
                            ct=co.read_typetree()
                            clips.append(dict(loader=ct["LoaderName"],target=ct["LevelSceneName"],unlock=ct["unlockLevel"],consume=ct["DestroyAfterUse"],video=deref_name(co,ct["Video"])))
            except Exception:pass
            rows.append(dict(kind="item",name=t["itemName"],key=t.get("m_Name"),prefab=deref_name(o,pf),scrap=t.get("isScrap"),components=components,clips=clips))
        elif "IntegratedLevels" in t:
            rows.append(dict(kind="integrations",levels=t["IntegratedLevels"]))
        elif "TapeFinishedEvents" in t and "LoaderName" in t:
            rows.append(dict(kind="loader",loader=t["LoaderName"],object=deref_name(o,t.get("m_GameObject")),events=[x.get("clipName") for x in t["TapeFinishedEvents"]]))
        elif "SourcePool" in t and "CustomList" in t:
            rows.append(dict(kind="spawner",pool=t["SourcePool"],object=deref_name(o,t.get("m_GameObject")),items=[(e["ItemName"],e["Weight"]) for e in t["CustomList"]]))
        elif "sceneName" in t and "PlanetName" in t:
            rows.append(dict(kind="level",scene=t["sceneName"],planet=t["PlanetName"]))
        elif "DungeonName" in t and "DungeonFlow" in t:
            rows.append(dict(kind="flow",name=t["DungeonName"],flow=deref_name(o,t["DungeonFlow"])))
    return rows
args.output.mkdir(parents=True,exist_ok=True)
for p in sorted(args.plugins.glob("Magic_Wesley-*/**/*.lethalbundle")):
    dst=args.output/(p.parent.name+"--"+p.name+".json")
    sha=file_sha(p)
    if dst.exists():
        old=json.loads(dst.read_text(encoding="utf-8"))
        if old.get("sha256")==sha and old.get("schema")==2:continue
    try:rows=collect(p)
    except Exception as exc:print("ERROR",p.name,repr(exc),flush=True);continue
    dst.write_text(json.dumps(dict(schema=2,sha256=sha,bundle=p.name,rows=rows),ensure_ascii=False),encoding="utf-8")
    print(p.name,len(rows),flush=True);gc.collect()
