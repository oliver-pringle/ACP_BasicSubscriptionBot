export interface ValidationResult {
  valid: boolean;
  reason?: string;
}

export function requireString(value: unknown, name: string): ValidationResult {
  if (typeof value !== "string" || value.trim() === "") {
    return { valid: false, reason: `${name} is required` };
  }
  return { valid: true };
}

export function requireStringLength(
  value: unknown,
  name: string,
  maxLen: number
): ValidationResult {
  const base = requireString(value, name);
  if (!base.valid) return base;
  if ((value as string).length > maxLen) {
    return { valid: false, reason: `${name} exceeds ${maxLen} character limit` };
  }
  return { valid: true };
}

export function requireOneOf(
  value: unknown,
  name: string,
  allowed: readonly string[]
): ValidationResult {
  if (value === undefined || value === null) return { valid: true };
  if (typeof value !== "string" || !allowed.includes(value)) {
    return { valid: false, reason: `${name} must be one of: ${allowed.join(", ")}` };
  }
  return { valid: true };
}

export function requireWebhookUrl(value: unknown, field: string): ValidationResult {
  if (typeof value !== "string") return { valid: false, reason: `${field} must be a string` };
  let url: URL;
  try { url = new URL(value); }
  catch { return { valid: false, reason: `${field} is not a valid URL` }; }

  const allowInsecure = process.env.ALLOW_INSECURE_WEBHOOKS === "true";
  if (url.protocol !== "https:" && !allowInsecure)
    return { valid: false, reason: `${field} must be HTTPS (set ALLOW_INSECURE_WEBHOOKS=true for dev)` };
  if (!allowInsecure) {
    const host = url.hostname.toLowerCase();
    if (host === "localhost" || host === "127.0.0.1" || host === "::1" ||
        host.startsWith("10.") || host.startsWith("192.168.") ||
        /^172\.(1[6-9]|2\d|3[01])\./.test(host))
      return { valid: false, reason: `${field} must not point to a private/loopback host` };
  }
  return { valid: true };
}

export function requireIntInRange(
  value: unknown,
  field: string,
  min: number,
  max: number
): ValidationResult {
  if (typeof value !== "number" || !Number.isInteger(value))
    return { valid: false, reason: `${field} must be an integer` };
  if (value < min) return { valid: false, reason: `${field} must be >= ${min}` };
  if (value > max) return { valid: false, reason: `${field} must be <= ${max}` };
  return { valid: true };
}
