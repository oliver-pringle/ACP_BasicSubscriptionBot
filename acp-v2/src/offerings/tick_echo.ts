import type { Offering } from "./types.js";
import { requireStringLength, requireWebhookUrl, requireIntInRange } from "../validators.js";

const MAX_MESSAGE_LENGTH = 1000;
const PRICE_PER_TICK_USDC = 0.01;
const MIN_INTERVAL_SECONDS = 60;
const MAX_TICKS = 1000;
const MAX_DURATION_DAYS = 90;

export const tickEcho: Offering = {
  name: "tick_echo",
  description:
    "Push a fixed message to your webhook every N seconds for K ticks. Subscription offering — pay upfront for the full tick budget. Demonstrates the BasicSubscriptionBot worker-loop + webhook + HMAC pattern end-to-end.",
  requirementSchema: {
    type: "object",
    properties: {
      message:         { type: "string",  maxLength: MAX_MESSAGE_LENGTH, description: "Message echoed verbatim on each tick." },
      webhookUrl:      { type: "string",  format: "uri",                  description: "HTTPS URL to receive each tick." },
      intervalSeconds: { type: "integer", minimum: MIN_INTERVAL_SECONDS,  description: "Seconds between ticks." },
      ticks:           { type: "integer", minimum: 1, maximum: MAX_TICKS, description: "Number of ticks (deliveries) to purchase." }
    },
    required: ["message", "webhookUrl", "intervalSeconds", "ticks"]
  },
  validate(req) {
    const m = requireStringLength(req.message, "message", MAX_MESSAGE_LENGTH);
    if (!m.valid) return m;
    const w = requireWebhookUrl(req.webhookUrl, "webhookUrl");
    if (!w.valid) return w;
    const i = requireIntInRange(req.intervalSeconds, "intervalSeconds", MIN_INTERVAL_SECONDS, MAX_DURATION_DAYS * 86400);
    if (!i.valid) return i;
    const t = requireIntInRange(req.ticks, "ticks", 1, MAX_TICKS);
    if (!t.valid) return t;
    const totalSec = (req.intervalSeconds as number) * (req.ticks as number);
    const cap = MAX_DURATION_DAYS * 86400;
    if (totalSec > cap) return { valid: false, reason: `intervalSeconds × ticks (${totalSec}s) exceeds ${MAX_DURATION_DAYS}d cap (${cap}s)` };
    return { valid: true };
  },
  subscription: {
    pricePerTickUsdc: PRICE_PER_TICK_USDC,
    minIntervalSeconds: MIN_INTERVAL_SECONDS,
    maxTicks: MAX_TICKS,
    maxDurationDays: MAX_DURATION_DAYS
  }
};
