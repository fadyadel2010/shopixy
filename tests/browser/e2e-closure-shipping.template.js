async page => {
  const input = __PHASE5_PRIVATE_INPUT__;
  const p = page.context().browser().contexts().at(-1).pages().at(-1);
  await p.locator('#checkout-fullName').fill(input.recipient);
  await p.locator('#checkout-phone').fill(input.phone);
  await p.locator('#checkout-email').fill(input.email);
  await p.locator('#checkout-city').fill('Cairo');
  await p.locator('#checkout-area').fill('Nasr City');
  await p.locator('#checkout-addressLine').fill(input.address);
  await p.locator('#checkout-notes').fill('PHASE5-20261015 controlled verification. No physical dispatch, pickup or collection.');
  await p.locator('#checkout-notes').blur();
  await p.getByRole('button',{name:'Continue to Payment',exact:true}).click();
  return {observedUtc:new Date().toISOString(),path:await p.evaluate(()=>location.pathname),
    headings:await p.locator('h1,h2,h3').allTextContents(),buttons:await p.getByRole('button').allTextContents(),
    phoneNormalized:await p.locator('#checkout-phone').evaluate(e=>e.value.startsWith('+20')).catch(()=>null),
    otpInputs:await p.locator('input[autocomplete="one-time-code"]').count()};
}
