import { QuoteTable } from "../../components/QuoteTable";
import { useQuotes } from "../../hooks/useQuotes";

export default function QuotesPage() {
  const quotes = useQuotes(1);
  return (
    <main>
      <h1>Quotes</h1>
      <QuoteTable quotes={quotes} />
    </main>
  );
}
