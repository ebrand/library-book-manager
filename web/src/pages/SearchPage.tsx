import { useState } from "react";
import { Alert, Button, Form, Link, SelectField, Table, TextField } from "@platform-standin/index";
import { searchTitles, type SearchField, type SearchPage as Results, type TitleSummary } from "../api/catalogue";
import { copiesText, describeError } from "../messages";

const fields = [
  { value: "title", label: "Title" },
  { value: "author", label: "Author" },
  { value: "isbn", label: "ISBN" },
] as const;

function availability(t: TitleSummary) {
  return t.copyCount === 0 ? `Unavailable (${copiesText(0)})` : `${t.availableCopies} of ${t.copyCount} available`;
}

/** REQ-BOOK-005, REQ-BOOK-006: search by one field, 50 to a page, with availability. */
export function SearchPage(props: { canManage: boolean }) {
  const [field, setField] = useState<SearchField>("title");
  const [term, setTerm] = useState("");
  const [searched, setSearched] = useState<{ field: SearchField; term: string } | null>(null);
  const [results, setResults] = useState<Results | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function run(query: { field: SearchField; term: string }, page: number) {
    const result = await searchTitles(query.field, query.term, page);
    if (result.ok) {
      setSearched(query);
      setResults(result.data);
      setError(null);
    } else {
      setError(describeError(result.error));
    }
  }

  const pages = results ? Math.max(1, Math.ceil(results.total / results.pageSize)) : 0;

  return (
    <section>
      <h1>Search the catalogue</h1>
      <Form label="Search the catalogue" onSubmit={() => void run({ field, term: term.trim() }, 1)}>
        <SelectField label="Search by" value={field} options={fields} onChange={setField} />
        <TextField label="Search term" value={term} onChange={setTerm} />
        <Button type="submit" variant="primary">
          Search
        </Button>
      </Form>
      {error && <Alert>{error}</Alert>}
      {results && results.total === 0 && <p>No titles match.</p>}
      {results && results.total > 0 && searched && (
        <>
          <p>
            {results.total} {results.total === 1 ? "match" : "matches"} · page {results.page} of {pages}
          </p>
          <Table
            caption="Matching titles"
            columns={["Title", "Author", "Year", "ISBN", "Availability"]}
            rows={results.items.map((t) => ({
              key: t.isbn,
              cells: [props.canManage ? <Link to={`/titles/${encodeURIComponent(t.isbn)}`}>{t.title}</Link> : t.title, t.author, t.publicationYear, t.isbn, availability(t)],
            }))}
          />
          <Button label="Previous page" disabled={results.page <= 1} onClick={() => void run(searched, results.page - 1)}>
            Previous
          </Button>
          <Button label="Next page" disabled={results.page >= pages} onClick={() => void run(searched, results.page + 1)}>
            Next
          </Button>
        </>
      )}
    </section>
  );
}
