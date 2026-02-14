using Sharc;
using Sharc.Exceptions;

Console.WriteLine("Sharc Encrypted Read Sample");
Console.WriteLine("---------------------------");
Console.WriteLine("NOTE: This sample requires an encrypted .db file.");
Console.WriteLine("Create one using SharcWriter with encryption options.\n");

// Configure decryption options
var options = new SharcOpenOptions
{
    Encryption = new SharcEncryptionOptions { Password = "secure-test-password" }
};

var dbPath = "encrypted.db";
if (!File.Exists(dbPath))
{
    Console.WriteLine($"File '{dbPath}' not found. Skipping demo.");
    Console.WriteLine("To create an encrypted database, see docs/COOKBOOK.md.");
    return;
}

try
{
    using var db = SharcDatabase.Open(dbPath, options);
    using var reader = db.CreateReader("secrets");

    while (reader.Read())
    {
        Console.WriteLine($"Secret: {reader.GetString(1)}");
    }
}
catch (SharcCryptoException ex)
{
    Console.WriteLine($"Decryption failed: {ex.Message}");
}
