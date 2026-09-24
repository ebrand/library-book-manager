import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { App } from "../src/App";
import { error, fakeApi, title } from "./fakeApi";
import { signedInAs } from "./session";

// Capability BOOK as seen through the web UI. The rules themselves are enforced and
// tested in the api component; these tests check the UI sends what the contract expects
// and shows what the API answers.

// Catalogue changes are a librarian's; searching is anyone's, and a librarian is someone.
function renderAt(path: string) {
  signedInAs("librarian");
  window.history.pushState({}, "", path);
  return render(<App />);
}

afterEach(() => {
  window.sessionStorage.clear();
  window.history.pushState({}, "", "/");
});

const leGuinPage = (page: number, count: number) => ({
  items: Array.from({ length: count }, (_, i) => ({
    ...title({ isbn: `978000${String((page - 1) * 50 + i + 1).padStart(7, "0")}` }),
    title: `Earthsea volume ${(page - 1) * 50 + i + 1}`,
    author: "Le Guin",
    copies: undefined,
  })),
  total: 120,
  page,
  pageSize: 50,
});

describe("REQ-BOOK-001 add a title", () => {
  it("AC-BOOK-001-1: the add-title form sends ISBN, title, author and year, then shows the title with zero copies", async () => {
    const api = fakeApi()
      .on("POST", "/v1/titles", { status: 201, body: title() })
      .on("GET", "/v1/titles/9780261102217", { status: 200, body: title() });
    const user = userEvent.setup();
    renderAt("/titles/new");

    await user.type(screen.getByLabelText("ISBN"), "9780261102217");
    await user.type(screen.getByLabelText("Title"), "The Hobbit");
    await user.type(screen.getByLabelText("Author"), "J. R. R. Tolkien");
    await user.type(screen.getByLabelText("Publication year"), "1937");
    await user.click(screen.getByRole("button", { name: "Add title" }));

    expect(api.sent("POST", "/v1/titles")[0]?.body).toEqual({
      isbn: "9780261102217",
      title: "The Hobbit",
      author: "J. R. R. Tolkien",
      publicationYear: 1937,
    });
    expect(await screen.findByRole("heading", { name: "The Hobbit" })).toBeInTheDocument();
    expect(screen.getByText("0 copies")).toBeInTheDocument();
  });

  it("AC-BOOK-001-2: an ISBN already in the catalogue is reported and the form keeps what was typed", async () => {
    fakeApi().on("POST", "/v1/titles", { status: 409, body: error("ISBN_ALREADY_EXISTS") });
    const user = userEvent.setup();
    renderAt("/titles/new");

    await user.type(screen.getByLabelText("ISBN"), "9780261102217");
    await user.type(screen.getByLabelText("Title"), "The Hobbit");
    await user.type(screen.getByLabelText("Author"), "Tolkien");
    await user.type(screen.getByLabelText("Publication year"), "1937");
    await user.click(screen.getByRole("button", { name: "Add title" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A title with this ISBN is already in the catalogue.");
    expect(screen.getByLabelText("ISBN")).toHaveValue("9780261102217");
  });

  it("AC-BOOK-001-3: an empty publication year is sent as missing (never 0) and FIELD_REQUIRED is shown on that field", async () => {
    const api = fakeApi().on("POST", "/v1/titles", { status: 400, body: error("FIELD_REQUIRED", "publicationYear") });
    const user = userEvent.setup();
    renderAt("/titles/new");

    await user.type(screen.getByLabelText("ISBN"), "9780261102217");
    await user.type(screen.getByLabelText("Title"), "The Hobbit");
    await user.type(screen.getByLabelText("Author"), "Tolkien");
    await user.click(screen.getByRole("button", { name: "Add title" }));

    expect(api.sent("POST", "/v1/titles")[0]?.body).toEqual({
      isbn: "9780261102217",
      title: "The Hobbit",
      author: "Tolkien",
      publicationYear: null,
    });
    const year = screen.getByLabelText("Publication year");
    await waitFor(() => expect(year).toHaveAttribute("aria-invalid", "true"));
    expect(year).toHaveAccessibleDescription("Publication year is required.");
  });
});

describe("REQ-BOOK-002 add a copy", () => {
  it("AC-BOOK-002-1: adding a copy without a barcode shows the assigned barcode and the new count", async () => {
    let copies = [
      { barcode: "A1", onLoan: false },
      { barcode: "A2", onLoan: false },
    ];
    const api = fakeApi()
      .on("GET", "/v1/titles/9780261102217", () => ({
        status: 200,
        body: title({ copies, copyCount: copies.length, availableCopies: copies.length, available: true }),
      }))
      .on("POST", "/v1/titles/9780261102217/copies", () => {
        copies = [...copies, { barcode: "LBM0000000042", onLoan: false }];
        return { status: 201, body: { barcode: "LBM0000000042", onLoan: false } };
      });
    const user = userEvent.setup();
    renderAt("/titles/9780261102217");

    expect(await screen.findByText("2 copies")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Add copy" }));

    expect(api.sent("POST", "/v1/titles/9780261102217/copies")[0]?.body).toEqual({});
    expect(await screen.findByText("3 copies")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Copy LBM0000000042 added.");
  });

  it("AC-BOOK-002-2: a barcode already in use is reported", async () => {
    const api = fakeApi()
      .on("GET", "/v1/titles/9780261102217", { status: 200, body: title() })
      .on("POST", "/v1/titles/9780261102217/copies", { status: 409, body: error("BARCODE_IN_USE") });
    const user = userEvent.setup();
    renderAt("/titles/9780261102217");

    await user.type(await screen.findByLabelText("Barcode (optional)"), "A1");
    await user.click(screen.getByRole("button", { name: "Add copy" }));

    expect(api.sent("POST", "/v1/titles/9780261102217/copies")[0]?.body).toEqual({ barcode: "A1" });
    expect(await screen.findByRole("alert")).toHaveTextContent("Another copy already holds this barcode.");
  });
});

describe("REQ-BOOK-003 change a title", () => {
  it("AC-BOOK-003-1: correcting the author sends only title, author and year and shows the result", async () => {
    let current = title({ author: "J. R. R. Tolkein", copyCount: 3, availableCopies: 2, available: true });
    const api = fakeApi()
      .on("GET", "/v1/titles/9780261102217", () => ({ status: 200, body: current }))
      .on("PATCH", "/v1/titles/9780261102217", (r) => {
        current = { ...current, ...(r.body as object) };
        return { status: 200, body: current };
      });
    const user = userEvent.setup();
    renderAt("/titles/9780261102217");

    const author = await screen.findByLabelText("Author");
    await user.clear(author);
    await user.type(author, "J. R. R. Tolkien");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    expect(api.sent("PATCH", "/v1/titles/9780261102217")[0]?.body).toEqual({
      title: "The Hobbit",
      author: "J. R. R. Tolkien",
      publicationYear: 1937,
    });
    expect(await screen.findByRole("status")).toHaveTextContent("Changes saved.");
    expect(screen.getByText("3 copies")).toBeInTheDocument();
  });

  it("AC-BOOK-003-2: the ISBN is shown but offered for no editing, and ISBN_IMMUTABLE is reported if the API refuses", async () => {
    fakeApi()
      .on("GET", "/v1/titles/9780261102217", { status: 200, body: title() })
      .on("PATCH", "/v1/titles/9780261102217", { status: 400, body: error("ISBN_IMMUTABLE", "isbn") });
    const user = userEvent.setup();
    renderAt("/titles/9780261102217");

    expect(await screen.findByText("ISBN 9780261102217")).toBeInTheDocument();
    expect(screen.queryByLabelText("ISBN")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("A title's ISBN cannot be changed.");
  });
});

describe("REQ-BOOK-004 remove a copy", () => {
  it("AC-BOOK-004-1: removing a copy on the shelf asks for confirmation, then shows one fewer copy", async () => {
    let copies = [
      { barcode: "A1", onLoan: false },
      { barcode: "A2", onLoan: false },
    ];
    const api = fakeApi()
      .on("GET", "/v1/titles/9780261102217", () => ({ status: 200, body: title({ copies, copyCount: copies.length }) }))
      .on("DELETE", /^\/v1\/copies\//, (r) => {
        copies = copies.filter((c) => `/v1/copies/${c.barcode}` !== r.path);
        return { status: 204 };
      });
    const user = userEvent.setup();
    renderAt("/titles/9780261102217");

    await user.click(await screen.findByRole("button", { name: "Remove copy A1" }));
    expect(api.sent("DELETE", "/v1/copies/A1")).toHaveLength(0); // nothing happens before confirmation
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Remove" }));

    expect(api.sent("DELETE", "/v1/copies/A1")).toHaveLength(1);
    expect(await screen.findByText("1 copy")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove copy A1" })).toBeNull();
  });

  it("AC-BOOK-004-2: a copy on loan that the API refuses to remove is reported as on loan", async () => {
    fakeApi()
      .on("GET", "/v1/titles/9780261102217", {
        status: 200,
        body: title({ copies: [{ barcode: "A1", onLoan: true }], copyCount: 1 }),
      })
      .on("DELETE", "/v1/copies/A1", { status: 409, body: error("COPY_ON_LOAN") });
    const user = userEvent.setup();
    renderAt("/titles/9780261102217");

    await user.click(await screen.findByRole("button", { name: "Remove copy A1" }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Remove" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The copy is on loan and cannot be removed.");
    expect(screen.getByText("1 copy")).toBeInTheDocument();
  });

  it("AC-BOOK-004-3: a title whose copies are all gone is shown in search results as unavailable", async () => {
    fakeApi().on("GET", "/v1/titles", {
      status: 200,
      body: { items: [{ ...title(), copies: undefined }], total: 1, page: 1, pageSize: 50 },
    });
    const user = userEvent.setup();
    renderAt("/");

    await user.selectOptions(screen.getByLabelText("Search by"), "isbn");
    await user.type(screen.getByLabelText("Search term"), "9780261102217");
    await user.click(screen.getByRole("button", { name: "Search" }));

    const row = await screen.findByRole("row", { name: /The Hobbit/ });
    expect(row).toHaveTextContent("Unavailable (0 copies)");
  });
});

describe("REQ-BOOK-005 search", () => {
  it("AC-BOOK-005-1: an author search asks for no page and shows the first 50 and the total of 120", async () => {
    const api = fakeApi().on("GET", "/v1/titles", { status: 200, body: leGuinPage(1, 50) });
    const user = userEvent.setup();
    renderAt("/");

    await user.selectOptions(screen.getByLabelText("Search by"), "author");
    await user.type(screen.getByLabelText("Search term"), "Le Guin");
    await user.click(screen.getByRole("button", { name: "Search" }));

    await screen.findByText("120 matches · page 1 of 3");
    const sent = api.sent("GET", "/v1/titles")[0]!;
    expect(sent.query.get("author")).toBe("Le Guin");
    expect(sent.query.has("page")).toBe(false);
    expect(screen.getAllByRole("row")).toHaveLength(51); // header + 50
  });

  it("AC-BOOK-005-2: moving to the third page requests page 3 and shows matches 101 to 120", async () => {
    const api = fakeApi().on("GET", "/v1/titles", (r) => {
      const page = Number(r.query.get("page") ?? "1");
      return { status: 200, body: leGuinPage(page, page === 3 ? 20 : 50) };
    });
    const user = userEvent.setup();
    renderAt("/");

    await user.selectOptions(screen.getByLabelText("Search by"), "author");
    await user.type(screen.getByLabelText("Search term"), "Le Guin");
    await user.click(screen.getByRole("button", { name: "Search" }));
    await user.click(await screen.findByRole("button", { name: "Next page" }));
    await screen.findByText("120 matches · page 2 of 3");
    await user.click(screen.getByRole("button", { name: "Next page" }));

    await screen.findByText("120 matches · page 3 of 3");
    expect(api.requests.at(-1)?.query.get("page")).toBe("3");
    expect(api.requests.at(-1)?.query.get("author")).toBe("Le Guin");
    expect(screen.getByText("Earthsea volume 101")).toBeInTheDocument();
    expect(screen.getByText("Earthsea volume 120")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next page" })).toBeDisabled();
  });

  it("AC-BOOK-005-3: a search with no match shows an empty result, not an error", async () => {
    fakeApi().on("GET", "/v1/titles", { status: 200, body: { items: [], total: 0, page: 1, pageSize: 50 } });
    const user = userEvent.setup();
    renderAt("/");

    await user.type(screen.getByLabelText("Search term"), "zzz");
    await user.click(screen.getByRole("button", { name: "Search" }));

    expect(await screen.findByText("No titles match.")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("REQ-BOOK-006 availability in results", () => {
  it("AC-BOOK-006-1: a title with one of three copies on the shelf shows 1 of 3 available", async () => {
    fakeApi().on("GET", "/v1/titles", {
      status: 200,
      body: { items: [{ ...title({ copyCount: 3, availableCopies: 1, available: true }), copies: undefined }], total: 1, page: 1, pageSize: 50 },
    });
    const user = userEvent.setup();
    renderAt("/");

    await user.type(screen.getByLabelText("Search term"), "Hobbit");
    await user.click(screen.getByRole("button", { name: "Search" }));

    expect(await screen.findByRole("row", { name: /The Hobbit/ })).toHaveTextContent("1 of 3 available");
  });

  it("AC-BOOK-006-2: a title with every copy on loan is still listed, as 0 of 2 available", async () => {
    fakeApi().on("GET", "/v1/titles", {
      status: 200,
      body: { items: [{ ...title({ copyCount: 2, availableCopies: 0, available: false }), copies: undefined }], total: 1, page: 1, pageSize: 50 },
    });
    const user = userEvent.setup();
    renderAt("/");

    await user.type(screen.getByLabelText("Search term"), "Hobbit");
    await user.click(screen.getByRole("button", { name: "Search" }));

    expect(await screen.findByRole("row", { name: /The Hobbit/ })).toHaveTextContent("0 of 2 available");
  });
});
