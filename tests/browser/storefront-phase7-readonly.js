async page => {
  const checks=[],failures=[],errors=[],writes=[]; const viewport=page.viewportSize();
  const check=(name,passed,extra={})=>{checks.push({name,passed:!!passed,...extra});if(!passed)throw new Error(name);};
  const onError=e=>errors.push(e.message.split('\n')[0]); page.on('pageerror',onError);
  const guard=route=>{const r=route.request(),path=r.url().split('?')[0];if(/\/api\/admin\/(store-config|commerce-ai|storefront\/auth-providers)/.test(path)&&!['GET','HEAD','OPTIONS'].includes(r.method())){writes.push(path);return route.abort();}return route.continue();};
  await page.route('**/api/**',guard);
  const anonymous=await page.context().browser().newContext();
  try {
    for(const path of ['/api/admin/commerce-ai','/api/admin/storefront/auth-providers']) {
      const r=await anonymous.request.get('http://localhost:5010'+path,{headers:{Host:'ahramstore.localhost:5010'}});
      check('Anonymous configuration access denied '+path,r.status()===401,{status:r.status()});
    }
    for(const width of [1440,390])for(const language of ['en','ar']) {
      await page.setViewportSize({width,height:900});
      const toggle=page.getByRole('button',{name:language==='ar'?'AR':'EN',exact:true});if(await toggle.count())await toggle.click();
      for(const path of ['/ecommerce/commerce-ai','/ecommerce/auth-providers']) {
        await page.goto('http://localhost:5173'+path); await page.locator('main h1').waitFor();
        await page.getByRole('button',{name:language==='ar'?'تحديث':'Refresh',exact:true}).first().waitFor().catch(()=>{});
        const layout=await page.evaluate(()=>({dir:document.documentElement.dir,width:innerWidth,scroll:document.documentElement.scrollWidth}));
        const text=await page.locator('main').innerText();
        check(`${language}/${width} real screen ${path}`,text.length>100&&!/phase7\.|authProviders\.|undefined/.test(text),{heading:await page.locator('main h1').innerText()});
        check(`${language}/${width} direction and responsive bounds ${path}`,layout.dir===(language==='ar'?'rtl':'ltr')&&layout.scroll<=width+1,layout);
        if(language==='ar'&&width===390)await page.screenshot({path:'.ai/evidence/storefront-phase7-20261018/'+path.split('/').pop()+'-ar-mobile.png'});
      }
    }
    const english=page.getByRole('button',{name:'EN',exact:true});if(await english.count())await english.click();
    for(const [path,endpoint] of [['/ecommerce/commerce-ai','/api/admin/store-config'],['/ecommerce/auth-providers','/api/admin/storefront/auth-providers']]) {
      let fail=true;const injected=route=>fail?route.fulfill({status:503,contentType:'application/json',body:'{"message":"P7QA controlled unavailable"}'}):route.continue();
      await page.route('**'+endpoint+'?*',injected);await page.route('**'+endpoint,injected);
      await page.goto('http://localhost:5173'+path);await page.getByRole('alert').first().waitFor();
      check('Actual screen distinguishes unavailable from defaults/empty '+path,true);
      fail=false;await page.getByRole('button',{name:'Retry',exact:true}).click();
      if(path.includes('commerce-ai'))await page.getByRole('switch',{name:'Enable Shop Assistant'}).waitFor();else await page.getByRole('heading',{name:'Google',exact:true}).waitFor();
      check('Normal retry recovers against actual API '+path,await page.getByRole('alert').count()===0);
      await page.unroute('**'+endpoint+'?*',injected);await page.unroute('**'+endpoint,injected);
    }
    const spoof=await page.evaluate(async()=>{const {default:api}=await import('/src/services/api.js');return(await api.get('/admin/storefront/auth-providers',{headers:{'X-Tenant-Id':'11111111-1111-4111-8111-111111111111'},validateStatus:()=>true})).status;});
    check('Authenticated foreign tenant spoof denied',spoof===403,{status:spoof});
    check('No configuration writes in readonly/failure tests',writes.length===0);
    check('No uncaught page errors',errors.length===0);
  }catch(e){failures.push(e.message.split('\n')[0]);}
  finally{await anonymous.close();await page.unroute('**/api/**',guard);page.off('pageerror',onError);if(viewport)await page.setViewportSize(viewport);const english=page.getByRole('button',{name:'EN',exact:true});if(await english.count())await english.click();}
  return {checks,failures,errors,writes,classification:'Hosted real routes/readback; two explicitly injected GET503 failures, no provider execution'};
}
