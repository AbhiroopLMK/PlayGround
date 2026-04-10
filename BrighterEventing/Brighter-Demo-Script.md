# Brighter demo — recorded session script

**Audience:** Engineers and leads  
**Suggested length:** 15–20 minutes (+ optional Q&A)  
**References:** SE-Brighter High Level Design (`SE-Brighter High Level Design-010426-062959.pdf`), SE-Draft Brighter Implementation Guide (`SE-Draft_ Brighter Implementation Guide-010426-063032.pdf`), POC repo (`PlayGround/BrighterEventing`), translation-facade integration (e.g. PR discussion).

---

## Opening (0:00–1:00)

**Say:**

Hi everyone. This short demo is to share what we’ve learned about **Brighter** on our platform: what the architecture asks us to do, what we proved in code, and what broke when we took it into a real service.

I’m going to walk through **three things** in one story. **First**, why we’re looking at Brighter and what our architecture docs ask for. **Second**, what we built in a proof-of-concept to prove those ideas. **Third**, what happened when we tried the same abstractions in a real Azure Functions service — and what that means for rollout and technical debt. By the end you’ll know what works today, what doesn’t fit cleanly, and how I’d sequence decisions before we invest more.

On screen I’ll tie this to the **Brighter High Level Design** and the **Draft Brighter Implementation Guide** so you can see how the POC maps to what we were asked to follow.

**On screen:** Title slide — your name, date, links to Confluence/wiki or PDFs if used internally.

---

## Part 1 — HLD: what we were asked to achieve (≈3–4 min)

### 1.1 Context and intent

**Say:**

The HLD positions Brighter as the way we **standardise internal asynchronous communication** and **durable execution**, reduce the cost of **rolling our own** messaging, and keep **portability** between **Azure Service Bus in cloud** and **RabbitMQ on-prem**. Scope is **internal** eventing between platform and product services — not a **public** event API.

Everything I built in the POC was scoped to that: one reusable shape, two transports, same handlers.

### 1.2 Requirements (HLD — numbered)

Use one slide listing all nine; speak to meaning briefly.

| # | HLD requirement (short) | POC / service tie-in |
|---|-------------------------|---------------------|
| 1 | **Transport portability** — abstract app code from broker; explicit config per transport | `Transport: RabbitMQ \| AzureServiceBus` + shared library; handlers unchanged when swapping |
| 2 | **Transactional messaging (Outbox)** — persist outbound with domain state | Postgres/Cosmos outbox in POC; deposit + clear pattern |
| 3 | **Consumer idempotency (Inbox)** — dedupe for at-least-once | Postgres/Cosmos inbox on subscriber |
| 4 | **Durable execution & failure semantics** — retries, backoff, circuit-breaker, defer/requeue; broker DLQ for poison | Polly + `Messaging:` consumer / ASB settings (see POC README) |
| 5 | **Dead-letter integration** — delegate DLQ to **broker**, no extra abstraction | Align with broker DLQ; no custom failed-message table for retry (matches Implementation Guide) |
| 6 | **Observability** — correlation/causation, structured telemetry | Headers/baggage; logging guidance in POC |
| 7 | **Ordering & partitioning** — preserve per-key where required | Outbox ordering note + explicit broker semantics (“abstraction, not illusion”) |
| 8 | **Message evolution** — pipelines for versioning, validation, transforms | Mappers + wire envelope as extension boundary |
| 9 | **Security** — transformers, secrets via platform | HLD: consuming teams implement; POC leaves extension points |

**Say:**

Where we couldn’t fully implement something in the **first** production integration, I’ll call it out — especially anything that needs **hosting** Brighter the way the Implementation Guide prefers.

### 1.3 Non-requirements (HLD)

**Say:**

The HLD is clear on what we are **not** solving: **no** general workflow engine, **no** public event API, and **not** exactly-once delivery — we assume **at-least-once** with **Inbox** idempotency. That framed the POC and how I think about duplicates in production.

### 1.4 Default recommendation: Outbox + Inbox (HLD — Architecture)

**Say:**

For **business-relevant** messages, **producers should use Outbox** and **consumers with side effects should use Inbox**, with **limited, justified exceptions** — e.g. naturally idempotent handlers or best-effort low-value traffic. The POC demonstrates the **default** path; the translation façade integration is **partial** and I’ll explain the gap.

### 1.5 Building blocks (Outbox / Inbox)

**Say:**

