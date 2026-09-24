import type { ApiResult } from "@platform-standin/index";
import type { Role, Session, SessionUser } from "../auth/session";
import { client } from "./client";

export const signIn = (email: string, password: string): Promise<ApiResult<Session>> =>
  client.post("/v1/sessions", { email, password });

export const signOut = (): Promise<ApiResult<void>> => client.del("/v1/sessions/current");

export const listUsers = (): Promise<ApiResult<SessionUser[]>> => client.get("/v1/users");

export const changeRole = (userId: string, role: Role): Promise<ApiResult<SessionUser>> =>
  client.put(`/v1/users/${encodeURIComponent(userId)}/role`, { role });
