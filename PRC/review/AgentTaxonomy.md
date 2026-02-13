# Agent Taxonomy: Participants in the Trust Sandbox

**Every participant in the sandbox is an agent. Not all agents are AI.**

---

## 1. The Principle

The sandbox does not distinguish between a human making a decision, an AI model inferring a recommendation, a CNC machine reporting its status, or a third-party service submitting a quote. They are all **agents** — entities that read state, contribute information, and sign their contributions.

This unification is fundamental. The moment you separate "AI decisions" from "human decisions" or "machine telemetry" from "analyst reports," you create trust boundaries that become attack surfaces. In the Sharc sandbox, there is one trust model. Every contribution is signed. Every contribution is evidence-linkable. Every contribution is auditable.

The question is never *"Is this participant a human or a machine?"* The question is *"Is this contribution authentic, authorized, and evidence-backed?"*

---

## 2. The Five Agent Classes

```
┌─────────────────────────────────────────────────────────┐
│                 AGENT TAXONOMY                           │
│                                                          │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │   HUMAN     │  │   AI MODEL   │  │   MACHINE     │  │
│  │             │  │              │  │               │  │
│  │ Executives  │  │ LLM-based    │  │ CNC, Robots   │  │
│  │ Operators   │  │ Domain-tuned │  │ IoT Sensors   │  │
│  │ Analysts    │  │ Evaluators   │  │ Assembly Lines│  │
│  └─────────────┘  └──────────────┘  └───────────────┘  │
│                                                          │
│  ┌──────────────────┐  ┌────────────────────────────┐   │
│  │   THIRD-PARTY    │  │   COMPOSITE              │   │
│  │                  │  │                            │   │
│  │ Vendors          │  │ Human with AI assistant    │   │
│  │ Auditors         │  │ Machine with AI controller │   │
│  │ Regulators       │  │ Team acting as one agent   │   │
│  │ Supply chain     │  │                            │   │
│  └──────────────────┘  └────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### 2.1 Human Agents

A human participates in the sandbox through a software tool — a dashboard, a mobile app, a CLI. The tool holds the human's signing key. When the human approves a purchase order, the tool signs the ledger entry with the human's key.

**Key property**: Human agents are slow but high-authority. They make fewer decisions but those decisions carry more weight. A CFO human approving a $5M acquisition is one ledger entry — but it is the most important entry in the chain.

| Example | Role | Typical Authority |
|---|---|---|
| Founder | Strategic oversight, veto power | Unlimited |
| Finance Director | Budget approval, audit review | $500K |
| Factory Floor Lead | Shift scheduling, incident response | $10K |
| Sales Representative | Quote submission, contract negotiation | $50K |

**Trust mechanism**: Human agents hold hardware-backed keys (e.g., FIDO2 security keys, HSMs). Their signing key never leaves the hardware device. The sandbox verifies the signature, not the human's identity — the key *is* the identity.

### 2.2 AI Agents

AI agents are LLM-powered processes that read the sandbox state, reason over it, and write decisions or recommendations. They operate autonomously within their authority scope.

**Key property**: AI agents are fast but bounded. They can process thousands of ledger entries in seconds, identify patterns, and propose actions — but they cannot exceed their authority ceiling. A CFO AI agent can approve $50K but not $500K.

| Example | Role | Typical Authority |
|---|---|---|
| `cfo-prime` | Financial analysis, budget allocation | $50K |
| `cto-prime` | Architecture decisions, vendor evaluation | $25K |
| `cmo-campaign-optimizer` | Ad spend allocation, A/B test design | $5K |
| `coo-quality-inspector` | Defect rate analysis, supplier scoring | Advisory only |

**Trust mechanism**: AI agents hold software-backed keys generated at provisioning time. Keys are stored in the sandbox file itself (`_sharc_agents`). The key is rotated on each simulation-to-production transition.

### 2.3 Machine Agents

Physical assets — CNC machines, robotic arms, IoT sensors, conveyor systems — participate as agents. They don't "decide" in the human sense. They **report**. A CNC machine reports its cycle time, tool wear, defect rate, and maintenance status. These reports are signed ledger entries.

**Key property**: Machine agents are prolific but low-authority. A factory floor with 50 CNC machines might produce 10,000 telemetry entries per hour. Each is signed, each is evidence for decisions made by AI or human agents above.

| Example | Role | Data Contribution |
|---|---|---|
| CNC-Lathe-07 | Turning operations | Cycle time, tool wear, vibration data |
| Assembly-Robot-03 | Component placement | Placement accuracy, throughput, error rate |
| Temp-Sensor-Warehouse-B | Environment monitoring | Temperature, humidity, timestamps |
| Delivery-Truck-14 | Logistics | GPS position, fuel level, delivery status |

**Trust mechanism**: Machine agents hold embedded keys provisioned during commissioning. The key is burned into firmware or a TPM (Trusted Platform Module). The signature proves the data came from a specific physical asset, not from a spoofed software process.

### 2.4 Third-Party Agents

Vendors, auditors, regulators, and supply chain partners participate through controlled entry points. A vendor submits a quote. An auditor submits a compliance assessment. A regulator submits a policy update. Each is a signed ledger entry.

**Key property**: Third-party agents are external but accountable. They cannot write to arbitrary tables. Their contributions are quarantined in dedicated ingestion tables and must be accepted by an internal agent before entering the main state.

| Example | Role | Entry Point |
|---|---|---|
| Supplier-Acme | Raw material quotes | `_vendor_inbound` |
| Deloitte-Audit | Compliance assessment | `_audit_inbound` |
| FDA-RegUpdate | Regulatory requirement | `_regulatory_inbound` |
| AWS-BillingFeed | Infrastructure cost data | `_infra_inbound` |

**Trust mechanism**: Third-party keys are registered with explicit scope constraints and validity windows. A supplier's key is valid only for writing to `_vendor_inbound` and only for the duration of the contract. Expiration is enforced by the sandbox — `ValidityEnd` in `_sharc_agents`.

### 2.5 Composite Agents

The most important class. In practice, most agents are composites:

- **A human with an AI assistant**: The human reviews AI-generated analysis and co-signs the decision. The ledger entry carries *both* signatures — the AI that produced the analysis and the human who approved it. Dual-signed entries carry higher trust weight.

- **A machine with an AI controller**: A CNC machine produces telemetry. An AI controller interprets the telemetry and adjusts parameters. Both the raw data and the adjustment decision are separate signed entries — the machine signs the data, the AI signs the interpretation.

- **A team acting as one agent**: Five junior analysts share a team key. Their collective contributions appear as a single agent. The team lead holds the signing key and is accountable for the team's output.

**Trust mechanism**: Composite agents use multi-signature or co-signature schemes. A decision requiring both AI and human approval has two signatures in the ledger entry. Verification requires both to be valid.

---

## 3. Registry Structure

Every agent — regardless of class — is registered in `_sharc_agents` with the same schema:

| Field | Description | Example |
|---|---|---|
| `AgentId` | Unique identifier | `cfo-prime`, `cnc-lathe-07`, `acme-supplier` |
| `AgentClass` | Taxonomy class | `human`, `ai`, `machine`, `third-party`, `composite` |
| `PublicKey` | Cryptographic identity | ECDsa P-256 SubjectPublicKeyInfo |
| `AuthorityCeiling` | Maximum financial authority | `50000` (in base currency units) |
| `WriteScope` | Tables this agent can write to | `_financial_ledger,_approvals` |
| `ReadScope` | Tables this agent can read | `*` or explicit list |
| `ValidityStart` | Key activation timestamp | Unix milliseconds |
| `ValidityEnd` | Key expiration timestamp | Unix milliseconds (0 = no expiry) |
| `ParentAgent` | Reporting hierarchy | `cfo-prime` reports to `founder` |
| `CoSignRequired` | Whether decisions require co-signature | `true` for high-authority AI |
| `Signature` | Self-signed registration proof | ECDsa signature over all fields |

---

## 4. The Trust Implication

Because all five classes share the same cryptographic infrastructure:

1. **A CNC machine's telemetry is as verifiable as a CFO's approval** — same hash chain, same signature scheme, same audit trail
2. **A supplier's quote is as traceable as an AI's recommendation** — agent registry resolves both, provenance is identical
3. **A human's decision and an AI's decision are structurally indistinguishable** — authority and evidence matter, not the class of the agent
4. **Tampering by any agent class is detected identically** — the ledger doesn't care if the tamperer was human or machine

This is the fundamental shift from current systems: trust is not about *what kind of entity* made a decision. Trust is about whether the decision is **signed, authorized, evidence-backed, and hash-linked to the chain**.