**Producer:** Outbox store; **dispatcher** (envelopes, transformer pipeline, publish with Polly, mark sent, ordering when reading outbox); optional **sweeper**. **Consumer:** Inbox store; **performer** pipeline — deserialise, transformers, **inbox dedupe**, Polly, then handler. The POC follows that **shape** in code and configuration.

### 1.6 Why Brighter — five pillars (HLD)

One slide, one sentence each:

1. **Separation of concerns** — CQS / hexagonal style; separate HTTP, domain, messaging.  
2. **Encapsulation of cross-cutting concerns** — resilience, logging, validation in **middleware pipelines**.  
3. **Reliability via Outbox and Inbox** — transactional publish + consume-side dedupe.  
4. **Durable execution** — activator/performers, Polly, transactional messaging, **broker DLQ** on terminal failure, correlation in headers.  
5. **Transport flexibility** — pluggable transports; **“abstraction, not illusion”** — ASB vs RabbitMQ differ; we must not pretend they’re identical.

**Say:**

The POC’s **mapper**, **handler**, and **broker registration** separation is deliberate — we configure ASB subjects and correlation rules explicitly, not as a pretend-unified bus.

### 1.7 Risks (HLD — brief)

**Say:**

**Semantic mismatch** between transports — mitigation is guidance and not hiding the bus. **Retry storms** — one layer of Polly, exponential backoff + jitter, circuit-breaker. The POC centralises policy in registration/configuration.

### 1.8 Internal NuGet (HLD — Architecture)

**Say:**

The HLD describes documenting defaults and patterns, then shipping an **internal lightweight NuGet**. That matches **`BrighterEventing.Messaging`** — packable core, hosts add configuration only.

---

## Part 2 — Implementation Guide: how we were told to implement (≈4–5 min)

### 2.1 Purpose

**Say:**

The guide is **practical** and **solution-agnostic**. Preferred end-state: **Brighter owns consumption and publishing directly**; the **service** owns **contracts**, **mapping**, **business handling**, **Inbox**, **Outbox**. I used the “Brighter owns vs service owns” table as a checklist when splitting projects.

### 2.2 Recommended architecture — five responsibilities

**Say:**

**(1)** Brighter broker integration — no business logic. **(2)** Brighter message contract. **(3)** Mapper — wire ↔ request; transport out of handlers. **(4)** Handler — orchestration, Inbox/Outbox, calls application code. **(5)** Application processor — real work; **does not care** which broker delivered. The POC maps one-to-one.

### 2.3 Preferred end-state vs temporary migration (critical for Azure Functions)

**Say:**

The guide: **Brighter subscribes to the broker directly**, maps, invokes handlers, Inbox on receive, Outbox on send. **Do not add a custom broker callback layer** unless **deliberately** doing a **temporary migration step**. I’ll come back to that for **Azure Functions**, because **function triggers** are a different entry point than a Brighter-hosted service activator.

### 2.4 Quick start (Guide)

**Say:**

One queue first; **metadata contract first**; one mapper; thin handler; **Inbox before fan-out**; then retry/DLQ/replay; observability; then scale. The POC grows the same way.

### 2.5 Minimum message metadata (Guide)

**Say:**

Define **messageId**, **correlationId**, **causationId**, **messageType**, **schemaVersion**, **occurredAtUtc**, **topic/routing key**, **content type**. Weak **messageId** is a **contract** problem; never a **new random id per redelivery** for the same inbound message. The POC’s envelope builder and publication metadata keep that explicit.

### 2.6 Mapper identity rules (Guide)

**Say:**

Prefer **broker messageId**; derive deterministically from stable fields only; **no** `Guid.NewGuid()` as inbound fallback; **no** generic whole-payload hash as dedupe key. Mappers were written with those rules in mind.

### 2.7 Inbox and schema (Guide)

**Say:**

Brighter Inbox with the service database; **one contextKey per domain pipeline**; Inbox is protection — handlers still idempotent. Prefer **library schema builders** over hand-copied DDL where applicable.

### 2.8 Send path (Guide)

**Say:**

Small application seam over **`IAmACommandProcessor`**, not broker clients everywhere. **One canonical outbound id** for Brighter and broker — not two different ids. POC publisher registration and `PostAsync` / outbox deposit align.

### 2.9 Receive path and failures (Guide)

**Say:**

Brighter runs pump → mapper → handler → Inbox. Failures: **broker-native** retry and **DLQ**, not a service-local failed-message store — aligns with HLD req 5. For **Azure Service Bus**: keep contract/handler/Inbox/Outbox/policy; **replace** Rabbit topology with **transport-native** behaviour — **do not assume** Rabbit retry/DLQ is universal. POC: ASB correlation rules applied outside Paramore SQL filter limitations — “abstraction, not illusion” in practice.

