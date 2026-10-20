async page => {
    const results=[];
    for(const lang of ['en','ar']) {
        const context=await page.context().browser().newContext({viewport:{width:390,height:1000}});
        const p=await context.newPage();
        await p.goto('http://ahramstore.localhost:5174/register?lang='+lang);
        await p.locator('#register-name').waitFor();
        const controls=await p.locator('form input').evaluateAll(nodes=>nodes.map(n=>n.name));
        const text=await p.locator('main').innerText();
        const direction=await p.locator('main').evaluate(el=>getComputedStyle(el).direction);
        const noPhone=controls.join(',')==='displayName,email,password,confirmPassword' && !/phone|الهاتف|الجوال/i.test(text);
        const overflow=await p.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
        let postCount=0;
        p.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/api/storefront/auth/register'))postCount++;});
        await p.locator('form button[type="submit"]').click();
        await p.locator('#register-email-error').waitFor();
        const requiredMessage=await p.locator('#register-email-error').innerText();
        const errors=await p.locator('form p[id$="-error"]').count();
        await p.screenshot({path:'.ai/evidence/registration-remove-phone-20261020/primary-5174-'+lang+'.png',fullPage:true});
        const modules=[];
        // Vite HMR timestamps differ between the long-running and fresh servers.
        const normalizeHmr=text=>text.replace(/\?t=\d+/g,'');
        for(const path of ['/src/auth/components/RegisterForm.tsx','/src/auth/api/authApi.ts']) {
            const primary=await context.request.get('http://localhost:5174'+path);
            const isolated=await context.request.get('http://localhost:5175'+path);
            const primaryText=await primary.text();
            modules.push({path,status:primary.status(),etag:primary.headers().etag,identicalToTestedSource:normalizeHmr(primaryText)===normalizeHmr(await isolated.text()),noPhoneControl:!primaryText.includes('register-phone')});
        }
        const pass=noPhone&&!overflow&&direction===(lang==='ar'?'rtl':'ltr')&&errors===4&&postCount===0&&modules.every(m=>m.status===200&&m.identicalToTestedSource)&&(lang!=='ar'||/[\u0600-\u06ff]/.test(requiredMessage));
        results.push({lang,url:p.url(),controls,noPhone,overflow,direction,requiredMessage,requiredErrors:errors,registrationPosts:postCount,modules,pass});
        await context.close();
    }
    return {observedUtc:new Date().toISOString(),failed:results.some(r=>!r.pass),results};
}
