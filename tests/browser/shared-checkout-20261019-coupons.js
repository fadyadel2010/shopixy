async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const coupons = await admin.evaluate(async () => {
        const api = (await import('/src/services/api.js')).default;
        const results = [];
        for (const suffix of ['COD','WALLET']) {
            const code = 'SHQA20261019' + suffix;
            const r = await api.post('/coupons', {code,description:'SHQA20261019 TEST ONLY synthetic 5 percent; not reported coupon 1441',discountType:0,discountValue:5,isActive:true,firstOrderOnly:false});
            results.push({status:r.status,body:r.data});
        }
        return results;
    });
    await page.evaluate(c => sessionStorage.setItem('shqa-coupons',JSON.stringify(c)),coupons);
    return {coupons};
}
