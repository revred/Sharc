using System.Buffers.Binary;
using System.Text;
using Sharc;
using Sharc.Core.Trust;
using Sharc.Trust;
using Sharc.Crypto;

Console.WriteLine("Sharc Trust Layer - Advanced Capabilities Demo");
Console.WriteLine("--------------------------------------------");

// 1. Setup Database
// 1. Setup Database
string artifactsPath = Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/data");
if (!Directory.Exists(artifactsPath)) Directory.CreateDirectory(artifactsPath);
string dbPath = Path.Combine(artifactsPath, "trust_complex.db");

if (File.Exists(dbPath)) File.Delete(dbPath);
using var db = SharcDatabase.Create(dbPath);

var registry = new AgentRegistry(db);
var ledger = new LedgerManager(db);

// 2. Create Agents
Console.WriteLine("\n[1] Registering Agents...");

// CFO: High authority (1M ceiling), requires co-sig
using var cfoKey = new SharcSigner("admin-cfo");
var cfo = new AgentInfo(
    AgentId: "admin-cfo",
    Class: AgentClass.Root, // Was Human
    PublicKey: cfoKey.GetPublicKey(),
    AuthorityCeiling: 1_000_000,
    WriteScope: "*",
    ReadScope: "*",
    ValidityStart: Now(),
    ValidityEnd: Now() + 31536000,
    ParentAgent: "",
    CoSignRequired: true,
    Signature: cfoKey.Sign(AgentRegistry.GetVerificationBuffer(
        new AgentInfo("admin-cfo", AgentClass.Root, cfoKey.GetPublicKey(), 1_000_000, "*", "*", Now(), Now() + 31536000, "", true, [])))
);
registry.RegisterAgent(cfo);
Console.WriteLine("    Registered 'admin-cfo' (Ceiling: 1M, CoSign: Yes)");

// Bot: Low authority (100 ceiling), no co-sig
using var botKey = new SharcSigner("bot-01");
var bot = new AgentInfo(
    AgentId: "bot-01",
    Class: AgentClass.User, // Was Machine
    PublicKey: botKey.GetPublicKey(),
    AuthorityCeiling: 100,
    WriteScope: "logs",
    ReadScope: "logs",
    ValidityStart: Now(),
    ValidityEnd: Now() + 31536000,
    ParentAgent: "admin-cfo",
    CoSignRequired: false,
    Signature: botKey.Sign(AgentRegistry.GetVerificationBuffer(
        new AgentInfo("bot-01", AgentClass.User, botKey.GetPublicKey(), 100, "logs", "logs", Now(), Now() + 31536000, "admin-cfo", false, [])))
);
registry.RegisterAgent(bot);
Console.WriteLine("    Registered 'bot-01' (Ceiling: 100, CoSign: No)");

// Auditor: Verification only
using var auditorKey = new SharcSigner("auditor");
var auditor = new AgentInfo(
    AgentId: "auditor",
    Class: AgentClass.User, // Was Human
    PublicKey: auditorKey.GetPublicKey(),
    AuthorityCeiling: 0,
    WriteScope: "",
    ReadScope: "*",
    ValidityStart: Now(),
    ValidityEnd: Now() + 31536000,
    ParentAgent: "",
    CoSignRequired: false,
    Signature: auditorKey.Sign(AgentRegistry.GetVerificationBuffer(
        new AgentInfo("auditor", AgentClass.User, auditorKey.GetPublicKey(), 0, "", "*", Now(), Now() + 31536000, "", false, [])))
);
registry.RegisterAgent(auditor);
Console.WriteLine("    Registered 'auditor'");


// 3. Demonstrate Authority Limits
Console.WriteLine("\n[2] Testing Authority Limits...");

var smallTx = new TrustPayload(PayloadType.Financial, "Buy coffee", EconomicValue: 5);
ledger.Append(smallTx, botKey);
Console.WriteLine("    Bot spent $5 (Allowed)");

var bigTx = new TrustPayload(PayloadType.Financial, "Buy server", EconomicValue: 500);
try 
{
    ledger.Append(bigTx, botKey);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("    Bot spent $500 (FAILED: Should have blocked)");
}
catch (InvalidOperationException ex)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"    Bot blocked from spending $500: {ex.Message}");
}
Console.ResetColor();


// 4. Demonstrate Co-Signing
Console.WriteLine("\n[3] Testing Co-Signing...");

var strategicDecision = new TrustPayload(PayloadType.Approval, "Acquire Competitor", EconomicValue: 500_000);

// Try without co-sig
try 
{
    ledger.Append(strategicDecision, cfoKey);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("    CFO acted alone (FAILED: Should have blocked)");
}
catch (InvalidOperationException ex)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"    CFO blocked acting alone: {ex.Message}");
}
Console.ResetColor();

// Add co-signature from Auditor
Console.WriteLine("    ...Adding Auditor co-signature...");

// Reconstruct base bytes to sign
var baseBytesForSig = (strategicDecision with { CoSignatures = null }).ToBytes();
var baseHash = SharcHash.Compute(baseBytesForSig);
long ts = Now();

byte[] toSign = new byte[baseHash.Length + 8];
baseHash.CopyTo(toSign, 0);
BinaryPrimitives.WriteInt64BigEndian(toSign.AsSpan(baseHash.Length), ts);
byte[] sigBytes = auditorKey.Sign(toSign);

var signedPayload = strategicDecision with 
{ 
    CoSignatures = new List<CoSignature> { new CoSignature("auditor", sigBytes, ts) } // Fixed arg order
};

ledger.Append(signedPayload, cfoKey);
Console.WriteLine("    CFO action accepted with Auditor co-sig.");


// 5. Demonstrate Evidence Linking
Console.WriteLine("\n[4] Testing Evidence Linking...");

// Create a fake "row" hash
byte[] rowHash = SharcHash.Compute(Encoding.UTF8.GetBytes("Pre-meeting notes content"));
var evidence = new List<EvidenceRef> { new EvidenceRef("meetings", 101, rowHash) };

var attestedPayload = new TrustPayload(PayloadType.Text, "Meeting Minutes", Evidence: evidence);
ledger.Append(attestedPayload, botKey);
Console.WriteLine("    Bot appended minutes with evidence link.");


// 6. Verify Chain
Console.WriteLine("\n[5] Verifying Full Chain...");
bool valid = ledger.VerifyIntegrity();
Console.WriteLine($"    Chain Integrity: {(valid ? "VALID" : "INVALID")}");

long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
