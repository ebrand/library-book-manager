import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { App } from "../src/App";
import { error, fakeApi } from "./fakeApi";
import { signedInAs, users } from "./session";

// Capability LEND as seen through the web UI. The rules are enforced and tested in the api
// component; these tests check what the UI sends and shows.

function renderAt(path: string, role: keyof typeof users) {
  signedInAs(role);
  window.history.pushState({}, "", path);
  return render(<App />);
}

afterEach(() => {
  window.sessionStorage.clear();
  window.history.pushState({}, "", "/");
});

const reader = { userId: users.member.userId, email: users.member.email };

const loan = (overrides: Record<string, unknown> = {}) => ({
  barcode: "A1",
  isbn: "9780261102217",
  title: "The Hobbit",
  member: reader,
  checkedOutOn: "2026-03-01",
  dueOn: "2026-03-22",
  returnedOn: null,
  returnedLate: false,
  overdue: false,
  ...overrides,
});

async function lend(email: string, barcode: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Borrower email"), email);
  await user.type(screen.getByLabelText("Barcode to lend"), barcode);
  await user.click(screen.getByRole("button", { name: "Check out" }));
  return user;
}

async function giveBack(barcode: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Barcode to return"), barcode);
  await user.click(screen.getByRole("button", { name: "Return" }));
}

const memberFound = (api: ReturnType<typeof fakeApi>) => api.on("GET", "/v1/members", { status: 200, body: [reader] });

describe("REQ-LEND-001..003 check out", () => {
  it("AC-LEND-001-1: a librarian lends a copy to a member found by email and sees the due date", async () => {
    const api = memberFound(fakeApi()).on("POST", "/v1/loans", { status: 201, body: loan() });
    renderAt("/lending", "librarian");

    await lend("Reader@Example.test", "A1");

    expect(api.sent("GET", "/v1/members")[0]?.query.get("email")).toBe("Reader@Example.test");
    expect(api.sent("POST", "/v1/loans")[0]?.body).toEqual({ barcode: "A1", memberId: reader.userId });
    expect(await screen.findByRole("status")).toHaveTextContent("Lent A1 (The Hobbit) to reader@example.test, due 22 March 2026.");
  });

  it("AC-LEND-001-2: a copy already on loan is reported as such", async () => {
    memberFound(fakeApi()).on("POST", "/v1/loans", { status: 409, body: error("COPY_ON_LOAN") });
    renderAt("/lending", "librarian");

    await lend(reader.email, "A1");

    expect(await screen.findByRole("alert")).toHaveTextContent("That copy is already on loan.");
  });

  it("AC-LEND-001-3: a copy removed from the catalogue is reported as not found", async () => {
    memberFound(fakeApi()).on("POST", "/v1/loans", { status: 404, body: error("COPY_NOT_FOUND") });
    renderAt("/lending", "librarian");

    await lend(reader.email, "GONE");

    expect(await screen.findByRole("alert")).toHaveTextContent("No copy in the catalogue has this barcode.");
  });

  it("AC-LEND-002-2: a member at the limit of ten is reported", async () => {
    memberFound(fakeApi()).on("POST", "/v1/loans", { status: 409, body: error("LOAN_LIMIT_REACHED") });
    renderAt("/lending", "librarian");

    await lend(reader.email, "A11");

    expect(await screen.findByRole("alert")).toHaveTextContent("The member already has 10 books on loan.");
  });

  it("AC-LEND-003-1: a member with an overdue loan is reported", async () => {
    memberFound(fakeApi()).on("POST", "/v1/loans", { status: 409, body: error("MEMBER_HAS_OVERDUE_LOAN") });
    renderAt("/lending", "librarian");

    await lend(reader.email, "A2");

    expect(await screen.findByRole("alert")).toHaveTextContent("The member has an overdue loan and cannot borrow until it is returned.");
  });

  it("REQ-LEND-001 (Q-LEND-04): an email with no member is reported and nothing is lent", async () => {
    const api = fakeApi().on("GET", "/v1/members", { status: 200, body: [] });
    renderAt("/lending", "librarian");

    await lend("nobody@example.test", "A1");

    expect(await screen.findByRole("alert")).toHaveTextContent("No member has this email.");
    expect(api.sent("POST", "/v1/loans")).toHaveLength(0);
  });
});

