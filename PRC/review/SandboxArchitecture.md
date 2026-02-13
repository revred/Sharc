# Sandbox Architecture: The Execution Ether

**The medium in which all agents — human, AI, machine, and third-party — exist, interact, and are held accountable**

---

## 1. Definition

The sandbox is a **single `.sharc` binary file** that contains:

1. The **identity** of every participant (agent registry)
2. The **history** of every decision (cryptographic ledger)
3. The **state** of the business (domain tables)
4. The **rules** of engagement (authority ceilings, evidence requirements, verification pairs)

Nothing exists outside the sandbox. Agents do not have network access, filesystem access, or API access. Their entire reality — the data they read, the decisions they record, the evidence they cite — is inside this file.

The sandbox is not a container in the Docker sense. It is not a VM. It is not a browser tab. It is an **execution ether** — a self-contained universe of data, trust, and accountability that persists as a single portable binary.

---

## 2. Internal Structure

```
┌──────────────────────────────────────────────────────────────────┐
│                         .sharc FILE                              │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                   SYSTEM LAYER                              │  │
│  │                                                             │  │
│  │  _sharc_agents     Identity, keys, authority, scope         │  │
│  │  _sharc_ledger     Hash-linked decision chain               │  │
│  │  _sharc_rules      Verification pairs, escalation paths     │  │
│  │  _sharc_scores     Reputation metrics per agent per period  │  │
│  │  _sharc_config     Sandbox parameters and thresholds        │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                   DOMAIN LAYER                              │  │
│  │                                                             │  │
│  │  _financial_ledger    Revenue, expenses, cash position      │  │
│  │  _vendor_contracts    Active supplier agreements             │  │
│  │  _product_backlog     Feature roadmap and priorities         │  │
│  │  _machine_telemetry   CNC, robot, sensor data               │  │
│  │  _employee_roster     Workforce data                        │  │
│  │  _campaign_ledger     Marketing spend and attribution       │  │
│  │  ... (any domain-specific tables)                           │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                   INGESTION LAYER                           │  │
│  │                                                             │  │
│  │  _vendor_inbound      Quarantined third-party submissions   │  │
│  │  _audit_inbound       External audit data                   │  │
│  │  _regulatory_inbound  Compliance updates                    │  │
│  │  _telemetry_inbound   Raw machine data before validation    │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                   ENCRYPTION LAYER                          │  │
│  │                                                             │  │
│  │  AES-256-GCM page-level encryption                          │  │
│  │  Row-level entitlement (per-tag decryption)                 │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

### System Layer
The infrastructure of trust. These tables are managed by the sandbox itself, not by application code. Agents read them to understand the rules. Only the founder (or a designated governance agent) can modify `_sharc_rules` and `_sharc_config`.

### Domain Layer
The operational state of the business. These tables are populated by agents — AI models write analysis results, humans write approvals, machines write telemetry, third parties write quotes. Every write is a signed ledger entry.

### Ingestion Layer
A quarantine zone for external data. Third-party contributions land here first. An internal agent must validate and promote inbound data to the domain layer. This prevents external actors from directly modifying operational state.

### Encryption Layer
Page-level encryption (AES-256-GCM) protects data at rest. Row-level entitlement ensures that agents see only the data their entitlement tags authorize. The CFO sees financial rows. The CTO sees infrastructure rows. The CNC machine sees only its own telemetry table.

---

## 3. How Agents Connect to the Sandbox

Agents do not "connect" in the network sense. They **open the file, read state, write decisions, and close the file.** The interaction model varies by agent class:

### 3.1 AI Agents (In-Process)

AI agents run as processes in the same runtime as the sandbox. In a .NET host:

```csharp
// AI agent reads state, reasons, and writes a decision
using var db = SharcDatabase.OpenMemory(sandboxData, new SharcOpenOptions { Writable = true });
var ledger = new LedgerManager(db);
var registry = new AgentRegistry(db);

// Read the current financial state
using var reader = db.CreateReader("_financial_ledger");
var financials = AnalyzeFinancials(reader);  // AI reasoning