---

## Part 3 — POC walkthrough: traceability (≈4–5 min)

**Slide: Traceability**

| Document | Topic | POC |
|----------|--------|-----|
| HLD | Req 1 portability | Single `Transport`; ASB + Rabbit |
| HLD | Req 2–3 Outbox/Inbox | Postgres/Cosmos packages |
| HLD | Req 4–5 resilience/DLQ | Consumer policies; broker DLQ |
| HLD | Req 6 observability | Headers/correlation (extend to platform in prod) |
| HLD | Req 8 evolution | Mappers + envelope builder |
| HLD | Internal NuGet | `BrighterEventing.Messaging` net6/net8 |
| Guide | Ownership split | Messaging lib vs domain vs hosts |
| Guide | Mapper / handler | `IAmAMessageMapperAsync`, thin handlers |
| Guide | Metadata + stable id | Wire envelope + publication metadata |
| Guide | ASB ≠ Rabbit | Service Bus converter, correlation rules hosted service |

**Demo (30–60 s):** `appsettings` Publisher/Subscriber, one mapper, one handler; optional outbox/inbox or log line.

**Say:**

This follows the HLD’s reference direction and the Implementation Guide’s layering and metadata-first rules, packaged as the HLD foreshadows for other teams.

---

## Part 4 — Translation façade: gap analysis (≈4–5 min)

**Say:**

The Implementation Guide’s **preferred end-state** is **Brighter-owned subscription and pump**. **Azure Functions v4 in-process** fits **triggers** and WebJobs-style hosting, not the same **generic host** model current **Brighter** stacks expect. Reusing POC packages caused **hosting and dependency** friction; a from-scratch attempt still required **downgrading Brighter** for the **runtime**.

**Compliance framing:**

We are not ignoring the guide — we hit a **platform constraint**. The guide allows a **non-preferred** shape only as **temporary migration**. I treated the Functions work that way: prove **publish** path and bus alignment, accept **cut-backs**, record **debt** until **isolated worker** and **supported .NET** where Brighter fits naturally.

**Naming:**

Direction is **v4 isolated worker** with `Program.cs` and **.NET 8**; **v4 in-process** is legacy with a known **end-of-support** timeline — that’s why “latest Brighter” and “in-process Functions” collided.

**Slide: Followed vs constrained**

- **Followed:** internal scope, mapper/handler separation, metadata awareness, publish through Brighter where possible, broker DLQ story, explicit ASB vs Rabbit.  
- **Constrained:** full Brighter-driven consumer host on in-process Functions; full Outbox/Inbox parity with POC; same Brighter generation as strategic baseline.

---

## Part 5 — Recommendations (≈2 min)

**Say:**

The HLD warns about transport illusions and retry storms. The Implementation Guide says to **sequence** work and use **broker-native** failure paths. Recommendation: **confirm service longevity**; **upgrade strategic hosts** to the model the guide assumes; then **one Brighter generation** and the **internal package**. For retiring services or deprecating buses, avoid deep migration.

Mention **core services on very old stacks** (e.g. .NET Core 3) as a separate programme — rewrite vs façade vs defer — not hidden inside “just add Brighter.”

---

## Part 6 — Close (≈30 s)

**Say:**

To be explicit: the POC implements the **HLD requirements and Why-Brighter pillars**, and follows the **Implementation Guide** on ownership, metadata, mapper identity, send/receive paths, and ASB-specific caveats. The translation façade **validates** those documents where the platform allows and **surfaces** the **hosting** gap we must plan for. Links: POC repo, PR, HLD and Implementation Guide PDFs.

---

## Optional Q&A sound bites

- **“Did you ignore ‘Brighter subscribes directly’?”** — The guide allows a **temporary** non-preferred path; in-process Functions forced that. **Target state** remains isolated worker + Brighter pump.  
- **“HLD requirement 5 (DLQ)?”** — Terminal failure goes to **broker DLQ**; no competing service-local DLQ abstraction.  
- **“Exactly-once?”** — Out of scope per HLD; **at-least-once + Inbox** where wired.

---

## Production tips (not voiced)

- Chapter markers: Intro | HLD | Implementation Guide | POC traceability | Functions | Decisions | Links.  
- B-roll: POC run; sanitised `appsettings` or PR snippet.  
- Invite one-liner: *Brighter matches our HLD in a working POC; Azure Functions v4 in-process forces older Brighter and workarounds — this demo connects the docs, the code, and the rollout decision.*
