import { useEffect, useState } from "react";
import { Alert } from "@platform-standin/index";
import { lendingReport, type Loan } from "../api/lending";
import { describeError } from "../messages";
import { LoansTable } from "./LoansTable";

/** REQ-LEND-007: every copy currently lent out, and to whom. */
export function ReportPage() {
  const [loans, setLoans] = useState<Loan[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void lendingReport().then((result) => (result.ok ? setLoans(result.data) : setError(describeError(result.error))));
  }, []);

  return (
    <section>
      <h1>On loan</h1>
      {error && <Alert>{error}</Alert>}
      {loans && <p>{loans.length === 1 ? "1 book on loan" : `${loans.length} books on loan`}</p>}
      {loans && loans.length > 0 && <LoansTable caption="Books currently on loan" loans={loans} showMember={true} />}
    </section>
  );
}
