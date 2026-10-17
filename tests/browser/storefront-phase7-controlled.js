// Actual source components with ALL application HTTP intercepted. No hosted writes.
async page => {
  const context=await page.context().browser().newContext(); const tab=await context.newPage();
  const checks=[],failures=[],errors=[],requests=[];let mode='failure',held,socialSuccess=false;
  const check=(name,passed,details)=>{checks.push({name,passed:!!passed,...(details?{details}:{})});if(!passed)throw new Error(name);};
  tab.on('pageerror',e=>errors.push(e.message.split('\n')[0]));
  await context.route('**/__phase7__',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><html><body><div id="fixture"></div></body></html>'}));
  await context.route('**/api/**',async route=>{
    const path=route.request().url().replace(/^https?:\/\/[^/]+/,'').split('?')[0];if(!path.startsWith('/api/'))return route.continue();
    requests.push({path,method:route.request().method()});
    const json=(value,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(value)});
    if(path==='/api/storefront/chat') {
      if(mode==='held'){held=route;return;}
      return mode==='failure'?json({type:'message',message:'CONTROLLED failure',isFallback:true},503):json({type:'message',message:'CONTROLLED answer',isFallback:false});
    }
    if(path.endsWith('/providers'))return json({google:true,facebook:true,googleClientId:'p7qa-google',facebookAppId:'p7qa-facebook'});
    if(path.includes('/social/challenge/'))return json({state:'P7QA-browser-state',nonce:'P7QA-browser-nonce'});
    if(path.endsWith('/google')||path.endsWith('/facebook'))return socialSuccess?json({customer:{id:'77777777-7777-4777-8777-777777777777',email:'p7qa@example.invalid',emailVerified:true,displayName:'P7QA'},expiresAt:new Date(Date.now()+600000).toISOString()}):json({code:'INVALID_LOGIN_STATE',message:'Controlled rejected login'},401);
    if(path.endsWith('/cart/merge'))return json({items:[],ownerType:'customer'});
    return json({});
  });
  try {
    await tab.goto('http://localhost:5174/__phase7__');
    await tab.evaluate(async()=>{
      const refresh=await import('/@react-refresh');refresh.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>t=>t;
      const main=await(await fetch('/src/main.tsx')).text();const reactPath=main.match(/from "([^"]*\/react\.js[^"]*)"/)[1],clientPath=main.match(/from "([^"]*react-dom_client[^"]*)"/)[1];
      const rm=await import(reactPath),cm=await import(clientPath),React=rm.default||rm,createRoot=cm.createRoot||cm.default.createRoot;
      await import('/src/styles/index.css');const source=await(await fetch('/src/components/chat/ChatWidget.tsx')).text();
      const dep=part=>source.match(new RegExp('"([^" ]*'+part.replaceAll('.','\\.')+'[^" ]*)"'))[1];
      const {default:Chat}=await import('/src/components/chat/ChatWidget.tsx');const store=await import(dep('/src/store/index.ts'));
      const {useAuthStore}=await import(dep('/src/auth/store/authStore.ts'));const bootstrap=await import('/src/bootstrap/index.ts');
      const {DEFAULT_STORE_CONFIG}=await import('/src/types/storeConfig.ts');
      const {useLanguageStore}=await import('/src/i18n/languageStore.ts');
      const {SocialLoginButtons}=await import('/src/auth/components/SocialLoginButtons.tsx');
      bootstrap.setResolvedTenant('22222222-2222-4222-8222-222222222222',{overwrite:true});bootstrap.getRequestHeaders();
      store.useStoreConfigStore.getState().setRuntimeConfig({...DEFAULT_STORE_CONFIG,tenantId:'22222222-2222-4222-8222-222222222222',commerceAiEnabled:true});
      useLanguageStore.getState().setLanguage('en');
      const root=createRoot(document.getElementById('fixture'));window.p7={root,store,auth:useAuthStore,language:useLanguageStore};
      window.p7.mount=which=>root.render(React.createElement(which==='chat'?Chat:SocialLoginButtons,{onSuccess:()=>{window.p7.successes=(window.p7.successes||0)+1;},onError:()=>{window.p7.errors=(window.p7.errors||0)+1;}}));
      window.p7.mount('chat');
    });
    await tab.getByRole('button',{name:'Open chat',exact:true}).click();
    await tab.locator('.chat-input__field').fill('P7QA controlled failure');await tab.getByRole('button',{name:'Send',exact:true}).click();
    await tab.locator('.chat-widget__error').waitFor();check('Actual widget rejects provider failure and displays fallback',await tab.locator('.chat-message--fallback').count()===1);
    mode='held';await tab.locator('.chat-input__field').fill('P7QA cancellation');await tab.getByRole('button',{name:'Send',exact:true}).click();
    await tab.locator('.chat-widget__typing').waitFor();await tab.getByRole('button',{name:'Close',exact:true}).click();
    if(held)await held.fulfill({status:200,contentType:'application/json',body:'{"type":"message","message":"STALE_RESPONSE_MUST_NOT_APPEAR"}'}).catch(()=>{});
    await tab.getByRole('button',{name:'Open chat',exact:true}).click();check('Closing cancels current request and rejects its late response',await tab.getByText('STALE_RESPONSE_MUST_NOT_APPEAR').count()===0&&await tab.locator('.chat-widget__typing').count()===0);
    await tab.evaluate(()=>window.p7.auth.setState({customer:{id:'p7qa-next-account'}}));
    await tab.getByRole('button',{name:'Open chat',exact:true}).click();check('Account switch clears old transcript',await tab.getByText('P7QA controlled failure',{exact:true}).count()===0);
    await tab.evaluate(()=>window.p7.mount('social'));
    await tab.getByRole('button',{name:'Sign in with Google',exact:true}).waitFor();
    await tab.evaluate(()=>{
      window.p7.googlePrompts=0;window.google={accounts:{id:{initialize:config=>{window.p7.googleConfig=config;},prompt:listener=>{window.p7.googlePrompts++;window.p7.dismiss=listener;},cancel:()=>{}}}};
      window.FB={init:()=>{},login:callback=>{window.p7.facebookCallback=callback;}};
    });
    await tab.getByRole('button',{name:'Sign in with Google',exact:true}).click();
    await tab.waitForFunction(()=>window.p7.googlePrompts===1);
    check('Actual Google initiation passes server-issued nonce to SDK',await tab.evaluate(()=>window.p7.googleConfig.nonce==='P7QA-browser-nonce'));
    await tab.evaluate(()=>window.p7.dismiss({isNotDisplayed:()=>true}));
    check('Dismissed/unavailable Google prompt releases loading state',await tab.getByRole('button',{name:'Sign in with Google',exact:true}).isEnabled());
    await tab.getByRole('button',{name:'Sign in with Google',exact:true}).click();
    await tab.waitForFunction(()=>window.p7.googlePrompts===2);
    await tab.evaluate(async()=>{const callback=window.p7.googleConfig.callback;await Promise.all([callback({credential:'P7QA-synthetic'}),callback({credential:'P7QA-duplicate'})]);});
    check('Duplicate SDK callback submits exactly one completion',requests.filter(x=>x.path==='/api/storefront/auth/google').length===1);
    check('Rejected completion never invokes success navigation',await tab.evaluate(()=>!window.p7.successes&&window.p7.errors===1));
    await tab.getByRole('button',{name:'Sign in with Facebook',exact:true}).click();await tab.waitForFunction(()=>!!window.p7.facebookCallback);
    await tab.evaluate(()=>window.p7.mount('chat'));await tab.evaluate(()=>window.p7.facebookCallback({authResponse:{accessToken:'P7QA-late'}}));
    check('Leaving social-login UI rejects late SDK credential completion',requests.filter(x=>x.path==='/api/storefront/auth/facebook').length===0);
    await tab.evaluate(()=>{window.p7.auth.setState({customer:null,status:'unauthenticated'});window.p7.mount('social');});
    await tab.getByRole('button',{name:'Sign in with Google',exact:true}).waitFor();socialSuccess=true;
    await tab.getByRole('button',{name:'Sign in with Google',exact:true}).click();await tab.waitForFunction(()=>window.p7.googlePrompts===3);
    await tab.evaluate(()=>window.p7.googleConfig.callback({credential:'P7QA-controlled-positive'}));
    const completion=await tab.evaluate(()=>({status:window.p7.auth.getState().status,successes:window.p7.successes||0,errors:window.p7.errors||0}));
    check('Controlled accepted SDK completion authenticates and invokes success once',completion.status==='authenticated'&&completion.successes===1,completion);
    await tab.getByRole('button',{name:'Sign in with Google',exact:true}).click();await tab.waitForFunction(()=>window.p7.googlePrompts===4);
    await tab.evaluate(()=>window.p7.auth.setState({availableProviders:{google:true,facebook:true,googleClientId:'p7qa-reconfigured',facebookAppId:'p7qa-facebook'}}));
    check('Provider configuration change releases cancelled SDK loading state',await tab.getByRole('button',{name:'Sign in with Google',exact:true}).isEnabled());
    check('No uncaught component errors',errors.length===0);
  }catch(e){failures.push(e.message.split('\n')[0]);}
  finally{await context.close();}
  return {checks,failures,errors,requests,classification:'Controlled source components, intercepted internal HTTP and SDK fixtures; no real provider or application writes'};
}
