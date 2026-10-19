using Application.PreQuotes.ProcessRequirement;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Mvc;

namespace Api.ErrorHandling;

internal static class ProcessRequirementProblemMapper
{
    public static ObjectResult Create(HttpContext context, ProcessRequirementFailure failure)
    {
        ObjectResult RequirementProblem(int status, string code, string title, string detail) =>
            ApiProblemDetailsFactory.Create(context, status, code, title, detail);

        return failure switch
        {
            ProcessRequirementFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El requerimiento indicado no es valido."),
            ProcessRequirementFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            ProcessRequirementFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            ProcessRequirementFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            ProcessRequirementFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            ProcessRequirementFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            ProcessRequirementFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden procesar requerimientos en un proyecto inactivo."),
            ProcessRequirementFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            ProcessRequirementFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden procesar requerimientos para un cliente inactivo."),
            ProcessRequirementFailure.AlreadyProcessing =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProcessingAlreadyActive,
                    "Requerimiento en procesamiento",
                    "El requerimiento ya tiene un procesamiento activo."),
            ProcessRequirementFailure.NoFiles =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.NoFiles,
                    "Requerimiento sin archivos",
                    "El requerimiento no tiene archivos asociados para procesar."),
            ProcessRequirementFailure.StorageError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.StorageError,
                    "Error de almacenamiento",
                    "No fue posible leer los archivos del requerimiento."),
            ProcessRequirementFailure.AiTimeout =>
                RequirementProblem(
                    StatusCodes.Status504GatewayTimeout,
                    RequirementErrorCodes.AiTimeout,
                    "Tiempo agotado en AI2",
                    "Cotizador_AI2 no respondio dentro del tiempo esperado."),
            ProcessRequirementFailure.AiServiceUnavailable =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiServiceUnavailable,
                    "Servicio de extraccion no disponible",
                    "No fue posible contactar Cotizador_AI2."),
            ProcessRequirementFailure.AiRemoteRejected =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiRemoteRejected,
                    "Solicitud rechazada por AI2",
                    "Cotizador_AI2 rechazo los archivos del requerimiento."),
            ProcessRequirementFailure.AiInvalidResponse =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiInvalidResponse,
                    "Extraccion invalida",
                    "Cotizador_AI2 respondio con una extraccion no valida."),
            ProcessRequirementFailure.AiServiceError =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiServiceError,
                    "Error de AI2",
                    "Cotizador_AI2 no pudo completar la extraccion."),
            ProcessRequirementFailure.PersistenceError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de persistencia",
                    "No fue posible registrar el procesamiento del requerimiento."),
            ProcessRequirementFailure.Cancelled =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProcessingCancelled,
                    "Procesamiento cancelado",
                    "El procesamiento del requerimiento fue cancelado."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible registrar el procesamiento del requerimiento.")
        };
    }

    public static ProcessRequirementFailure FromPersistedError(string? code) => code switch
    {
        "AI2_TIMEOUT" => ProcessRequirementFailure.AiTimeout,
        "AI2_SERVICE_UNAVAILABLE" => ProcessRequirementFailure.AiServiceUnavailable,
        "AI2_REQUEST_REJECTED" => ProcessRequirementFailure.AiRemoteRejected,
        "AI_INVALID_RESPONSE" => ProcessRequirementFailure.AiInvalidResponse,
        "AI2_SERVICE_ERROR" => ProcessRequirementFailure.AiServiceError,
        "REQUIREMENT_STORAGE_ERROR" => ProcessRequirementFailure.StorageError,
        "REQUIREMENT_PROCESSING_CANCELLED" => ProcessRequirementFailure.Cancelled,
        _ => ProcessRequirementFailure.PersistenceError
    };
}
