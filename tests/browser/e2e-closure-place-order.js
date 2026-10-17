async page => {
  const p = page.context().browser().contexts().at(-1).pages().at(-1);
  const admitted = await p.evaluate(async () => {
    const {httpClient} = await import('/src/services/api/httpClient.ts');
    const response = await httpClient.get('/api/storefront/cart');
    const cart = response.data;
    if (cart.cartId !== '654f87c5-14f2-439d-8bbc-43ffb209e62c' || cart.items?.length !== 1 ||
        cart.items[0].variantId !== '1435b729-5fbe-48f1-a306-2e12051ad24b' || cart.items[0].quantity !== 1)
      throw new Error('Owned test cart no longer matches; no submission');
    if (window.phase5OrderSubmitted) throw new Error('Submission already attempted; no retry');
    window.phase5OrderSubmitted = true;
    return {cartId:cart.cartId,quantity:cart.items[0].quantity,unitPrice:cart.items[0].unitPrice};
  });
  const responsePromise = p.waitForResponse(r=>r.url().split('?')[0].endsWith('/api/storefront/checkout') && r.request().method()==='POST');
  await p.getByRole('button',{name:'Place Order',exact:true}).click();
  const response = await responsePromise;
  const d = await response.json();
  return {observedUtc:new Date().toISOString(),layer:'real-hosted-COD-checkout-UI',admitted,http:response.status(),
    code:d.code,orderId:d.orderId,orderNumber:d.orderNumber,total:d.total,currency:d.currency,
    paymentStatus:d.paymentStatus,orderStatus:d.orderStatus,paymentMethod:d.paymentMethod,
    requestId:d.traceId ?? response.headers()['x-correlation-id'] ?? null,submissionAttempts:1};
}
