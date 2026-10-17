// Only the exact synthetic offer from the recorded harness readback/save race.
async page => page.evaluate(async () => {
    const api = (await import('/src/services/api.js')).default;
    const id = '64717089-c2b0-4f13-9126-e6d4794cf220';
    const current = (await api.get('/promotions/' + id)).data;
    if (!current.name.startsWith('P3QA')) throw new Error('Not the synthetic fixture');
    const result = await api.delete('/promotions/' + id, { headers: { 'If-Match': `"${current.rowVersion}"` } });
    const readback = await api.get('/promotions/' + id).then(r => r.status, e => e.response?.status);
    return { id, status: result.status, readback };
})
