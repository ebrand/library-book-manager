import type { ApiErrorBody } from "@platform-standin/index";

const fieldLabels: Record<string, string> = {
  isbn: "ISBN",
  title: "Title",
  author: "Author",
  publicationYear: "Publication year",
  barcode: "Barcode",
  email: "Email",
  password: "Password",
  role: "Role",
};

const messages: Record<string, string> = {
  ISBN_ALREADY_EXISTS: "A title with this ISBN is already in the catalogue.",
  BARCODE_IN_USE: "Another copy already holds this barcode.",
  ISBN_IMMUTABLE: "A title's ISBN cannot be changed.",
  COPY_ON_LOAN: "That copy is already on loan.",
  TITLE_NOT_FOUND: "No title in the catalogue has this ISBN.",
  COPY_NOT_FOUND: "No copy in the catalogue has this barcode.",
  COPY_NOT_ON_LOAN: "That copy is not on loan.",
  LOAN_LIMIT_REACHED: "The member already has 10 books on loan.",
  MEMBER_HAS_OVERDUE_LOAN: "The member has an overdue loan and cannot borrow until it is returned.",
  BORROWER_NOT_A_MEMBER: "Books can only be lent to members.",
  FORBIDDEN: "Your role does not permit this.",
  INVALID_CREDENTIALS: "The email address or password is not correct.",
  ACCOUNT_LOCKED: "Too many failed sign-ins. Try again in 15 minutes.",
  USER_NOT_FOUND: "That user no longer exists.",
  UNAUTHENTICATED: "Sign in to continue.",
  INVALID_SEARCH: "That search could not be run.",
  INVALID_REQUEST: "The request could not be read.",
  NETWORK: "The server could not be reached.",
};

export function describeError(error: ApiErrorBody, overrides: Record<string, string> = {}): string {
  if (overrides[error.error]) return overrides[error.error]!;
  const label = error.field ? (fieldLabels[error.field] ?? error.field) : undefined;
  if (error.error === "FIELD_REQUIRED" && label) return `${label} is required.`;
  if (error.error === "INVALID_FIELD" && label) return `${label} is not valid.`;
  return messages[error.error] ?? `Something went wrong (${error.error}).`;
}

export const copiesText = (n: number) => (n === 1 ? "1 copy" : `${n} copies`);
