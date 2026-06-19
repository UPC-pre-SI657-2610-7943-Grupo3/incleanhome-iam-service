namespace InCleanHome.IamService.Domain.Services.External;

/// <summary>
///     Domain port for an external identity provider (SSO).
/// </summary>
/// <remarks>
///     <para>
///     This contract encapsulates only what the platform needs from an IdP:
///     <i>verify a token and obtain the user's data</i>. It does NOT know about
///     JWKS, OIDC, /userinfo, or any Auth0-specific detail.
///     </para>
///     <para>
///     Today the only implementation is <c>Auth0IdentityProviderAdapter</c>. To
///     switch to Cognito/Okta/Keycloak, write another adapter that implements
///     this interface and register it in DI — the rest of the project (controllers,
///     command services) does not change.
///     </para>
/// </remarks>
public interface IIdentityProvider
{
    bool IsEnabled { get; }
    Task<IdentityProviderUserInfo?> ValidateAndGetUserInfoAsync(string accessToken);
}

public record IdentityProviderUserInfo(string Subject, string Email, string Name, string? PictureUrl);
