async page => {
    const apiRead = path => page.evaluate(async path => (await (await import('/src/services/api.js')).default.get(path)).data, path);
    const methods = await apiRead('/admin/payment-methods');
    return { observedUtc: new Date().toISOString(), methods, readOnly: true };
}
