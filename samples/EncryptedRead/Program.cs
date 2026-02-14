using Sharc;
using Sharc.Crypto;

Console.WriteLine("Sharc Encrypted Read Sample");
Console.WriteLine("---------------------------");

// Configure decryption options
var options = new SharcOpenOptions 
{ 
    Password = "secure-test-password" 
};

try 
{
    using var db = SharcDatabase.Open("encrypted.db", options);
    using var reader = db.CreateReader("secrets");
    
    while (reader.Read())
    {
        Console.WriteLine($"Secret: {reader.GetString("payload")}");
    }
}
catch (SharcCryptoException ex)
{
    Console.WriteLine($"Error: Decryption failed. {ex.Message}");
}
