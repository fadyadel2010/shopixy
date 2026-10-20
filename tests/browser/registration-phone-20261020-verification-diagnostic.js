async page => {
  const results = [];
  for (const context of page.context().browser().contexts()) {
    for (const candidate of context.pages()) {
      if (candidate.url().startsWith('http://ahramstore.localhost:5174/verify-email?')) {
        const status = await candidate.evaluate(async () => {
          const response = await fetch('/api/storefront/auth/me?lang=en', {credentials:'include'});
          const body = await response.json();
          return {status:response.status,customerId:body.id,emailVerified:body.emailVerified,phone:body.phone ?? null};
        });
        results.push({route:'/verify-email',headings:await candidate.getByRole('heading').allTextContents(),status});
        await candidate.screenshot({path:'.ai/evidence/registration-phone-20261020/verification-before-fix-failed-ui.png'});
        await candidate.getByRole('button', {name:'Logout',exact:true}).first().click();
        await candidate.waitForURL('http://ahramstore.localhost:5174/');
        await context.close();
      }
    }
  }
  return {observedUtc:new Date().toISOString(),results};
}
