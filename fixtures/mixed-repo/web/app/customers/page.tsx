import { useEffect, useState } from "react";
import { fetchCustomers } from "../../lib";

export default function CustomersPage() {
  const [customers, setCustomers] = useState<string[]>([]);
  useEffect(() => {
    fetchCustomers().then(setCustomers);
  }, []);
  return (
    <main>
      <h1>Customers</h1>
      <ul>
        {customers.map((name) => (
          <li key={name}>{name}</li>
        ))}
      </ul>
    </main>
  );
}
