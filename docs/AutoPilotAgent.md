# AutoPilotAgent

The `AutoPilotAgent` (Master Agent) serves as the primary coordinator, central brain, and consensus node within the Sharc Flight Simulator. Its primary responsibility is to consume and verify telemetry data published by various `WorkerAgent` (Sub-Agent) sensors to the Sharc Trust Ledger, and subsequently apply consensus algorithms to determine the true, sanitized state of the aircraft.

## Core Concepts & Agent Topology

### 1. Sub-Agents (Worker Agents / Sensors)
Sub-agents represent individual hardware sensors (e.g., Altimeters, Pitot tubes, Gyroscopes) or localized software routines. 
*   **Vulnerability:** Sensors can physically malfunction, degrade over time, or be compromised. Similarly, the sub-agents reporting their data can suffer from transient software faults or network delays.
*   **Role:** Each sensor continuously observes the environment and pushes its readings to the Sharc Trust Ledger as a signed `TrustPayload`.

### 2. Master Agent (Auto-Pilot / Central Brain)
The Master Agent does *not* directly measure the environment. Instead, it relies entirely on the aggregation of data from the Trust Ledger.
*   **The Trust Challenge:** Because sensors and sub-agents can be faulty—either maliciously (Byzantine) or due to natural wear—the Master Agent cannot blindly trust any single data source.
*   **Situational Awareness:** It is the job of the Master Agent to allocate trust. By cross-checking correlated sensors (e.g., comparing GPS altitude with barometric altitude) against historical reliability scores, the Master Agent arrives at a cohesive "Situational Awareness" of the aircraft's true state.

## Modes of Operation

The system is designed to support different levels of autonomy depending on the operational context:

1.  **Suggestive Mode (Human-in-the-Loop):** In this mode, the Master Agent analyzes the ledger, detects anomalies, and calculates the safest course of action. It then *presents* these findings to the human pilot via the EICAS (Engine Indicating and Crew Alerting System) and Primary Flight Display (PFD) as warnings or flight director cues. The human pilot retains ultimate authority to accept or override the suggestions.
2.  **Auto-Pilot Mode (Fully Autonomous):** In critical situations (or when engaged by the crew), the Master Agent takes direct control of the flight surfaces. It uses its consensus-derived "True Data" to bypass faulty sensors automatically, isolating bad actors and keeping the aircraft flying smoothly without human intervention.

## Core Responsibilities

1. **Ledger Ingestion**: Continuously listens to the `LedgerManager` for incoming `TrustPayload` entries representing new sensor telemetry.
2. **Cryptographic Validation**: Relies on the underlying Sharc Trust Layer to ensure that the cryptographic signatures on all payloads are perfectly valid and have not been tampered with.
3. **Authority & Reputation Enforcement**: Consults the `AgentRegistry` to verify that the emitting sensor agent possesses the appropriate authority rights and maintains an adequate reputation score before considering its data.
4. **Byzantine Fault Tolerance & Consensus**: Implements quorum-based consensus logic to filter out noise and detect anomalies. When one sensor reports a critical dive but three others report level flight, the Master Agent dynamically slashes the trust score of the outlier and discards its data.
5. **State Management**: Computes and maintains the "True" aircraft state—including Altitude, Speed, Pitch, and Roll—which is then rendered securely on the PFD.

## Data Flow Architecture

1. **Generation**: `WorkerAgent` instances simulate sensor inputs, wrap them in JSON, and sign them to produce a `TrustPayload`.
2. **Commitment**: The payload is appended to the `LedgerManager` where Sharc executes signature and hash-chain validation.
3. **Consensus**: The `AutoPilotAgent` processes the committed telemetry. If the agent's reputation is poor or the data deviates heavily from consensus, it is rejected; otherwise, the aircraft's state is updated.
