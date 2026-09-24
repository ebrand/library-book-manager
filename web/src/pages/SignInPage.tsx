import { useState } from "react";
import { Alert, Button, Form, Status, TextField } from "@platform-standin/index";
import { signIn } from "../api/identity";
import { startSession } from "../auth/session";
import { describeError } from "../messages";

/** REQ-AUTH-001, REQ-AUTH-003. */
export function SignInPage(props: { notice: string | null }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    const result = await signIn(email.trim(), password);
    if (result.ok) {
      startSession(result.data);
    } else {
      setError(describeError(result.error));
    }
  }

  return (
    <section>
      <h1>Sign in</h1>
      {props.notice && !error && <Status>{props.notice}</Status>}
      {error && <Alert>{error}</Alert>}
      <Form label="Sign in" onSubmit={() => void submit()}>
        <TextField label="Email" type="email" autoComplete="username" value={email} onChange={setEmail} />
        <TextField label="Password" type="password" autoComplete="current-password" value={password} onChange={setPassword} />
        <Button type="submit" variant="primary">
          Sign in
        </Button>
      </Form>
    </section>
  );
}
