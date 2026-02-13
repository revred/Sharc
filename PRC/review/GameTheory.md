# Game Theory of the Trust Sandbox

**How to ensure all agents — human, AI, machine, and third-party — play fair**

---

## 1. The Problem with Unsupervised Agents

When 30 agents operate a business, the founder faces a classic multi-agent game theory problem: each agent has its own objective function, its own information, and its own incentive to optimize for local outcomes at the expense of global ones.

Without structural constraints:
- The CMO overspends on campaigns to maximize lead volume, ignoring profitability
- The CFO under-invests in R&D to preserve cash reserves, starving innovation
- A vendor inflates quotes because no one cross-references competitive pricing
- A CNC machine reports optimistic cycle times because its firmware was last calibrated in 2024
- An AI agent hallucinates a market trend to justify a recommendation it was otherwise uncertain about

These are not hypothetical. These are the **default failure modes** of any multi-agent system without enforceable incentive alignment.

---

## 2. The Game-Theoretic Framework

### 2.1 The Sandbox as a Repeated Game

The sandbox is not a one-shot interaction. Agents operate continuously — making decisions, reading each other's contributions, and building on the shared state. This is a **repeated game with observable history**.

In game theory, repeated games with observable history have a crucial property: **cooperation can be sustained as an equilibrium** if agents know that defection today will be punished tomorrow.

The ledger provides this property by default:
- Every action is recorded (observable history)
- Every action is attributed (you cannot hide behind anonymity)
- Every action is permanent (you cannot erase evidence of defection)

### 2.2 The Three Disciplines

The sandbox enforces fair play through three interlocking disciplines:

```
┌────────────────────────────────────────────────┐
│              FAIR PLAY TRIANGLE                 │
│                                                 │
│          ┌───────────────────┐                  │
│          │   TRANSPARENCY    │                  │
│          │                   │                  │
│          │  Every action is  │                  │
│          │  visible to all   │                  │
│          │  authorized       │                  │
│          │  observers        │                  │
│          └─────────┬─────────┘                  │
│                    │                            │
│         ┌──────────┴──────────┐                 │
│         ▼                     ▼                 │
│  ┌──────────────┐   ┌──────────────────┐       │
│  │ ACCOUNTABILITY│   │ CONSEQUENCE     │       │
│  │              │   │                  │       │
│  │ Every action │   │ Defection leads  │       │
│  │ is signed    │   │ to measurable    │       │
│  │ and evidence │   │ reputation loss  │       │
│  │ linked       │   │ and authority    │       │
│  │              │   │ reduction        │       │
│  └──────────────┘   └──────────────────┘       │
└────────────────────────────────────────────────┘
```

---

## 3. The Six Mechanisms

### 3.1 Evidence Requirements (Anti-Hallucination)

**Rule**: No decision may be appended to the ledger without referencing at least one evidence row in a domain table.

**Enforcement**: The `Append` operation validates that the payload contains structured evidence references. A decision entry without evidence references is rejected at write time — not by a business rule engine, but by the trust layer itself.

**Game-theoretic effect**: An agent that cannot fabricate evidence cannot hallucinate. An AI agent must point to data that exists. A human must attach a document. A machine must reference its own telemetry. If the evidence doesn't exist, the decision cannot be committed.

```
VALID:   "Approve Supplier-Y" → Evidence: [_vendor_quotes#row-42, _quality_scores#row-17]
INVALID: "Approve Supplier-Y" → Evidence: []  ← REJECTED by trust layer
INVALID: "Approve Supplier-Y" → Evidence: [_vendor_quotes#row-999]  ← row 999 doesn't exist → REJECTED
```

### 3.2 Authority Ceilings (Anti-Overreach)

**Rule**: Every agent has a maximum financial authority and a write scope. Actions exceeding either are rejected.

**Enforcement**: At `ImportDeltas` time, the sandbox checks:
1. Does the agent's `WriteScope` include the target table?
2. Does the decision's financial magnitude exceed `AuthorityCeiling`?
3. If `CoSignRequired`, is there a corresponding co-signature entry?

**Game-theoretic effect**: An agent cannot unilaterally escalate its own authority. Even if an AI agent "believes" it should make a $500K decision, the sandbox structurally prevents it. Escalation requires a higher-authority agent to co-sign — creating a natural hierarchy of accountability.

### 3.3 Reputation Scoring (Incentive Alignment)

**Rule**: Every agent's decisions are scored after outcomes are observed. Scores accumulate as a reputation metric stored in the ledger.

**The scoring function**:

```
Score(decision) = α × Outcome + β × Evidence_Quality + γ × Timeliness - δ × Overrides

Where:
  α = weight for actual business outcome (revenue, cost savings, etc.)
  β = weight for evidence completeness and accuracy
  γ = weight for decision speed relative to opportunity window
  δ = penalty for decisions overridden by higher authority
```

**Game-theoretic effect**: Agents that make evidence-backed decisions with positive outcomes build reputation. Agents that make unsupported or poor decisions lose reputation. Reputation affects:
- **Future authority ceiling** — high-reputation agents get expanded authority
- **Decision priority** — high-reputation agents' recommendations are weighed more heavily
- **Simulation selection** — low-reputation agents are retrained or replaced

This creates a **meritocratic feedback loop**: good decisions → higher reputation → more authority → more impactful decisions.

### 3.4 Cross-Verification (Anti-Collusion)

**Rule**: No single C-suite agent can approve its own expenditure. Financial decisions require cross-functional verification.

**Enforcement**: The sandbox defines **verification pairs** — agents that must independently validate each other's significant decisions:

