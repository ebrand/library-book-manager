import type { ReactNode } from "react";
import { Button, Menu, Router, type MenuItem } from "@platform-standin/index";
import { signOut } from "./api/identity";
import { endSession, useSession, type Role } from "./auth/session";
import { AddTitlePage } from "./pages/AddTitlePage";
import { LendingPage } from "./pages/LendingPage";
import { MyLoansPage } from "./pages/MyLoansPage";
import { ReportPage } from "./pages/ReportPage";
import { SearchPage } from "./pages/SearchPage";
import { SignInPage } from "./pages/SignInPage";
import { TitlePage } from "./pages/TitlePage";
import { UsersPage } from "./pages/UsersPage";

/** What each role is offered (Q-WEB-03). The API enforces the same rules regardless. */
const menuFor: Record<Role, readonly MenuItem[]> = {
  member: [
    { to: "/", label: "Search" },
    { to: "/loans/mine", label: "My loans" },
  ],
  librarian: [
    { to: "/", label: "Search" },
    { to: "/titles/new", label: "Add a title" },
    { to: "/lending", label: "Lending" },
    { to: "/loans", label: "On loan" },
  ],
  administrator: [
    { to: "/", label: "Search" },
    { to: "/users", label: "Users" },
  ],
};

export function App() {
  const { session, notice } = useSession();
  if (!session) {
    return (
      <main>
        <SignInPage notice={notice} />
      </main>
    );
  }

  const role = session.user.role;
  // The API enforces every role rule; this only avoids offering what would be refused.
  const only = (allowed: Role, page: ReactNode) => (role === allowed ? page : <p>Your role does not permit this.</p>);

  async function leave() {
    await signOut();
    endSession(null);
  }

  return (
    <>
      <header>
        <Menu
          label="Main"
          home={{ to: "/", label: "Library Book Manager" }}
          items={menuFor[role]}
          end={
            <>
              <p>
                Signed in as {session.user.email} ({role})
              </p>
              <Button onClick={() => void leave()}>Sign out</Button>
            </>
          }
        />
      </header>
      <main>
        <Router
          routes={[
            { path: "/", element: <SearchPage canManage={role === "librarian"} /> },
            { path: "/titles/new", element: only("librarian", <AddTitlePage />) },
            { path: "/titles/:isbn", element: only("librarian", <TitlePage />) },
            { path: "/users", element: only("administrator", <UsersPage self={session.user} />) },
            { path: "/loans/mine", element: only("member", <MyLoansPage self={session.user} />) },
            { path: "/lending", element: only("librarian", <LendingPage />) },
            { path: "/loans", element: only("librarian", <ReportPage />) },
          ]}
          fallback={<p>Page not found.</p>}
        />
      </main>
    </>
  );
}
