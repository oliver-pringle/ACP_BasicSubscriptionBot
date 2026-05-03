import type { Offering } from "./types.js";
import { requireStringLength } from "../validators.js";

const MAX_MESSAGE_LENGTH = 10_000;

export const echo: Offering = {
  name: "echo",
  description:
    "Echo a message back. One-shot offering. Demonstrates the BasicSubscriptionBot pattern handles vanilla one-shot calls alongside subscription offerings.",
  requirementSchema: {
    type: "object",
    properties: {
      message: { type: "string", description: "The message to echo back.", maxLength: MAX_MESSAGE_LENGTH }
    },
    required: ["message"]
  },
  validate(req) {
    return requireStringLength(req.message, "message", MAX_MESSAGE_LENGTH);
  },
  async execute(req, { client }) {
    return await client.echo({ message: String(req.message) });
  }
};
