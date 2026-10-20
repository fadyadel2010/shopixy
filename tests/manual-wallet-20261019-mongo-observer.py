"""Read only the manifested QA projections and absent diagnostic identity; never print connection secrets."""
import json,re,os
from pathlib import Path
from datetime import datetime,timezone
from pymongo import MongoClient
root=Path(__file__).resolve().parents[1]
settings={}
for name in ['appsettings.json','appsettings.Development.json','appsettings.Development.local.json']:
    p=root/'PointOfSale.API'/name
    if p.exists():
        section=re.search(r'"MongoDB"\s*:\s*\{([^}]+)\}',p.read_text(encoding='utf-8-sig'))
        if section:
            settings.update(dict(re.findall(r'"(ConnectionString|DatabaseName)"\s*:\s*"([^"\r\n]+)"',section[1])))
with MongoClient(os.environ.get('MongoDB__ConnectionString',settings['ConnectionString']),serverSelectionTimeoutMS=10000) as client:
    db=client[os.environ.get('MongoDB__DatabaseName',settings['DatabaseName'])]
    manifest=json.loads((root/'.ai/evidence/manual-wallet-20261019/sql-closeout.json').read_text())
    ids=[o['identity']['Id'] for o in manifest['orders']]
    docs=list(db.orders.find({'TenantId':'c9c5c1ae-949e-498e-bc63-07242daeb49d','OrderId':{'$in':ids}}, {'_id':0,'TenantId':1,'OrderId':1,'OrderNumber':1,'Status':1,'PaymentStatus':1,'Total':1,'Currency':1,'Timeline':1,'LastMutationVersion':1}))
    diagnostic=list(db.orders.find({'OrderNumber':'ORD-A758-2026-01040'},{'_id':0,'OrderId':1,'TenantId':1,'OrderNumber':1,'Total':1,'Currency':1,'Status':1,'PaymentStatus':1}))
    result={'observedUtc':datetime.now(timezone.utc).isoformat(),'readOnly':True,'qaProjections':docs,'diagnostic':diagnostic}
    out=root/'.ai/evidence/manual-wallet-20261019/mongo-closeout.json'
    out.write_text(json.dumps(result,default=str,indent=2),encoding='utf-8')
    print(json.dumps({'path':str(out.relative_to(root)),'qaCount':len(docs),'diagnosticCount':len(diagnostic)}))
