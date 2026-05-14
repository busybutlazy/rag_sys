namespace BeServer.Auth;

public static class JwtConstants
{
    public const string Issuer = "rag-sys";
    public const string Audience = "rag-sys-frontend";
    public const int MinSecretLength = 32;
}
