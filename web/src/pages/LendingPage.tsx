import { useState } from "react";
import { Alert, Button, Form, Status, TextField } from "@platform-standin/index";
import { checkOut, findMember, formatDay, memberLoans, returnCopy, type Loan, type Member } from "../api/lending";
import { describeError } from "../messages";
import { LoansTable } from "./LoansTable";

type Notice = { kind: "status" | "alert"; text: string } | null;

/** REQ-LEND-001..004, -006: the librarian's desk. */
export function LendingPage() {
  const [borrower, setBorrower] = useState("");
  const [lendBarcode, setLendBarcode] = useState("");
  const [returnBarcode, setReturnBarcode] = useState("");
  const [lookup, setLookup] = useState("");
  const [shown, setShown] = useState<{ member: Member; loans: Loan[] } | null>(null);
  const [notice, setNotice] = useState<Notice>(null);

  async function member(email: string): Promise<Member | null> {
    const result = await findMember(email.trim());
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error) });
      return null;
    }
    const found = result.data[0];
    if (!found) setNotice({ kind: "alert", text: "No member has this email." });
    return found ?? null;
  }

  async function lend() {
    const found = await member(borrower);
    if (!found) return;
    const result = await checkOut(lendBarcode.trim(), found.userId);
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error) });
      return;
    }
    const l = result.data;
    setLendBarcode("");
    setNotice({ kind: "status", text: `Lent ${l.barcode} (${l.title}) to ${l.member.email}, due ${formatDay(l.dueOn)}.` });
  }

  async function giveBack() {
    const result = await returnCopy(returnBarcode.trim());
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error) });
      return;
    }
    const l = result.data;
    setReturnBarcode("");
    const late = l.returnedLate ? " It was returned late." : "";
    setNotice({ kind: "status", text: `${l.barcode} (${l.title}) returned on ${formatDay(l.returnedOn ?? "")}.${late}` });
  }

  async function show() {
    const found = await member(lookup);
    if (!found) return;
    const result = await memberLoans(found.userId);
    if (result.ok) {
      setShown({ member: found, loans: result.data });
      setNotice(null);
    } else {
      setNotice({ kind: "alert", text: describeError(result.error) });
    }
  }

  return (
    <section>
      <h1>Lending</h1>
      {notice?.kind === "alert" && <Alert>{notice.text}</Alert>}
      {notice?.kind === "status" && <Status>{notice.text}</Status>}

      <h2>Check out</h2>
      <Form label="Check out" onSubmit={() => void lend()}>
        <TextField label="Borrower email" type="email" value={borrower} onChange={setBorrower} />
        <TextField label="Barcode to lend" value={lendBarcode} onChange={setLendBarcode} />
        <Button type="submit" variant="primary">
          Check out
        </Button>
      </Form>

      <h2>Return</h2>
      <Form label="Return" onSubmit={() => void giveBack()}>
        <TextField label="Barcode to return" value={returnBarcode} onChange={setReturnBarcode} />
        <Button type="submit">Return</Button>
      </Form>

      <h2>A member's loans</h2>
      <Form label="A member's loans" onSubmit={() => void show()}>
        <TextField label="Member email" type="email" value={lookup} onChange={setLookup} />
        <Button type="submit">Show loans</Button>
      </Form>
      {shown && shown.loans.length === 0 && <p>{shown.member.email} has no books on loan.</p>}
      {shown && shown.loans.length > 0 && (
        <LoansTable caption={`Loans of ${shown.member.email}`} loans={shown.loans} showMember={false} />
      )}
    </section>
  );
}
