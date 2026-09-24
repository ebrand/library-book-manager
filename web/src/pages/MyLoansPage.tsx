import { useEffect, useState } from "react";
import { Alert } from "@platform-standin/index";
import { memberLoans, type Loan } from "../api/lending";
import type { SessionUser } from "../auth/session";
import { describeError } from "../messages";
import { LoansTable } from "./LoansTable";

/** REQ-LEND-006: a member's own current loans, with due dates. */
export function MyLoansPage(props: { self: SessionUser }) {
  const [loans, setLoans] = useState<Loan[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void memberLoans(props.self.userId).then((result) =>
      result.ok ? setLoans(result.data) : setError(describeError(result.error)),
    );
  }, [props.self.userId]);

  return (
    <section>
      <h1>My loans</h1>
      {error && <Alert>{error}</Alert>}
      {loans && loans.length === 0 && <p>You have no books on loan.</p>}
      {loans && loans.length > 0 && <LoansTable caption="Books you have on loan" loans={loans} showMember={false} />}
    </section>
  );
}
