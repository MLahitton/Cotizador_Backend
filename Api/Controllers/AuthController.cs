using Application.Authentication.GetCurrentUser;
using Application.Authentication.GoogleSignIn;
using Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    GoogleSignInService googleSignInService,
    GetCurrentUserService getCurrentUserService)
    : ControllerBase
{
    [HttpPost("google")]
    [AllowAnonymous]
    [ProducesResponseType<GoogleSignInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GoogleSignInResponse>> GoogleSignIn(
        [FromBody] GoogleSignInRequest request,
        CancellationToken cancellationToken)
    {
        var result = await googleSignInService.ExecuteAsync(
            new GoogleSignInCommand(request.IdToken ?? string.Empty),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapGoogleSignInFailure(result.Failure);
        }

        var user = result.User!;

        return Ok(new GoogleSignInResponse(
            result.AccessToken!,
            "Bearer",
            result.ExpiresAtUtc!.Value,
            result.IsNewUser,
            new AuthenticatedUserResponse(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.ProfilePictureUrl,
                user.IsActive)));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AuthenticatedUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthenticatedUserResponse>> GetCurrentUser(
        CancellationToken cancellationToken)
    {
        var result = await getCurrentUserService.ExecuteAsync(
            cancellationToken);

        if (result.Failure == GetCurrentUserFailure.Unauthorized)
        {
            return AuthenticationProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado.");
        }

        if (result.Failure == GetCurrentUserFailure.InactiveUser)
        {
            return AuthenticationProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso a la aplicacion.");
        }

        return Ok(new AuthenticatedUserResponse(
            result.Id!.Value,
            result.Email!,
            result.FirstName!,
            result.LastName,
            result.ProfilePictureUrl,
            result.IsActive));
    }

    private ActionResult<GoogleSignInResponse> MapGoogleSignInFailure(
        GoogleSignInFailure failure)
    {
        return failure switch
        {
            GoogleSignInFailure.InvalidRequest => AuthenticationProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud invalida",
                "El idToken es obligatorio."),
            GoogleSignInFailure.InvalidGoogleToken => AuthenticationProblem(
                StatusCodes.Status401Unauthorized,
                "Credencial invalida",
                "No fue posible validar la credencial de Google."),
            GoogleSignInFailure.InactiveUser => AuthenticationProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso a la aplicacion."),
            GoogleSignInFailure.IdentityConflict => AuthenticationProblem(
                StatusCodes.Status409Conflict,
                "Conflicto de identidad",
                "No fue posible completar el inicio de sesion. Intente nuevamente."),
            GoogleSignInFailure.ProviderUnavailable => AuthenticationProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Proveedor no disponible",
                "No fue posible validar la credencial en este momento."),
            _ => AuthenticationProblem(
                StatusCodes.Status500InternalServerError,
                "Error de autenticacion",
                "No fue posible completar el inicio de sesion.")
        };
    }

    private ObjectResult AuthenticationProblem(
        int statusCode,
        string title,
        string detail)
    {
        return Problem(
            statusCode: statusCode,
            title: title,
            detail: detail);
    }
}
