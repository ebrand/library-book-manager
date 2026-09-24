import { useEffect, useState } from "react";
import { Alert, Button, SelectField, Status, Table } from "@platform-standin/index";
import { changeRole, listUsers } from "../api/identity";
import type { Role, SessionUser } from "../auth/session";
import { describeError } from "../messages";

const roles = [
  { value: "member", label: "Member" },
  { value: "librarian", label: "Librarian" },
  { value: "administrator", label: "Administrator" },
] as const;

const withArticle = (role: Role) => (role === "administrator" ? "an administrator" : `a ${role}`);

/** REQ-AUTH-004: administrators change other users' roles. */
export function UsersPage(props: { self: SessionUser }) {
  const [users, setUsers] = useState<SessionUser[] | null>(null);
  const [pending, setPending] = useState<Record<string, Role>>({});
  const [notice, setNotice] = useState<{ kind: "status" | "alert"; text: string } | null>(null);

  useEffect(() => {
    void listUsers().then((result) => {
      if (result.ok) setUsers(result.data);
      else setNotice({ kind: "alert", text: describeError(result.error) });
    });
  }, []);

  async function apply(user: SessionUser) {
    const role = pending[user.userId] ?? user.role;
    const result = await changeRole(user.userId, role);
    if (!result.ok) {
      setNotice({ kind: "alert", text: describeError(result.error) });
      return;
    }
    setUsers((all) => all?.map((u) => (u.userId === user.userId ? result.data : u)) ?? null);
    setNotice({ kind: "status", text: `${result.data.email} is now ${withArticle(result.data.role)}.` });
  }

  return (
    <section>
      <h1>Users</h1>
      {notice?.kind === "alert" && <Alert>{notice.text}</Alert>}
      {notice?.kind === "status" && <Status>{notice.text}</Status>}
      {users && (
        <Table
          caption="Users and their roles"
          columns={["Email", "Role", "Change role"]}
          rows={users.map((u) => ({
            key: u.userId,
            cells: [
              u.email,
              u.role,
              u.userId === props.self.userId ? (
                "You cannot change your own role."
              ) : (
                <>
                  <SelectField
                    label={`Role for ${u.email}`}
                    value={pending[u.userId] ?? u.role}
                    options={roles}
                    onChange={(role) => setPending((p) => ({ ...p, [u.userId]: role }))}
                  />
                  <Button label={`Change role for ${u.email}`} onClick={() => void apply(u)}>
                    Change role
                  </Button>
                </>
              ),
            ],
          }))}
        />
      )}
    </section>
  );
}
