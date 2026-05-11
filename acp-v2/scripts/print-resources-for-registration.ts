import { RESOURCES } from "../src/resources.js";

function main() {
  const names = Object.keys(RESOURCES).sort();
  if (names.length === 0) {
    console.log("=".repeat(72));
    console.log("(no resources registered)");
    console.log("");
    console.log("Resources are public, free, parameterised endpoints that buyer / orchestrator");
    console.log("agents (Butler etc.) call BEFORE paying for an offering — e.g. capability checks,");
    console.log("liveness pings, schema catalogues, cost quotes. Register entries in");
    console.log("acp-v2/src/resources.ts and wire matching handlers in BasicSubscriptionBot.Api/Program.cs.");
    return;
  }
  for (const name of names) {
    const r = RESOURCES[name]!;
    console.log("=".repeat(72));
    console.log(`Resource name:        ${r.name}`);
    console.log(`Path on bot API:      ${r.url}`);
    console.log(`Description:`);
    console.log(`  ${r.description}`);
    console.log(`Params schema (JSON):`);
    console.log(JSON.stringify(r.params, null, 2));
    console.log("");
  }
  console.log("=".repeat(72));
  console.log(`Total: ${names.length} resource(s).`);
  console.log(`Paste each block into app.virtuals.io → BasicSubscriptionBot agent → Resources → New resource.`);
  console.log(`Resources are FREE — no price field. The marketplace form takes name + URL +`);
  console.log(`params schema + description. Make sure the URL is publicly reachable BEFORE you`);
  console.log(`register — Butler-style buyer agents will call it to introspect your bot.`);
}

main();
