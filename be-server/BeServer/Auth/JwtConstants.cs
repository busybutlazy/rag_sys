namespace BeServer.Auth;

internal static class JwtConstants
{
    internal const string Issuer = "rag-sys";
    internal const string Audience = "rag-sys-frontend";
    internal const int MinSecretLength = 32;
}
