export type Quote = {
  id: number;
  customer: string;
  total: string;
  status: string;
};

export async function fetchQuotes(tenantId: number): Promise<Quote[]> {
  const response = await fetch(`/quotes?tenantId=${tenantId}`);
  return (await response.json()) as Quote[];
}

export async function fetchCustomers(): Promise<string[]> {
  const response = await fetch("/customers");
  return (await response.json()) as string[];
}