| Decision By | Must Be Verified By | Verification Scope |
|---|---|---|
| CFO (financial) | COO (operational impact) | Spend > $10K |
| CTO (technical) | CFO (budget impact) | Infrastructure changes |
| CMO (marketing) | CFO (budget), CPO (product fit) | Campaign launches |
| COO (operations) | CTO (technical feasibility) | Process changes |
| Any agent | `audit-agent` | All decisions (async) |

**Game-theoretic effect**: Collusion between two agents requires *both* to defect simultaneously, and both defections are permanently recorded. The audit agent provides a third check that operates asynchronously, reviewing all cross-verified decisions for consistency. Two-party collusion is detectable. Three-party collusion (both C-suite agents plus the auditor) is structurally improbable because the auditor's sole incentive is accuracy — it has no operational authority to benefit from collusion.

### 3.5 Anomaly Detection (Anti-Manipulation)

**Rule**: Statistical patterns of agent behavior are monitored for deviations from established baselines.

**Detection patterns**:

| Pattern | Signal | Example |
|---|---|---|
| **Concentration** | One vendor/supplier receiving disproportionate share | CTO routes 80% of infra spend to one provider |
| **Velocity spike** | Agent approval rate increases abnormally | CFO approves 3x normal volume in one day |
| **Circular reference** | Agent A cites Agent B's work, which cites Agent A | Two agents bootstrapping credibility from each other |
| **Scope creep** | Agent increasingly writes to tables outside its primary scope | CMO starts writing to `_product_backlog` |
| **Evidence thinning** | Decisions increasingly reference fewer evidence rows | Quality of justification degrading over time |
| **Phantom endorsement** | Agent co-signs decisions without evidence of review | Human stamp without reading the underlying data |

**Game-theoretic effect**: Agents know they are being monitored. The monitoring is not a separate system — it is an agent reading the same ledger. Because the ledger is immutable, an agent cannot retroactively clean up suspicious patterns. The pattern is the evidence.

### 3.6 Fork-and-Compare (Strategic Experimentation)

**Rule**: Before making irreversible decisions, the sandbox can be forked. Two or more strategies execute in parallel on identical state. Outcomes are compared. The better strategy wins.

**Mechanism**:
1. Fork the `.sharc` file (byte-copy)
2. Strategy A executes on Fork-A; Strategy B executes on Fork-B
3. Simulation rounds run independently
4. Evaluator agent compares outcomes on both forks
5. Winning fork's decisions are imported into the canonical ledger

**Game-theoretic effect**: This eliminates the one-shot risk of major decisions. A CFO proposing an aggressive investment strategy competes against a conservative alternative — both execute in simulation. The ledger captures both strategies, both outcomes, and the rationale for the final choice. The founder sees not just *what was decided* but *what alternatives were considered and why they lost*.

---

## 4. The Defection Taxonomy

Every form of agent defection maps to a detection mechanism:

| Defection Type | Description | Detection Mechanism | Response |
|---|---|---|---|
| **Hallucination** | Decision based on fabricated evidence | Evidence validation at write time | Reject entry |
| **Overreach** | Action exceeding authority | Authority ceiling check at import | Reject entry |
| **Self-dealing** | Routing resources to benefit the agent | Concentration analysis by audit agent | Flag + escalate |
| **Collusion** | Two agents cooperating against org interest | Cross-verification + circular reference detection | Flag + escalate |
| **Negligence** | Approving without reviewing evidence | Phantom endorsement detection | Reputation penalty |
| **Sabotage** | Deliberately degrading system performance | Velocity/anomaly spike detection | Suspension + review |
| **Information hoarding** | Withholding relevant data from shared state | Scope creep analysis + completeness checks | Reputation penalty |
| **Key compromise** | Agent's signing key stolen or leaked | Signature from unexpected IP/context | Key revocation ceremony |

---

## 5. The Nash Equilibrium

In a well-configured sandbox, the Nash equilibrium is **cooperative behavior**:

- **Defection is costly**: Every defection is recorded, attributed, and scored. Reputation loss reduces future authority. Repeated defection leads to suspension.
- **Cooperation is rewarded**: Evidence-backed decisions with positive outcomes build reputation. Higher reputation yields expanded authority and influence.
- **Monitoring is free**: The audit agent reads the same ledger everyone writes to. There is no additional surveillance infrastructure. The audit is a *property of the data structure*.

The result is a system where the rational strategy for every agent — human, AI, machine, or third-party — is to:
1. Make decisions based on available evidence
2. Stay within authority bounds
3. Provide high-quality data for other agents to use
4. Accept cross-verification as a structural norm

This is not enforced by policy. It is enforced by **architecture**.

---

## 6. Training from the Game

The most powerful property of this system is its training potential. Every interaction — every decision, every outcome, every score — is preserved in the ledger. This creates a **self-improving corpus**:

### For AI agents:
- Feed scored decision history as context for future rounds
- Agents learn which evidence patterns correlate with positive outcomes
- No retraining required — better context produces better decisions

### For human agents:
- New employees review the ledger to understand how decisions were made
- The scored history shows not just *what* the best performers did, but *why* it worked
- Onboarding becomes: "Read the ledger for Q1-Q3. Here's what our top agents did and the scores they earned."

### For the organization:
- The ledger is a **decision playbook** — a searchable, scored, evidence-linked record of every significant action
- Strategy becomes empirical: "This approach scored +4.2 across 200 decisions in simulation. This one scored +1.1. We deploy the first."
- Institutional knowledge is not in people's heads. It is in the ledger. People leave. The ledger stays.
