import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { App } from "../src/App";
import { error, fakeApi } from "./fakeApi";
import { signedInAs, users } from "./session";

// Capability AUTH as seen through the web UI. The rules are enforced and tested in the api
// component; these tests check what the UI sends, stores and shows.

function renderAt(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}

afterEach(() => {
  window.sessionStorage.clear();
  window.history.pushState({}, "", "/");
});

async function signIn(email: string, password: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Email"), email);
  await user.type(screen.getByLabelText("Password"), password);
  await user.click(screen.getByRole("button", { name: "Sign in" }));
  return user;
}

describe("REQ-AUTH-001 sign in", () => {
  it("AC-AUTH-001-1: signing in sends email and password, and later requests carry the session", async () => {
    const api = fakeApi()
      .on("POST", "/v1/sessions", { status: 201, body: { token: "tok-123", user: users.member } })
      .on("GET", "/v1/titles", { status: 200, body: { items: [], total: 0, page: 1, pageSize: 50 } });
    renderAt("/");

    const user = await signIn("reader@example.test", "correct horse");

    expect(api.sent("POST", "/v1/sessions")[0]?.body).toEqual({ email: "reader@example.test", password: "correct horse" });
    expect(await screen.findByText("Signed in as reader@example.test (member)")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Search term"), "x");
    await user.click(screen.getByRole("button", { name: "Search" }));
    await screen.findByText("No titles match.");
    expect(api.sent("GET", "/v1/titles")[0]?.headers.get("Authorization")).toBe("Bearer tok-123");
  });

  it("AC-AUTH-001-2: a wrong password shows INVALID_CREDENTIALS and leaves the user signed out", async () => {
    fakeApi().on("POST", "/v1/sessions", { status: 401, body: error("INVALID_CREDENTIALS") });
    renderAt("/");

    await signIn("reader@example.test", "wrong");

    expect(await screen.findByRole("alert")).toHaveTextContent("The email address or password is not correct.");
    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
    expect(window.sessionStorage.length).toBe(0);
  });

  it("AC-AUTH-001-3: an email with no account shows exactly the same message as a wrong password", async () => {
    fakeApi().on("POST", "/v1/sessions", (r) => ({
      status: 401,
      body: { error: "INVALID_CREDENTIALS", message: (r.body as { email: string }).email, field: null },
    }));
    renderAt("/");

    await signIn("nobody@example.test", "whatever");
    const unknown = (await screen.findByRole("alert")).textContent;

    // The UI words the code itself and ignores anything else the server sends.
    expect(unknown).toBe("The email address or password is not correct.");
  });
});

describe("REQ-AUTH-002 no passwords kept", () => {
  it("REQ-AUTH-002: the browser keeps the session token and user, never the password", async () => {
    fakeApi().on("POST", "/v1/sessions", { status: 201, body: { token: "tok-123", user: users.member } });
    renderAt("/");

    await signIn("reader@example.test", "correct horse");
    await screen.findByText("Signed in as reader@example.test (member)");

    const stored = Object.keys(window.sessionStorage).map((k) => window.sessionStorage.getItem(k) ?? "").join(" ");
    expect(stored).toContain("tok-123");
    expect(stored).not.toContain("correct horse");
    expect(window.localStorage.length).toBe(0);
  });
});

describe("REQ-AUTH-003 lockout", () => {
  it("AC-AUTH-003-1: the failure that locks the account shows ACCOUNT_LOCKED", async () => {
    fakeApi().on("POST", "/v1/sessions", { status: 401, body: error("ACCOUNT_LOCKED") });
    renderAt("/");

    await signIn("reader@example.test", "wrong");

    expect(await screen.findByRole("alert")).toHaveTextContent("Too many failed sign-ins. Try again in 15 minutes.");
  });

  it("AC-AUTH-003-2: a locked account is shown as locked even when the password is right", async () => {
    fakeApi().on("POST", "/v1/sessions", { status: 401, body: error("ACCOUNT_LOCKED") });
    renderAt("/");

    await signIn("reader@example.test", "correct horse");

    expect(await screen.findByRole("alert")).toHaveTextContent("Too many failed sign-ins. Try again in 15 minutes.");
    expect(window.sessionStorage.length).toBe(0);
  });

  it("AC-AUTH-003-3: a successful sign-in after failures signs the user in and clears the error", async () => {
    let attempt = 0;
    fakeApi().on("POST", "/v1/sessions", () =>
      ++attempt === 1
        ? { status: 401, body: error("INVALID_CREDENTIALS") }
        : { status: 201, body: { token: "tok-9", user: users.member } },
    );
    renderAt("/");

    const user = await signIn("reader@example.test", "wrong");
    await screen.findByRole("alert");
    await user.clear(screen.getByLabelText("Password"));
    await user.type(screen.getByLabelText("Password"), "correct horse");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Signed in as reader@example.test (member)")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("REQ-AUTH-004 roles", () => {
  it("AC-AUTH-004-1: a member is not offered adding a title, and going there directly shows it is not permitted", async () => {
    signedInAs("member");
    fakeApi();
    renderAt("/titles/new");

    expect(screen.queryByRole("link", { name: "Add a title" })).toBeNull();
    expect(screen.getByText("Your role does not permit this.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add title" })).toBeNull();
  });

  it("AC-AUTH-004-2: a librarian is offered adding a title", async () => {
    signedInAs("librarian");
    fakeApi();
    renderAt("/titles/new");

    expect(screen.getByRole("link", { name: "Add a title" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add title" })).toBeInTheDocument();
  });

  it("AC-AUTH-004-3: a librarian is not offered changing roles, and going there directly shows it is not permitted", async () => {
    signedInAs("librarian");
    const api = fakeApi();
    renderAt("/users");

    expect(screen.queryByRole("link", { name: "Users" })).toBeNull();
    expect(screen.getByText("Your role does not permit this.")).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });

  it("AC-AUTH-004-4: an administrator changes another user's role", async () => {
    signedInAs("administrator");
    let member = users.member;
    const api = fakeApi()
      .on("GET", "/v1/users", () => ({ status: 200, body: [users.administrator, member] }))
      .on("PUT", `/v1/users/${users.member.userId}/role`, (r) => {
        member = { ...member, role: (r.body as { role: string }).role };
        return { status: 200, body: member };
      });
    const user = userEvent.setup();
    renderAt("/users");

    const row = await screen.findByRole("row", { name: /reader@example\.test/ });
    await user.selectOptions(within(row).getByLabelText("Role for reader@example.test"), "librarian");
    await user.click(within(row).getByRole("button", { name: "Change role for reader@example.test" }));

    expect(api.sent("PUT", `/v1/users/${users.member.userId}/role`)[0]?.body).toEqual({ role: "librarian" });
    expect(await screen.findByRole("status")).toHaveTextContent("reader@example.test is now a librarian.");
    // An administrator's own role is shown but not offered for change.
    const own = screen.getByRole("row", { name: /head@example\.test/ });
    expect(within(own).queryByRole("button")).toBeNull();
  });
});

describe("REQ-AUTH-005 sign out and expiry", () => {
  it("AC-AUTH-005-1: signing out ends the session on the server and forgets it in the browser", async () => {
    signedInAs("member");
    const api = fakeApi().on("DELETE", "/v1/sessions/current", { status: 204 });
    const user = userEvent.setup();
    renderAt("/");

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(api.sent("DELETE", "/v1/sessions/current")[0]?.headers.get("Authorization")).toBe("Bearer tok-member");
    expect(await screen.findByRole("button", { name: "Sign in" })).toBeInTheDocument();
    expect(window.sessionStorage.length).toBe(0);
  });

  it("AC-AUTH-005-1: a request refused with SESSION_INVALID returns the user to sign-in", async () => {
    signedInAs("member");
    fakeApi().on("GET", "/v1/titles", { status: 401, body: error("SESSION_INVALID") });
    const user = userEvent.setup();
    renderAt("/");

    await user.type(screen.getByLabelText("Search term"), "x");
    await user.click(screen.getByRole("button", { name: "Search" }));

    expect(await screen.findByRole("button", { name: "Sign in" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("You have been signed out. Sign in again.");
    expect(window.sessionStorage.length).toBe(0);
  });

  it("AC-AUTH-005-2: a request refused with SESSION_EXPIRED returns the user to sign-in, saying why", async () => {
    signedInAs("member");
    fakeApi().on("GET", "/v1/titles", { status: 401, body: error("SESSION_EXPIRED") });
    const user = userEvent.setup();
    renderAt("/");

    await user.type(screen.getByLabelText("Search term"), "x");
    await user.click(screen.getByRole("button", { name: "Search" }));

    expect(await screen.findByRole("button", { name: "Sign in" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Your session expired after 30 days without use. Sign in again.");
    expect(window.sessionStorage.length).toBe(0);
  });
});
