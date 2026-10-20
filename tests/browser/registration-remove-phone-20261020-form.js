async page => {
    const input = __REGISTRATION_PRIVATE_INPUT__;
    const context = await page.context().browser().newContext({viewport:{width:1440,height:1100}});
    const p = await context.newPage();
    const checks = [];
    const check = (name, pass) => { checks.push({name,pass}); if (!pass) throw Error(name); };
    const out = '.ai/evidence/registration-remove-phone-20261020/';
    try {
        await p.goto('http://ahramstore.localhost:5174/register');
        await p.locator('#register-name').waitFor();
        const controls = await p.locator('form input').evaluateAll(nodes => nodes.map(n => ({id:n.id,name:n.name,type:n.type,autocomplete:n.autocomplete})));
        check('Exactly four registration inputs, including no hidden phone', controls.length === 4 && controls.every(n => !/phone|tel/i.test(JSON.stringify(n))));
        check('No Phone label, placeholder or validation message', !/phone|country code|dialing prefix/i.test(await p.locator('main').innerText()) && await p.locator('[id*="phone"], input[type="tel"], input[name="phone"], input[autocomplete="tel"]').count() === 0);
        const layouts = [];
        for (const width of [1440,390,320]) {
            await p.setViewportSize({width,height:1100});
            const layout = await p.locator('form').evaluate(form => {
                const groups = [...form.children].filter(n => n.tagName === 'DIV');
                const boxes = groups.map(n => { const r=n.getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height}; });
                return {groups:groups.length,boxes,gaps:boxes.slice(1).map((r,i)=>r.y-boxes[i].y-boxes[i].height),viewport:innerWidth,scrollWidth:document.documentElement.scrollWidth};
            });
            check(width + ': four aligned groups, regular spacing, no overflow', layout.groups === 4 && layout.scrollWidth <= width && layout.gaps.every(g => Math.abs(g-20)<1) && layout.boxes.every(b => b.x >= 0 && b.x+b.width <= width));
            layouts.push({width,...layout});
            await p.screenshot({path:out+'register-'+width+'.png',fullPage:true});
        }
        await p.setViewportSize({width:1440,height:1100});
        let posts = 0;
        p.on('request', r => { if(r.method()==='POST' && r.url().includes('/api/storefront/auth/register')) posts++; });
        const submit = () => p.locator('form button[type="submit"]').click();
        const errorText = async id => (await p.locator('#register-'+id+'-error').innerText()).trim();
        await submit();
        const required = {};
        for (const id of ['name','email','password','confirm-password']) required[id] = await errorText(id);
        check('All required field messages remain', Object.values(required).every(Boolean) && posts===0);
        await p.locator('#register-name').fill('QA Remove Phone');
        await p.locator('#register-email').fill('not-an-email');
        await p.locator('#register-password').fill('short');
        await p.locator('#register-confirm-password').fill('different');
        await submit();
        const invalid = {email:await errorText('email'),password:await errorText('password'),confirmation:await errorText('confirm-password')};
        check('Invalid email, short password and confirmation mismatch', /valid email/i.test(invalid.email) && /8 characters/i.test(invalid.password) && /do not match/i.test(invalid.confirmation));
        const weak = [];
        for (const value of ['alllowercase1','ALLUPPERCASE1','NoDigitsHere']) {
            await p.locator('#register-password').fill(value);
            await p.locator('#register-confirm-password').fill(value);
            await submit();
            weak.push(await errorText('password'));
        }
        check('Uppercase, lowercase and digit password rules unchanged', weak.every(t => /uppercase, lowercase, and a number/i.test(t)) && posts===0);
        await p.screenshot({path:out+'validation.png',fullPage:true});
        const email = 'regphone20261020-remove-main-'+input.runId+'@example.test';
        await p.locator('#register-email').fill(email);
        await p.locator('#register-password').fill(input.password);
        await p.locator('#register-confirm-password').fill(input.password);
        const responsePromise = p.waitForResponse(r => r.url().includes('/api/storefront/auth/register') && r.request().method()==='POST');
        await p.locator('form').evaluate(form => {form.requestSubmit();form.requestSubmit();});
        const response = await responsePromise;
        const body = await response.json();
        const request = response.request().postDataJSON();
        check('Actual POST contains only displayName, email and password', Object.keys(request).sort().join(',')==='displayName,email,password' && request.email===email);
        check('Real registration succeeds once with unverified identity and email dispatch', response.status()===200 && body.customer?.email===email && body.customer?.emailVerified===false && body.verificationEmailSent===true && posts===1);
        await p.getByRole('heading',{name:'Account created!'}).waitFor();
        check('Registration does not grant an authenticated session', (await context.request.get('http://localhost:5174/api/storefront/auth/me?lang=en',{headers:{host:'ahramstore.localhost:5174'}})).status()===401);
        await p.screenshot({path:out+'account-created.png',fullPage:true});
        const result = {observedUtc:new Date().toISOString(),checks,controls,layouts,validation:{required,invalid,weak},registration:{url:response.url(),request:{displayName:request.displayName,email:request.email,password:'[REDACTED]'},requestKeys:Object.keys(request),status:response.status(),body,postCount:posts,correlation:response.headers()['x-correlation-id']}};
        await context.close();
        return result;
    } catch (error) {
        await p.screenshot({path:out+'form-failed.png',fullPage:true});
        return {failed:true,checks,error:error.message};
    }
}
