import { useCallback, useEffect, useState } from "react";
import { Alert, Button, Dialog, Form, Status, Table, TextField, useParams } from "@platform-standin/index";
import { addCopy, getTitle, removeCopy, updateTitle, yearFromInput, type Title } from "../api/catalogue";
import { copiesText, describeError } from "../messages";

type Notice = { kind: "status" | "alert"; text: string } | null;

/** REQ-BOOK-002, -003, -004: a title's details and copies (librarian). */
export function TitlePage() {
  const isbn = useParams().isbn ?? "";
  const [title, setTitle] = useState<Title | null>(null);
  const [form, setForm] = useState({ title: "", author: "", publicationYear: "" });
  const [barcode, setBarcode] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice>(null);

  const load = useCallback(async () => {
    const result = await getTitle(isbn);
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error) });
      return null;
    }
    setTitle(result.data);
    return result.data;
  }, [isbn]);

  useEffect(() => {
    void load().then((t) => {
      if (t) setForm({ title: t.title, author: t.author, publicationYear: String(t.publicationYear) });
    });
  }, [load]);

  async function save() {
    const result = await updateTitle(isbn, {
      title: form.title.trim(),
      author: form.author.trim(),
      publicationYear: yearFromInput(form.publicationYear),
    });
    if (result.ok) {
      setTitle(result.data);
      setNotice({ kind: "status", text: "Changes saved." });
    } else {
      setNotice({ kind: "alert", text: describeError(result.error) });
    }
  }

  async function add() {
    const result = await addCopy(isbn, barcode.trim());
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error) });
      return;
    }
    setBarcode("");
    await load();
    setNotice({ kind: "status", text: `Copy ${result.data.barcode} added.` });
  }

  async function remove(target: string) {
    setConfirming(null);
    const result = await removeCopy(target);
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error, { COPY_ON_LOAN: "The copy is on loan and cannot be removed." }) });
      return;
    }
    await load();
    setNotice({ kind: "status", text: `Copy ${target} removed.` });
  }

  if (!title) return notice ? <Alert>{notice.text}</Alert> : <p>Loading…</p>;

  return (
    <section>
      <h1>{title.title}</h1>
      <p>ISBN {title.isbn}</p>
      <p>{copiesText(title.copyCount)}</p>
      {notice?.kind === "alert" && <Alert>{notice.text}</Alert>}
      {notice?.kind === "status" && <Status>{notice.text}</Status>}

      <h2>Details</h2>
      <Form label="Title details" onSubmit={() => void save()}>
        <TextField label="Title" value={form.title} onChange={(v) => setForm((f) => ({ ...f, title: v }))} />
        <TextField label="Author" value={form.author} onChange={(v) => setForm((f) => ({ ...f, author: v }))} />
        <TextField
          label="Publication year"
          inputMode="numeric"
          value={form.publicationYear}
          onChange={(v) => setForm((f) => ({ ...f, publicationYear: v }))}
        />
        <Button type="submit" variant="primary">
          Save changes
        </Button>
      </Form>

      <h2>Copies</h2>
      <Table
        caption="Copies of this title"
        columns={["Barcode", "Status", ""]}
        rows={title.copies.map((c) => ({
          key: c.barcode,
          cells: [
            c.barcode,
            c.onLoan ? "On loan" : "On the shelf",
            <Button label={`Remove copy ${c.barcode}`} variant="danger" onClick={() => setConfirming(c.barcode)}>
              Remove
            </Button>,
          ],
        }))}
      />
      <Form label="Add a copy" onSubmit={() => void add()}>
        <TextField label="Barcode (optional)" value={barcode} onChange={setBarcode} />
        <Button type="submit">Add copy</Button>
      </Form>

      <Dialog
        open={confirming !== null}
        title={`Remove copy ${confirming ?? ""}?`}
        actions={
          <>
            <Button variant="danger" onClick={() => confirming && void remove(confirming)}>
              Remove
            </Button>
            <Button onClick={() => setConfirming(null)}>Cancel</Button>
          </>
        }
      >
        <p>The copy leaves the catalogue. Its loan history is kept.</p>
      </Dialog>
    </section>
  );
}
