using Microsoft.AspNetCore.Mvc;

namespace BeServer.Services;

public static class ApiErrors
{
    public static IActionResult BadRequest(ControllerBase controller, string code, string message) =>
        controller.BadRequest(Envelope(controller, code, message));

    public static IActionResult NotFound(ControllerBase controller, string code, string message) =>
        controller.NotFound(Envelope(controller, code, message));

    public static object Envelope(ControllerBase controller, string code, string message) =>
        new
        {
            error = new
            {
                code,
                message,
            },
            correlationId = controller.HttpContext.TraceIdentifier,
        };
}
