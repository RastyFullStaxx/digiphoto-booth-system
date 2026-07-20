# DigiPhoto Booth System

Windows-first, offline-capable photobooth software for certified camera capture,
editable event templates, silent printing, optional verified QR Ph payment, and
private QR galleries.

This repository is in greenfield execution. Start with:

- `PROJECT_OVERVIEW.md` — product, users, architecture, and first vertical slice.
- `IMPLEMENTATION_PLAN.md` — locked scope, rollout, technology, and acceptance gates.
- `PRODUCT.md` — users, positioning, personality, and design principles.
- `DESIGN.md` — visual tokens, responsive behavior, components, motion, and content.
- `AGENTS.md` — implementation invariants and repository working rules.
- `PLAN_AMENDMENT_PROPOSAL.md` — provider/privacy corrections awaiting explicit
  approval before the locked plan is revised.
- `docs/operations/development.md` — exact simulator setup, run, smoke, and safety
  instructions.

The initial runnable build has executable camera and printer simulators plus an
explicitly browser-only payment-gate demo. Physical Canon/DNP proof, real PayMongo
validation, and cloud-wired template publication remain delivery gates rather than
claims made from simulated behavior.
