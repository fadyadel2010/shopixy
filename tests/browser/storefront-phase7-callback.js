async page => {
  const context = await page.context().browser().newContext();
  const local = await context.newPage(); const checks=[],failures=[];
  const check=(name,passed,details={})=>{checks.push({name,passed:!!passed,...details});if(!passed)throw new Error(name);};
  try {
    await local.goto('http://127.0.0.1:5097/', {timeout:60000});
    const result=await local.evaluate(async()=>{
      const results=[];
      async function request(path,body) {
        const csrf=document.cookie.split('; ').find(x=>x.startsWith('csrf_token='))?.split('=')[1];
        const r=await fetch(path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...(csrf?{'X-CSRF-Token':decodeURIComponent(csrf)}:{})},...(body===undefined?{}:{body:JSON.stringify(body)})});
        let data;try{data=await r.json();}catch{data=null;}return {status:r.status,data};
      }
      async function completion(mode) {
        const challenge=await request('/api/storefront/auth/social/challenge/google',{});
        if(challenge.status!==200)throw new Error('Local challenge unavailable: '+challenge.status);
        const token=await request('/fixture/token?nonce='+encodeURIComponent(challenge.data.nonce)+'&mode='+mode);
        const body={idToken:token.data.credential,state:challenge.data.state};
        const login=await request('/api/storefront/auth/google',body);
        return {login,body};
      }
      const providers=await request('/api/storefront/auth/providers');
      results.push({name:'Actual public provider contract admits configured synthetic Google',passed:providers.status===200&&providers.data.google===true&&providers.data.facebook===false});
      const missing=await request('/api/storefront/auth/google',{idToken:'synthetic',state:'0'.repeat(64)});
      results.push({name:'Missing correlation cookie rejected by actual callback',passed:missing.status===401,status:missing.status});
      for(const mode of ['nonce','audience','issuer','expired']) {
        const {login}=await completion(mode);
        const identity=await request('/api/storefront/fixture-identity');
        results.push({name:'Signed '+mode+' negative creates no authenticated partial browser session',passed:login.status===401&&identity.status===401,status:login.status});
      }
      const valid=await completion('valid');
      const identity=await request('/api/storefront/fixture-identity');
      const me=await request('/api/storefront/auth/me');
      results.push({name:'Valid locally signed fixture reaches actual callback and authenticated server identity',passed:valid.login.status===200&&identity.status===200&&identity.data.authenticated&&!identity.data.elevated&&me.status===200&&me.data.id===identity.data.customerId,status:valid.login.status,identityStatus:identity.status,meStatus:me.status});
      results.push({name:'Browser auth response contains no access or refresh token',passed:!valid.login.data.accessToken&&!valid.login.data.refreshToken});
      results.push({name:'Authentication cookies are not readable by browser JavaScript',passed:!document.cookie.includes('storefront_access_token=')&&!document.cookie.includes('storefront_refresh_token=')});
      const disabled=await request('/fixture/disable',{}); const after=await request('/api/storefront/auth/providers');const existing=await request('/api/storefront/fixture-identity');const next=await request('/api/storefront/auth/social/challenge/google',{});
      results.push({name:'Actual provider disable excludes new login, preserves existing session policy',passed:disabled.data.success&&after.data.google===false&&existing.status===200&&next.status===403});
      const logout=await request('/api/storefront/auth/logout',{});const loggedOut=await request('/api/storefront/fixture-identity');
      results.push({name:'Actual logout revokes SQL refresh session and clears browser authentication',passed:logout.status===200&&loggedOut.status===401});
      const replay=await request('/api/storefront/auth/google',valid.body);
      results.push({name:'Replayed completion rejected by actual callback',passed:replay.status===401});
      return {results,identity:identity.data};
    });
    for(const c of result.results)check(c.name,c.passed,{status:c.status,identityStatus:c.identityStatus,meStatus:c.meStatus});
    return {classification:'Browser -> real MVC controller/services -> configured SQL transaction; local signed authority, NOT external Google sign-in',checks,failures,identity:result.identity};
  } catch(e){failures.push(e.message.split('\n')[0]);return {checks,failures};}
  finally {
    try {await local.evaluate(async()=>{await fetch('/fixture/stop',{method:'POST'});});}catch{}
    await context.close();
  }
}
