import type { ApiResult } from "@platform-standin/index";
import { client } from "./client";

export interface Member {
  userId: string;
  email: string;
}

export interface Loan {
  barcode: string;
  isbn: string;
  title: string;
  member: Member;
  checkedOutOn: string;
  dueOn: string;
  returnedOn: string | null;
  returnedLate: boolean;
  overdue: boolean;
}

export const findMember = (email: string): Promise<ApiResult<Member[]>> => client.get("/v1/members", { email });

export const checkOut = (barcode: string, memberId: string): Promise<ApiResult<Loan>> =>
  client.post("/v1/loans", { barcode, memberId });

export const returnCopy = (barcode: string): Promise<ApiResult<Loan>> =>
  client.post(`/v1/copies/${encodeURIComponent(barcode)}/return`, undefined);

export const memberLoans = (memberId: string): Promise<ApiResult<Loan[]>> =>
  client.get(`/v1/members/${encodeURIComponent(memberId)}/loans`);

export const lendingReport = (): Promise<ApiResult<Loan[]>> => client.get("/v1/loans");

const months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

/** "2026-03-22" → "22 March 2026". Calendar dates from the API carry no time zone; none is applied. */
export function formatDay(day: string): string {
  const [year, month, date] = day.split("-").map(Number);
  return `${date} ${months[(month ?? 1) - 1]} ${year}`;
}
