import type { Quote } from "../lib";

export function QuoteTable({ quotes }: { quotes: Quote[] }) {
  return (
    <table>
      <tbody>
        {quotes.map((quote) => (
          <tr key={quote.id}>
            <td dangerouslySetInnerHTML={{ __html: quote.customer }} />
            <td>{quote.total}</td>
            <td>{quote.status}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
