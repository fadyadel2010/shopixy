// Hosted startup/router/StrictMode regression with a synthetic verification
// response. No real token, registration, email or business mutation is sent.
async page => {
  const context=await page.context().browser().newContext();
  const p=await context.newPage();
  let posts=0,tokenIntact=true,blockedWrites=0;
  await context.route('**/api/**',async route=>{
    const request=route.request();
    const url=request.url().split('?')[0];
    if(url.endsWith('/auth/verify-email')){
      posts++;
      tokenIntact &&= request.postDataJSON()?.token==='synthetic-auth-fixture';
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({success:true,message:'Synthetic verification accepted'})});
    }
    if(url.endsWith('/auth/me') || url.endsWith('/auth/refresh'))
      return route.fulfill({status:401,contentType:'application/json',body:JSON.stringify({code:'UNAUTHORIZED'})});
    if(request.method()==='GET')return route.continue();
    blockedWrites++;
    return route.fulfill({status:200,contentType:'application/json',body:'{}'});
  });
  try {
    await p.goto('http://ahramstore.localhost:5174/verify-email?token=synthetic-auth-fixture&tenant=c9c5c1ae-949e-498e-bc63-07242daeb49d');
    await p.getByRole('button',{name:'Go to My Account',exact:true}).waitFor();
    const heading=await p.locator('h1').textContent();
    await p.getByRole('button',{name:'Go to My Account',exact:true}).click();
    return {observedUtc:new Date().toISOString(),layer:'hosted-startup-with-controlled-verification',posts,tokenIntact,
      successActionVisible:true,heading,invalidLinkAbsent:await p.getByText('Invalid Link',{exact:true}).count()===0,
      realVerificationRequests:0,blockedWrites};
  }finally{await context.close();}
}
