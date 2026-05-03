export interface ApiClient {
  echo(input: { message: string }): Promise<unknown>;
  createSubscription(input: {
    jobId: string;
    buyerAgent: string;
    offeringName: string;
    requirement: Record<string, unknown>;
  }): Promise<{
    subscriptionId: string;
    webhookSecret: string;
    ticksPurchased: number;
    intervalSeconds: number;
    expiresAt: string;
  }>;
}

export function createApiClient(baseUrl: string, opts: { apiKey?: string } = {}): ApiClient {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (opts.apiKey) headers["X-API-Key"] = opts.apiKey;

  async function post<T>(path: string, body: unknown): Promise<T> {
    const r = await fetch(`${baseUrl}${path}`, { method: "POST", headers, body: JSON.stringify(body) });
    if (!r.ok) throw new Error(`POST ${path} -> ${r.status}: ${await r.text()}`);
    return (await r.json()) as T;
  }

  return {
    echo(input)              { return post("/echo", input); },
    createSubscription(input) { return post("/subscriptions", input); }
  };
}
