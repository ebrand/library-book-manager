import type { ApiResult } from "@platform-standin/index";
import { client } from "./client";

// v1 representations from api/src/LibraryBookManager.Api/Contract/openapi.v1.yaml.

export interface Copy {
  barcode: string;
  onLoan: boolean;
}

export interface TitleSummary {
  isbn: string;
  title: string;
  author: string;
  publicationYear: number;
  copyCount: number;
  availableCopies: number;
  available: boolean;
}

export interface Title extends TitleSummary {
  copies: Copy[];
}

export interface SearchPage {
  items: TitleSummary[];
  total: number;
  page: number;
  pageSize: number;
}

export type SearchField = "title" | "author" | "isbn";

export interface NewTitle {
  isbn: string;
  title: string;
  author: string;
  /** null when the field was left empty; a string when it is not a whole number, for the API to refuse. */
  publicationYear: number | string | null;
}

export type TitleChanges = Omit<NewTitle, "isbn">;

const titlePath = (isbn: string) => `/v1/titles/${encodeURIComponent(isbn)}`;

export const searchTitles = (field: SearchField, term: string, page: number): Promise<ApiResult<SearchPage>> =>
  client.get("/v1/titles", { [field]: term, page: page === 1 ? undefined : page });

export const addTitle = (title: NewTitle): Promise<ApiResult<Title>> => client.post("/v1/titles", title);

export const getTitle = (isbn: string): Promise<ApiResult<Title>> => client.get(titlePath(isbn));

export const updateTitle = (isbn: string, changes: TitleChanges): Promise<ApiResult<Title>> =>
  client.patch(titlePath(isbn), changes);

export const addCopy = (isbn: string, barcode: string): Promise<ApiResult<Copy>> =>
  client.post(`${titlePath(isbn)}/copies`, barcode ? { barcode } : {});

export const removeCopy = (barcode: string): Promise<ApiResult<void>> =>
  client.del(`/v1/copies/${encodeURIComponent(barcode)}`);

/** Empty stays null (the API reports FIELD_REQUIRED); non-numbers go through as text for the API to refuse. */
export function yearFromInput(value: string): number | string | null {
  const trimmed = value.trim();
  if (trimmed === "") return null;
  return /^-?\d+$/.test(trimmed) ? Number(trimmed) : trimmed;
}
