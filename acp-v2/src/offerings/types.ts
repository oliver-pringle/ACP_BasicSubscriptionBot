import type { ValidationResult } from "../validators.js";
import type { ApiClient } from "../apiClient.js";

export interface OfferingContext {
  client: ApiClient;
}

export interface SubscriptionConfig {
  pricePerTickUsdc: number;
  minIntervalSeconds: number;
  maxTicks: number;
  maxDurationDays: number;
}

export interface Offering {
  name: string;
  description: string;
  requirementSchema: Record<string, unknown>;
  validate(req: Record<string, unknown>): ValidationResult;

  // Exactly one of the following two MUST be set.
  execute?(req: Record<string, unknown>, ctx: OfferingContext): Promise<unknown>;
  subscription?: SubscriptionConfig;
}
