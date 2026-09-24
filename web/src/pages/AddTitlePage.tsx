import { useState } from "react";
import { Alert, Button, Form, TextField, navigate } from "@platform-standin/index";
import { addTitle, yearFromInput } from "../api/catalogue";
import { describeError } from "../messages";

type FieldName = "isbn" | "title" | "author" | "publicationYear";

/** REQ-BOOK-001: add a title (librarian). */
export function AddTitlePage() {
  const [values, setValues] = useState<Record<FieldName, string>>({ isbn: "", title: "", author: "", publicationYear: "" });
  const [fieldError, setFieldError] = useState<{ field: string; message: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const set = (name: FieldName) => (value: string) => setValues((v) => ({ ...v, [name]: value }));
  const errorFor = (name: FieldName) => (fieldError?.field === name ? fieldError.message : undefined);

  async function submit() {
    const result = await addTitle({
      isbn: values.isbn.trim(),
      title: values.title.trim(),
      author: values.author.trim(),
      publicationYear: yearFromInput(values.publicationYear),
    });
    if (result.ok) {
      navigate(`/titles/${encodeURIComponent(result.data.isbn)}`);
      return;
    }
    const message = describeError(result.error);
    if (result.error.field) {
      setFieldError({ field: result.error.field, message });
      setError(null);
    } else {
      setFieldError(null);
      setError(message);
    }
  }

  return (
    <section>
      <h1>Add a title</h1>
      {error && <Alert>{error}</Alert>}
      <Form label="Add a title" onSubmit={() => void submit()}>
        <TextField label="ISBN" value={values.isbn} onChange={set("isbn")} error={errorFor("isbn")} />
        <TextField label="Title" value={values.title} onChange={set("title")} error={errorFor("title")} />
        <TextField label="Author" value={values.author} onChange={set("author")} error={errorFor("author")} />
        <TextField
          label="Publication year"
          inputMode="numeric"
          value={values.publicationYear}
          onChange={set("publicationYear")}
          error={errorFor("publicationYear")}
        />
        <Button type="submit" variant="primary">
          Add title
        </Button>
      </Form>
    </section>
  );
}
