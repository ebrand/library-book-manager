# Platform stand-in — NOT the platform library

plt-ui-001 REQ-UI-003 and plt-ui-react-001 REQ-UIR-002 require the platform component library,
router and data client. Their package names and APIs are not available to this build
(QUESTIONS.md Q-STD-01), so this directory stands in for them behind one import path,
`@platform-standin/*`. Application code under `src/` imports primitives, routing and data
fetching only from here (enforced by `tests/conformance.test.ts`), so replacing it is a
change of import path plus adapting to the real API.

A real platform conformance check should, and would, flag this directory. It is deliberately
named so it cannot be mistaken for the platform package.