// Make an evidence-backed decision
ledger.Append(
    decision: "APPROVED: Q3 marketing budget of $120K",
    evidence: new[] { "_financial_ledger#row-312", "_campaign_ledger#row-88" },
    signer: cfoAgent
);
```

The AI agent's entire context is the sandbox. It cannot call external APIs. It cannot fetch web pages. It reasons over what exists in the file.

### 3.2 Human Agents (Tool-Mediated)

Humans interact through a dashboard or application that reads the sandbox and presents it as a UI. The human's signing key is held by the tool (backed by hardware if available).

```
┌─────────────────────────────────────┐
│          FOUNDER DASHBOARD           │
│                                      │
│  Pending Decisions (3)               │
│  ┌────────────────────────────────┐  │
│  │ CFO requests $500K CapEx       │  │
│  │ Evidence: [fin#312, tech#88]   │  │
│  │ CFO Score: +4.2 (high)         │  │
│  │ [✓ APPROVE]  [✗ REJECT]       │  │
│  └────────────────────────────────┘  │
│                                      │
│  Latest Ledger Entries (50)          │
│  Agent Reputation Board              │
│  Anomaly Alerts (1 new)              │
└─────────────────────────────────────┘
```

When the human clicks "APPROVE," the tool signs the entry with the founder's key and appends it to the ledger. The human's decision has the same structure as an AI's decision — signed, evidence-linked, hash-chained.

### 3.3 Machine Agents (Firmware-Mediated)

Physical assets run firmware that reads sensors, encodes readings into Sharc records, and signs them with the machine's embedded key. Telemetry is written to the ingestion layer.

```
CNC-Lathe-07 → _telemetry_inbound:
  Cycle Time: 42.3s
  Tool Wear: 78%
  Vibration: 0.12mm/s (nominal)
  Timestamp: 2026-02-14T14:22:31Z
  Signed: [CNC-Lathe-07 key]
```

An AI agent (`coo-quality-inspector`) reads the telemetry, validates it against historical baselines, and promotes valid readings to `_machine_telemetry`. Invalid readings (e.g., vibration spikes suggesting sensor malfunction) are flagged for human review.

### 3.4 Third-Party Agents (Scoped Access)

External participants receive a sandbox *fragment* — a view containing only the ingestion tables they have write access to, plus enough domain data for context. They write to their ingestion table. Their fragment is synced back to the main sandbox via `ImportDeltas`.

```
Supplier-Acme receives:
  _vendor_inbound (write)
  _vendor_contracts (read, filtered to their own contracts)
  _product_specs (read, current quarter only)

Supplier-Acme writes:
  Quote for raw material X: $4.20/unit, MOQ 10K, lead time 6 weeks
  Signed: [Acme supplier key]
  → Quarantined in _vendor_inbound
  → coo-procurement validates, promotes to _vendor_contracts
```

---

## 4. Sandbox Lifecycle

```
PHASE 1          PHASE 2          PHASE 3          PHASE 4
PROVISION        SIMULATE         HARDEN           GOVERN
    │                │                │                │
    ▼                ▼                ▼                ▼
┌────────┐   ┌────────────┐   ┌────────────┐   ┌────────────┐
│ Create │   │ Run agent  │   │ Review     │   │ Connect    │
│ empty  │   │ decision   │   │ scored     │   │ live data  │
│ sandbox│   │ rounds on  │   │ history    │   │ feeds      │
│        │   │ seed data  │   │            │   │            │
│ Regist-│   │            │   │ Adjust     │   │ Agents     │
│ er 30  │   │ Score      │   │ authority  │   │ operate on │
│ agents │   │ outcomes   │   │ ceilings   │   │ real state │
│        │   │            │   │            │   │            │
│ Seed   │   │ Identify   │   │ Tune       │   │ Ledger is  │
│ domain │   │ weak agents│   │ detection  │   │ permanent  │
│ tables │   │            │   │ thresholds │   │ record     │
└────────┘   └────────────┘   └────────────┘   └────────────┘
```

### Phase 1: Provision
- Create the `.sharc` file
- Register all 30 agents with keys, authority, and scope
- Define verification pairs and escalation paths
- Seed domain tables with real or synthetic operational data

### Phase 2: Simulate
- Agents make decisions on seed data in memory (`OpenMemory`)
- Decision rounds execute: each C-suite agent reads state, reasons, writes decisions
- Specialist agents provide supporting analysis
- Machine agents inject telemetry (synthetic or replayed from real sensors)
- The evaluator agent scores decisions against simulated outcomes
- Multiple simulation runs produce scored decision histories

### Phase 3: Harden
- The founder reviews scored histories across simulation runs
- Low-performing agents are re-configured (adjusted prompts, tighter authority)
- Authority ceilings are calibrated based on observed decision quality
- Anomaly detection thresholds are tuned (what concentration level triggers an alert?)
- Verification pairs are adjusted based on observed decision flow
- The sandbox configuration is frozen and signed by the founder

### Phase 4: Govern
- The sandbox is connected to live data inflows
- Domain tables receive real ERP data (financial feeds, inventory counts, sensor telemetry)
- Agents operate on real state with real consequences
- The ledger accumulates the permanent record of all decisions
- The founder monitors via the dashboard
- Anomaly alerts trigger human review
- Periodic re-simulation validates that agent strategies remain effective

---

## 5. Deployment Topologies

The sandbox is a file. It runs wherever a file can be read.

### 5.1 Single-Host (Startup / SME)

```
┌──────────────────────────────────────┐
│           SINGLE SERVER              │
│                                      │
│   .sharc file on disk                │
│   30 agents as .NET background tasks │
│   Web dashboard for founder          │
│   ERP data via ETL or API adapter    │
└──────────────────────────────────────┘
```

One server. One file. All agents run as concurrent tasks in a single process. Data flows in via scheduled ETL jobs or real-time API adapters that write to ingestion tables. The founder accesses a web dashboard that reads the sandbox.

**Cost**: One VM. No databases. No message queues. No Kubernetes.

### 5.2 Browser-Native (Solo Operator / Consultant)

```
┌──────────────────────────────────────┐
│           BROWSER TAB                │
│                                      │
│   .sharc file as byte[] in WASM      │
│   Agents run as Blazor components    │
│   Dashboard is the app itself        │
│   Data entered manually or imported  │
└──────────────────────────────────────┘
```

The entire sandbox runs in a browser tab. No server. No backend. The consultant opens the app, loads (or creates) a `.sharc` file, and agents operate on the data. Decisions are made, scored, and stored — all in the browser.

**Cost**: Zero. A laptop and a browser.

### 5.3 Edge-Distributed (Manufacturing / Logistics)

```
┌────────────┐   ┌────────────┐   ┌────────────┐
│  FACTORY   │   │  WAREHOUSE │   │  HQ        │
│  FLOOR     │   │            │   │            │
│            │   │ .sharc     │   │ .sharc     │
│ .sharc     │   │ (logistics)│   │ (master)   │
│ (machines) │   │            │   │            │
│            │   │ Truck,     │   │ C-suite    │
│ CNC, Robot │   │ Inventory  │   │ agents +   │
│ agents     │   │ agents     │   │ Founder    │
└─────┬──────┘   └─────┬──────┘   └─────┬──────┘
      │                │                │
      └────────────────┴────────────────┘
                  Delta Sync
           (ExportDeltas / ImportDeltas)
```

Multiple sandbox instances at different locations. Each operates autonomously with its own agents. Periodically, they sync via delta export/import. The HQ master sandbox receives deltas from factory and warehouse, verifies signatures, and integrates into the unified ledger.

**Cost**: One lightweight compute per location. No central database. Sync over any transport (USB drive, email attachment, secure file transfer, or network).

### 5.4 Hybrid Cloud (Enterprise)

```
┌──────────────────────────────────────────────┐
│              CLOUD (Azure / AWS)              │
│                                              │
│   .sharc file in blob storage                │
│   AI agents as serverless functions          │
│   Triggered by ERP events                    │
│   Dashboard as web app                       │
│                                              │
│   ┌─────────────┐  ┌────────────────────┐    │
│   │ Factory Edge │  │ Supplier Portal    │    │
│   │ (delta sync) │  │ (scoped fragment)  │    │
│   └─────────────┘  └────────────────────┘    │
└──────────────────────────────────────────────┘
```

The sandbox lives in cloud blob storage. AI agents run as serverless functions triggered by events (new invoice, sensor alert, supplier quote). Human agents access via a web dashboard. Edge locations and third parties sync via deltas.

**Cost**: Pay-per-invocation. No always-on servers for agent execution.

---

## 6. What Makes This Different from Palantir AIP

Palantir AIP is powerful. It connects AI to an enterprise ontology with human-in-the-loop governance. But it differs from the Sharc sandbox in fundamental ways:

| Dimension | Palantir AIP | Sharc Sandbox |
|---|---|---|
| **Infrastructure** | Cloud-hosted SaaS platform | Single binary file, runs anywhere |
| **Trust model** | Platform-mediated access control | Cryptographic signatures at the data layer |
| **Verification** | Server-side audit logs | Hash-linked chain verifiable by anyone holding the file |
| **Cost** | Enterprise licensing ($M/year) | Zero — the file is the infrastructure |
| **Offline capability** | Requires cloud connectivity | Fully offline, sync via delta |
| **Data sovereignty** | Data lives on Palantir's infrastructure | Data lives wherever you put the file |
| **Agent accountability** | Platform-level logging | Per-entry cryptographic attribution |
| **Third-party integration** | API-based, platform-managed | Scoped sandbox fragments, self-contained |

Palantir solves the ontology problem — connecting AI to structured enterprise data. Sharc solves the **trust problem** — ensuring that every contribution to that structured data is authentic, authorized, and permanently accountable.

They are complementary, not competitive. A Palantir deployment could use Sharc files as the trust-verified data layer beneath its ontology.

---

## 7. The Core Insight

The sandbox works because it collapses three traditionally separate concerns into one:

1. **Database** — the `.sharc` file stores structured data in B-trees
2. **Ledger** — every write is hash-linked and signed
3. **Access control** — authority and scope are embedded in the agent registry

In traditional architectures, these are three separate systems: PostgreSQL + audit log + IAM. Three systems means three points of failure, three consistency boundaries, and three attack surfaces.

In the sandbox, they are one file. Consistency is guaranteed by the transaction layer. Trust is guaranteed by the hash chain. Access control is guaranteed by the agent registry. There is one system, one file, one source of truth.

> The sandbox is not an application. It is the **substrate** on which applications are built. The same substrate hosts a startup's first AI experiment and an enterprise's full operational governance.
