async page => {
  const p=page.context().browser().contexts().at(-1).pages().at(-1);
  const result=await p.evaluate(async()=>{
    const {httpClient}=await import('/src/services/api/httpClient.ts');
    const checks=[];
    for(const path of ['/api/storefront/products/by-id/03144bba-1880-c1d6-3062-997a1006feca',
      '/api/storefront/products/cert-media-variable-20261007','/api/storefront/shipping-methods',
      '/api/storefront/payment-methods','/api/storefront/pages/phase5-nonexistent-page']){
      try {const r=await httpClient.get(path);checks.push({path,http:r.status,cacheControl:r.headers['cache-control'],etagPresent:!!r.headers.etag,lastModifiedPresent:!!r.headers['last-modified'],correlationId:r.headers['x-correlation-id']});}
      catch(e){const r=e.response;checks.push({path,http:r?.status,code:r?.data?.code,cacheControl:r?.headers['cache-control'],etagPresent:!!r?.headers.etag,lastModifiedPresent:!!r?.headers['last-modified']});}
    }
    const history=await httpClient.get('/api/storefront/account/orders');
    const rows=history.data.orders??history.data.items??[];
    const own=rows.filter(o=>(o.id??o.orderId)==='ac89be7f-5e8c-4834-bcec-a1d14d5af2e6');
    const cart=await httpClient.get('/api/storefront/cart');
    const me=await httpClient.get('/api/storefront/auth/me');
    return {observedUtc:new Date().toISOString(),checks,customer:{http:me.status,isOwnedTest:me.data.id==='e829b8e0-7c97-41cb-8cfa-3c9644ac69ad',emailVerified:me.data.emailVerified},
      history:{http:history.status,ownedOrderCount:own.length,orders:own.map(o=>({id:o.id??o.orderId,orderNumber:o.orderNumber,status:o.status,paymentStatus:o.paymentStatus,total:o.total,currency:o.currency}))},cart:{http:cart.status,itemCount:cart.data.items?.length}};
  });
  const original=page.context().pages().find(t=>t.url().includes('ahramstore.localhost:5174'));
  result.originalCustomerSession=await original.evaluate(async()=>{
    const {httpClient}=await import('/src/services/api/httpClient.ts');
    try{const r=await httpClient.get('/api/storefront/auth/me');return {http:r.status,authenticated:true,notReplacedByTestIdentity:r.data.id!=='e829b8e0-7c97-41cb-8cfa-3c9644ac69ad'};}
    catch(e){return {http:e.response?.status,authenticated:false};}
  });
  const admin=page.context().pages().find(t=>t.url().includes('localhost:5173'));
  result.admin=await admin.evaluate(async()=>{
    const r=await fetch('/api/admin/orders/ac89be7f-5e8c-4834-bcec-a1d14d5af2e6',{credentials:'include'});const d=await r.json();
    return {http:r.status,id:d.id??d.orderId,orderNumber:d.orderNumber,status:d.status,paymentStatus:d.paymentStatus,tenantMatches:d.tenantId==='c9c5c1ae-949e-498e-bc63-07242daeb49d'};
  });
  return result;
}
