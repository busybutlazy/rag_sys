using System.Security.Claims;

namespace BeServer.Services;

public class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
{
    public string UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            var id = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("JWT is missing user identity claim.");
            return id;
        }
    }
}
