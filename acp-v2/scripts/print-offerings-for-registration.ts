import { OFFERINGS } from "../src/offerings/registry.js";

for (const [name, off] of Object.entries(OFFERINGS)) {
  console.log("=".repeat(60));
  console.log(`name:        ${name}`);
  console.log(`description: ${off.description}`);
  if (off.subscription) {
    console.log(`type:        SUBSCRIPTION`);
    console.log(`pricePerTick: ${off.subscription.pricePerTickUsdc} USDC`);
    console.log(`minInterval:  ${off.subscription.minIntervalSeconds}s`);
    console.log(`maxTicks:     ${off.subscription.maxTicks}`);
    console.log(`maxDuration:  ${off.subscription.maxDurationDays}d`);
    console.log(`requirementSchema:`);
    console.log(JSON.stringify(off.requirementSchema, null, 2));
    console.log(`pricing note: total = pricePerTick × ticks (computed at requirement time)`);
  } else {
    console.log(`type:        ONE-SHOT`);
    console.log(`requirementSchema:`);
    console.log(JSON.stringify(off.requirementSchema, null, 2));
  }
}
console.log("=".repeat(60));
