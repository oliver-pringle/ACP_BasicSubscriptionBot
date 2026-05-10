import { OFFERINGS } from "../src/offerings/registry.js";
import { priceFor } from "../src/pricing.js";

for (const [name, off] of Object.entries(OFFERINGS)) {
  console.log("=".repeat(60));
  console.log(`name:        ${name}`);
  console.log(`description: ${off.description}`);
  if (off.subscription) {
    const basePrice = Math.min(...off.subscription.tiers.map(t => t.priceUsd));
    console.log(`type:        SUBSCRIPTION`);
    console.log(`Price:        ${basePrice.toFixed(2)} USDC  (base price — marketplace requires min $0.01; cheapest tier)`);
    console.log(`SLA:          ${off.slaMinutes} min  (hire → subscription receipt; per-tick is governed by interval)`);
    console.log(`Marketplace tiers (paste into "Add Job - Subscription Tiers" form):`);
    for (const tier of off.subscription.tiers) {
      console.log(`  Tier: ${tier.name} | $${tier.priceUsd} | ${tier.durationDays} days`);
    }
  } else {
    const price = priceFor(name, {});
    console.log(`type:        ONE-SHOT`);
    console.log(`Price:       ${price.amountUsdc} USDC`);
    console.log(`SLA:         ${off.slaMinutes} min  (estimated max time from hire to deliverable)`);
  }
  console.log(`requirementSchema:`);
  console.log(JSON.stringify(off.requirementSchema, null, 2));
  console.log(`requirementExample:`);
  console.log(JSON.stringify(off.requirementExample, null, 2));
  console.log(`deliverableSchema:`);
  console.log(JSON.stringify(off.deliverableSchema, null, 2));
  console.log(`deliverableExample:`);
  console.log(JSON.stringify(off.deliverableExample, null, 2));
}
console.log("=".repeat(60));
console.log("Marketplace form takes the requirement schema (with example) + tier list (subscription).");
console.log("Deliverable schema + example are for offering descriptions, buyer docs, and pre-launch wire-shape validation.");
