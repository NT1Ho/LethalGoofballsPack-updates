"""Build a narrow QuickSell config delta and a story checkpoint index from inventory.py output."""
from pathlib import Path
import json,sys
root=Path(__file__).resolve().parent
data=[]
for file in sorted((root/"private/inventory").glob("*.json")):
    doc=json.loads(file.read_text(encoding="utf-8"))
    if doc.get("schema")!=2:raise SystemExit("outdated inventory "+str(file))
    data.extend((doc["bundle"],r) for r in doc["rows"])
items=[(f,r) for f,r in data if r["kind"]=="item"]
from collections import defaultdict
names=defaultdict(set)
for f,r in items:names[r["name"].casefold()].add(r["prefab"])
# These are mission items, not ordinary supplies that just happen to be usable by a crafting station.
quest_names={n.casefold() for n in (
    "Bloody apparatus","Cosmic apparatus","Royal apparatus","Brooding seed","Crystal cube","Cavil's keycard",
    "Dungeon key","Manor safe key","Toy vault key","Coffin key","Fabricated key","Blue key",
    "Chunk of Overseer","Chunk of harold","Emma's body","Emma's head","Grind the deluded ticket",
    "Medicate the unwell ticket","Reshape the incapable ticket","Teach the disloyal ticket","Harrington's research 4",
    "Mycete case","Secret coordinates","Very secret coordinates","Stone of purification","Untampered operating panel",
    "Triangulation device","Huge harold","Beholder's eye")}
evidence=defaultdict(set);links=set();unresolved=set();spawns=set();locks=set()
for f,r in data:
    if r["kind"]=="consumer":
        needs=[i["item"] for i in r["needs"] if i.get("item")]
        needs+= [a["itemName"]["value"] for a in r["filters"] if a["itemName"]["shouldCheck"] and a["itemName"]["compareMethod"]==0]
        for n in needs:
            if n.casefold() not in quest_names:continue
            if not names[n.casefold()]:unresolved.add(n)
            for pf in names[n.casefold()]:
                if pf:evidence[pf].add(n+" | "+f+" | "+",".join(r["success"]))
    if r["kind"]=="item":
        for c in r["clips"]:
            if c["target"] and c["unlock"]:
                links.add((r["name"],r["prefab"],c["loader"],c["target"],str(bool(c["consume"]))))
                evidence[r["prefab"]].add(r["name"]+" | "+c["loader"]+" | "+c["target"])
    if r["kind"]=="integrations":
        for v in r["levels"]:locks.add((v.get("sceneName",""),str(v.get("forceLock",None)),f))
    if r["kind"]=="spawner":
        for n,weight in r["items"]:
            if n:spawns.add((n,f,r["object"],str(weight),str(r["pool"])))
def write_tsv(filename,cols,rows):
    (root/filename).write_text("\n".join(["\t".join(cols)]+["\t".join(map(str,row)) for row in sorted(rows)])+"\n",encoding="utf-8")
write_tsv("checkpoint-tapes.tsv",("item","prefab","loader","targetScene","consume"),links)
write_tsv("checkpoint-authored-spawns.tsv",("name","bundle","spawner","weight","pool"),[s for s in spawns if s[0].casefold() in {x[0].casefold() for x in links}|quest_names])
write_tsv("checkpoint-author-locks.tsv",("scene","forceLock","bundle"),locks)
write_tsv("quicksell-evidence.tsv",("prefab","identity evidence"),[(k,"; ".join(sorted(v))) for k,v in evidence.items() if k])
source=Path(sys.argv[1]);target=root/"config"/source.name
target.parent.mkdir(exist_ok=True);original=source.read_bytes();lines=original.splitlines(keepends=True);hits=0
for n,line in enumerate(lines):
    if line.startswith(b"Item Blacklist = "):
        end=line[len(line.rstrip(b"\r\n")):];line=line.rstrip(b"\r\n");base=line.decode("utf-8").split(" = ",1)[1].split(",")
        new=[p for p in sorted(evidence) if p and p.casefold() not in {x.casefold() for x in base}]
        lines[n]=line+b","+(",".join(new)).encode("utf-8")+end;hits+=1
if hits!=1:raise SystemExit("Ambiguous blacklist config; refusing to write")
target.write_bytes(b"".join(lines))
print(len(links),"route tapes;",len(new),"new exact prefab names; unresolvable:",sorted(unresolved))
