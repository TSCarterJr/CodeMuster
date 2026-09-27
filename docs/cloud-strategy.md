# Cloud strategy

Direction agreed in conversation with Tim on 2026-09-27. This is a plan, not a build: the hosted
service is a separate project. The only work it asks of this repository is the hidden engine
interface (ENGINE1, ENGINE2) and spend tracking (COST1). `idea.md` Part 2 described the same
direction; this document records the decisions made since.

## What is free and what is paid

The CLI stays free and unlimited under the current license (D41): personal and internal company
use, with no cap on agents, units or repositories. A cap in the CLI would protect nothing, because
every model call runs on the user's own subscription and machine, and .NET assemblies are easy to
patch. Limits belong where they cannot be edited: on CodeMuster's servers.

The paid product is everything the CLI does not do on its own:

| Paid value | Why a company pays | Builds on |
|---|---|---|
| Nobody runs it | No installs, restores, babysitting or lost ledgers | The CLI, run in containers |
| Runs where the team works | Every pull request, nightly, on merge; fix pull requests opened for them | L1, L7, `fix` |
| Speed | A fleet of agent executors per run instead of one laptop's rate limit | T13.6, PERF7 |
| One view of the organization | Coverage, findings, trends and history across every repository | L12 finding identity |
| Live control | Real-time progress with ETA, workers up or down, model changes, pause, stop, restart | ENGINE1, ENGINE2 |
| Proof | Retained, dated coverage and verification reports for SOC 2, PCI and customer questionnaires | The ledger |
| Company requirements | SSO, roles, audit logs, retention terms, region choice, SLA, support | Service only |
| Code map and middle-out impact | The architecture map and "what does this change affect" on every pull request | MAP1 to MAP4 |

The live dashboard (progress, workers, model, pause and resume) is cloud only. The CLI supports it
through an undocumented, versioned machine interface enabled by an environment variable (D65).

"Open source" is the wrong label for the current license. Say "free for personal and internal
use" or "source-available". If larger companies should pay even when they run the CLI themselves,
that needs a license change for future versions (for example free under a company-size
threshold), which is a new decision replacing D41 and has not been made.

## Tiers

| | Team | Business | Enterprise |
|---|---|---|---|
| AI keys | Managed: provider cost plus a markup | Managed | Bring your own key (their Anthropic, Azure or AWS contract), or managed at a negotiated markup |
| Base fee (starting point) | About $99 per repository per month | About $299 per month for 5 repositories, then $49 each | About $1,500 to $3,000+ per month, annual contract |
| Adds | Scheduled scans, pull request checks, dashboard | Fix pull requests, code map, impact analysis, team triage | SSO/SCIM, audit logs, retention and region controls, SLA, optional self-hosted runner |

Bring your own key is Enterprise only: it removes CodeMuster's margin on AI, so it is priced into a
higher base fee, and the customers who ask for it (procurement, data terms, committed spend) are
enterprises anyway. Managed-key plans carry a monthly AI budget per account with alerts, so one
large repository on a fixed plan cannot become a loss.

## What a customer costs: ToolbagCRM as the example

Estimates from the review conversation; replace them with measured figures once COST1 records real
usage per call. Assumptions for a full pass: about 2,000 audit units, 6,000 verifications and 800
file repairs.

| Phase | Batched Sonnet 5 | Opus 5.5 |
|---|---|---|
| First full pass (audit, verify, fix) | about $325 | about $975 |
| Typical month after (15 to 20 percent of code changes) | about $80 | about $160 |
| Compute (containers), per full pass | $1 to $3 | $1 to $3 |
| Storage and transfer, per month | cents | cents |

These assume the service calls the model API directly with only the pack. Running through the
Claude Code harness adds about 45,000 tokens of harness prompt per call (measured 2026-09-27: a
one-word answer cost $0.36 on the first call because the prompt was written to the one-hour cache,
and about 2 cents on later calls that read it). Across 9,000 calls that is $150 to $250 more per full
pass, so the hosted service should call the API directly.

With a 25 percent markup on AI and the Team platform fee, ToolbagCRM would pay about $505 in its
first month and about $200 a month after, a gross margin of roughly 35 to 45 percent. Onboarding
costs three to five normal months, so either charge for it or offer the first audit as the sales
hook.

## Architecture

- **Engine storage stays SQLite.** Each repository's ledger is one file, exactly as in the CLI, so the
  cloud runs the same engine. Nothing is pinned to a server: the file lives in object storage.
- **Service storage is PostgreSQL:** accounts, organizations, repositories, schedules, the run queue,
  billing, live events, findings history across repositories, audit logs.
- **Workers are stateless.** A job leases its repository (one job per repository at a time, as D46
  does locally), downloads the ledger, runs the CLI with the hidden event stream on, forwards events
  to Postgres, uploads the ledger as a new version with a conditional write, and releases the lease.
  Checkpoints during long runs limit what a crashed worker loses; an expired lease lets another
  worker take over.
- **One large run spreads across machines** by separating the coordinator, which owns the ledger and
  records results, from stateless agent executors that only call the model.
- **Costs to avoid:** keep workers and buckets in the same region, route S3 traffic through a VPC
  gateway endpoint rather than a NAT gateway, and expire old ledger versions with a lifecycle rule.
  Same-region transfer is free; a ToolbagCRM-sized ledger is about 40 MB.
- **Never keep SQLite on a shared network drive** with several writers: its locking is unreliable
  there.
- A Postgres-backed ledger for the cloud stays possible behind `ILedger` if one repository ever needs
  several writers at once; nothing measured so far needs it.

## Order

1. Free CLI: close the review's honesty and CI gaps, record real spend (COST1), ship the code map.
2. Cheapest demand test: a GitHub Action with a customer-supplied key.
3. Hosted beta for design partners: GitHub App, containerized jobs, pull request comments,
   dashboard, live progress; ToolbagCRM as the first customer.
4. Enterprise: self-hosted runner, SSO, audit logs, SOC 2.

Before marketing the service: record the monetization direction as a decision (D22 deferred it
until the CLI existed), check that "CodeMuster" can be trademarked, and settle per-repository,
per-seat or usage-based pricing from measured COST1 data.