describe("REQ-LEND-004 return", () => {
  it("AC-LEND-004-1: returning a copy shows the return date", async () => {
    const api = fakeApi().on("POST", "/v1/copies/A1/return", {
      status: 200,
      body: loan({ returnedOn: "2026-03-10" }),
    });
    renderAt("/lending", "librarian");

    await giveBack("A1");

    expect(api.sent("POST", "/v1/copies/A1/return")).toHaveLength(1);
    expect(await screen.findByRole("status")).toHaveTextContent("A1 (The Hobbit) returned on 10 March 2026.");
  });

  it("AC-LEND-004-2: returning a copy that is on the shelf is reported", async () => {
    fakeApi().on("POST", "/v1/copies/A1/return", { status: 409, body: error("COPY_NOT_ON_LOAN") });
    renderAt("/lending", "librarian");

    await giveBack("A1");

    expect(await screen.findByRole("alert")).toHaveTextContent("That copy is not on loan.");
  });

  it("AC-LEND-004-3: a late return says it was late", async () => {
    fakeApi().on("POST", "/v1/copies/A1/return", {
      status: 200,
      body: loan({ returnedOn: "2026-03-25", returnedLate: true }),
    });
    renderAt("/lending", "librarian");

    await giveBack("A1");

    expect(await screen.findByRole("status")).toHaveTextContent("A1 (The Hobbit) returned on 25 March 2026. It was returned late.");
    // Nothing about money: fines are a declared gap.
    expect(document.body.textContent ?? "").not.toMatch(/fine|fee|charge|pay/i);
  });
});

describe("REQ-LEND-005, -006 current loans", () => {
  it("AC-LEND-006-1: a member sees their own loans with due dates, asked for by their own id", async () => {
    const api = fakeApi().on("GET", `/v1/members/${reader.userId}/loans`, {
      status: 200,
      body: [loan(), loan({ barcode: "B7", title: "Earthsea", dueOn: "2026-03-26" })],
    });
    renderAt("/loans/mine", "member");

    expect(await screen.findByRole("row", { name: /The Hobbit/ })).toHaveTextContent("22 March 2026");
    expect(screen.getByRole("row", { name: /Earthsea/ })).toHaveTextContent("26 March 2026");
    expect(api.requests.map((r) => r.path)).toEqual([`/v1/members/${reader.userId}/loans`]);
  });

  it("AC-LEND-005-1: an overdue loan is shown as overdue", async () => {
    fakeApi().on("GET", `/v1/members/${reader.userId}/loans`, {
      status: 200,
      body: [loan({ overdue: true }), loan({ barcode: "B7", title: "Earthsea" })],
    });
    renderAt("/loans/mine", "member");

    expect(await screen.findByRole("row", { name: /The Hobbit/ })).toHaveTextContent("Overdue");
    expect(screen.getByRole("row", { name: /Earthsea/ })).not.toHaveTextContent("Overdue");
  });

  it("AC-LEND-006-1: a member with no loans is told so", async () => {
    fakeApi().on("GET", `/v1/members/${reader.userId}/loans`, { status: 200, body: [] });
    renderAt("/loans/mine", "member");

    expect(await screen.findByText("You have no books on loan.")).toBeInTheDocument();
  });

  it("AC-LEND-006-2: a member is offered no way to look at another member's loans", async () => {
    const api = fakeApi();
    renderAt("/lending", "member");

    expect(screen.getByText("Your role does not permit this.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Member email")).toBeNull();
    expect(screen.queryByRole("link", { name: "Lending" })).toBeNull();
    expect(api.requests).toHaveLength(0);
  });

  it("AC-LEND-006-3: a librarian looks up a member's loans by email", async () => {
    const api = memberFound(fakeApi()).on("GET", `/v1/members/${reader.userId}/loans`, { status: 200, body: [loan()] });
    const user = userEvent.setup();
    renderAt("/lending", "librarian");

    await user.type(screen.getByLabelText("Member email"), reader.email);
    await user.click(screen.getByRole("button", { name: "Show loans" }));

    const table = await screen.findByRole("table", { name: `Loans of ${reader.email}` });
    expect(within(table).getByRole("row", { name: /The Hobbit/ })).toHaveTextContent("22 March 2026");
    expect(api.sent("GET", `/v1/members/${reader.userId}/loans`)).toHaveLength(1);
  });
});

describe("REQ-LEND-007 report", () => {
  it("AC-LEND-007-1: a librarian sees every book lent out and to whom", async () => {
    fakeApi().on("GET", "/v1/loans", {
      status: 200,
      body: [
        loan(),
        loan({ barcode: "B7", title: "Earthsea", member: { userId: "x", email: "other@example.test" }, overdue: true }),
      ],
    });
    renderAt("/loans", "librarian");

    expect(await screen.findByRole("row", { name: /The Hobbit/ })).toHaveTextContent("reader@example.test");
    const other = screen.getByRole("row", { name: /Earthsea/ });
    expect(other).toHaveTextContent("other@example.test");
    expect(other).toHaveTextContent("Overdue");
    expect(screen.getByText("2 books on loan")).toBeInTheDocument();
  });

  it("AC-LEND-007-2: members and administrators are not offered the report, and going there directly is not permitted", async () => {
    for (const role of ["member", "administrator"] as const) {
      const api = fakeApi();
      const { unmount } = renderAt("/loans", role);

      expect(screen.getByText("Your role does not permit this.")).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "On loan" })).toBeNull();
      expect(api.requests).toHaveLength(0);
      unmount();
      window.sessionStorage.clear();
    }
  });
});
