// The session the web app keeps in sessionStorage after signing in (see src/auth/session.ts).

export const users = {
  member: { userId: "11111111-1111-1111-1111-111111111111", email: "reader@example.test", role: "member" },
  librarian: { userId: "22222222-2222-2222-2222-222222222222", email: "shelves@example.test", role: "librarian" },
  administrator: { userId: "33333333-3333-3333-3333-333333333333", email: "head@example.test", role: "administrator" },
};

export function signedInAs(role: keyof typeof users) {
  window.sessionStorage.setItem("lbm.session", JSON.stringify({ token: `tok-${role}`, user: users[role] }));
}
