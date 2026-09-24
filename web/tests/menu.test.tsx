import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { App } from "../src/App";
import { fakeApi } from "./fakeApi";
import { signedInAs, users } from "./session";

// The main menu (QUESTIONS.md Q-WEB-04). Which pages a role is offered follows REQ-AUTH-004
// and Q-WEB-03; the API enforces the roles regardless.

function renderAt(path: string, role: keyof typeof users) {
  signedInAs(role);
  window.history.pushState({}, "", path);
  return render(<App />);
}

afterEach(() => {
  window.sessionStorage.clear();
  window.history.pushState({}, "", "/");
});

const menu = () => screen.getByRole("navigation", { name: "Main" });
const items = () => within(within(menu()).getByRole("list")).getAllByRole("link").map((l) => l.textContent);

describe("main menu", () => {
  it.each([
    ["member", ["Search", "My loans"]],
    ["librarian", ["Search", "Add a title", "Lending", "On loan"]],
    ["administrator", ["Search", "Users"]],
  ] as const)("REQ-AUTH-004 (Q-WEB-03): the menu offers a %s exactly their pages, in order", (role, expected) => {
    fakeApi();
    renderAt("/", role);

    expect(items()).toEqual(expected);
  });

  it("Q-WEB-04: the menu marks the page being shown as the current page, and only that one", () => {
    fakeApi();
    renderAt("/lending", "librarian");

    const current = within(menu()).getAllByRole("link").filter((l) => l.getAttribute("aria-current") === "page");
    expect(current.map((l) => l.textContent)).toEqual(["Lending"]);
  });

  it("Q-WEB-04: choosing a menu item shows that page without reloading, and moves the current-page mark", async () => {
    fakeApi().on("GET", "/v1/loans", { status: 200, body: [] });
    const user = userEvent.setup();
    renderAt("/", "librarian");

    await user.click(within(menu()).getByRole("link", { name: "On loan" }));

    expect(await screen.findByRole("heading", { name: "On loan" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/loans");
    expect(within(menu()).getByRole("link", { name: "On loan" })).toHaveAttribute("aria-current", "page");
    expect(within(menu()).getByRole("link", { name: "Search" })).not.toHaveAttribute("aria-current");
  });

  it("Q-WEB-04: the menu shows who is signed in and offers signing out", () => {
    fakeApi();
    renderAt("/", "member");

    expect(within(menu()).getByText("Signed in as reader@example.test (member)")).toBeInTheDocument();
    expect(within(menu()).getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  it("Q-WEB-04: the name of the application leads back to search", async () => {
    fakeApi();
    const user = userEvent.setup();
    renderAt("/loans/mine", "member");
    fakeApi().on("GET", `/v1/members/${users.member.userId}/loans`, { status: 200, body: [] });

    await user.click(within(menu()).getByRole("link", { name: "Library Book Manager" }));

    expect(window.location.pathname).toBe("/");
    expect(screen.getByRole("heading", { name: "Search the catalogue" })).toBeInTheDocument();
  });

  it("Q-WEB-04: on small screens the Menu button opens and closes the list, and choosing an item closes it", async () => {
    fakeApi().on("GET", `/v1/members/${users.member.userId}/loans`, { status: 200, body: [] });
    const user = userEvent.setup();
    renderAt("/", "member");
    const toggle = within(menu()).getByRole("button", { name: "Menu" });
    const list = within(menu()).getByRole("list");

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveAttribute("aria-controls", list.id);

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(list).toHaveAttribute("data-open", "true");

    await user.click(within(list).getByRole("link", { name: "My loans" }));
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(list).toHaveAttribute("data-open", "false");
  });

  it("Q-WEB-04: Escape closes an open menu and returns focus to the Menu button", async () => {
    fakeApi();
    const user = userEvent.setup();
    renderAt("/", "member");
    const toggle = within(menu()).getByRole("button", { name: "Menu" });

    await user.click(toggle);
    const item = within(within(menu()).getByRole("list")).getByRole("link", { name: "My loans" });
    item.focus();
    expect(item).toHaveFocus();
    await user.keyboard("{Escape}");

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveFocus();
  });

  it("Q-WEB-04: signed out, there is no menu, only sign-in", () => {
    fakeApi();
    window.history.pushState({}, "", "/");
    render(<App />);

    expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });
});
