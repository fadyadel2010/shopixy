async page => {
  const context = await page.context().browser().newContext();
  const p = await context.newPage();
  await p.goto('http://ahramstore.localhost:5174/register');
  await p.locator('#register-name').waitFor();
  const modules = await p.evaluate(async () => {
    const targets = [
      ['/src/auth/components/RegisterForm.tsx', ['phoneInvalid', 'submitting.current', 'mounted.current']],
      ['/src/auth/pages/RegisterPage.tsx', ['status === "unknown"', 'status !== "unauthenticated"']],
      ['/src/auth/store/authStore.ts', ['Registration was superseded']],
    ];
    const results = [];
    for (const [route, markers] of targets) {
      const response = await fetch(route);
      const source = await response.text();
      const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(source));
      results.push({route,status:response.status,etag:response.headers.get('etag'),markersPresent:markers.every(marker => source.includes(marker)),sha256:[...new Uint8Array(digest)].map(b => b.toString(16).padStart(2,'0')).join('')});
    }
    return results;
  });
  if (modules.some(m => m.status !== 200 || !m.markersPresent)) throw new Error('Running frontend does not contain expected source fixes');
  await context.close();
  return {observedUtc:new Date().toISOString(),host:'ahramstore.localhost:5174',modules};
}
