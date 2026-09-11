import { useEffect, useState } from "react";
import { fetchQuotes, type Quote } from "../lib";

export function useQuotes(tenantId: number): Quote[] {
  const [quotes, setQuotes] = useState<Quote[]>([]);
  useEffect(() => {
    fetchQuotes(tenantId).then(setQuotes);
  }, [tenantId]);
  return quotes;
}
