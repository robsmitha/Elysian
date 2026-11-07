namespace Elysian.Application.Interfaces
{
    /// <summary>Encrypts Plaid access tokens before they're stored, and decrypts them for Plaid calls.</summary>
    public interface IAccessTokenProtector
    {
        /// <summary>False when no encryption key is configured</summary>
        bool CanProtect { get; }
        string Protect(string plaintext);
        string Unprotect(string protectedValue);
    }
}
