import { Table } from "@platform-standin/index";
import { formatDay, type Loan } from "../api/lending";

/** Current loans. `showMember` adds who holds each copy (librarian views). */
export function LoansTable(props: { caption: string; loans: readonly Loan[]; showMember: boolean }) {
  return (
    <Table
      caption={props.caption}
      columns={["Title", "Barcode", ...(props.showMember ? ["Member"] : []), "Due", "Status"]}
      rows={props.loans.map((l) => ({
        key: l.barcode,
        cells: [
          l.title,
          l.barcode,
          ...(props.showMember ? [l.member.email] : []),
          formatDay(l.dueOn),
          l.overdue ? "Overdue" : "On loan",
        ],
      }))}
    />
  );
}
