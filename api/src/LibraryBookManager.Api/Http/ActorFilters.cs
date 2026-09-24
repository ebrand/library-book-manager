using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Identity;

namespace LibraryBookManager.Api.Http;

public static class ActorFilters
{
    private const string ItemKey = "lbm.actor";

    /// <summary>Resolves the caller once per request, before routing to an endpoint.</summary>
    public static IApplicationBuilder UseActorResolution(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var resolver = context.RequestServices.GetRequiredService<IActorResolver>();
            context.Items[ItemKey] = await resolver.ResolveAsync(context);
            await next();
        });

    public static Actor CurrentActor(this HttpContext context) =>
        (context.Items[ItemKey] as ActorResolution)?.Actor
        ?? throw new InvalidOperationException("Endpoint is missing RequireActor().");

    /// <summary>
    /// Refuses the request unless it carries an accepted credential and, when roles are
    /// given, the caller holds one of them.
    /// </summary>
    public static TBuilder RequireActor<TBuilder>(this TBuilder builder, params Role[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var resolution = invocation.HttpContext.Items[ItemKey] as ActorResolution;
            if (resolution?.Actor is null)
                return resolution?.RejectionCode is { } code
                    ? ApiError.Unauthenticated(code, "Your session is no longer valid. Sign in again.")
                    : ApiError.Unauthenticated();
            if (roles.Length > 0 && !roles.Contains(resolution.Actor.Role))
                return ApiError.Forbidden();
            return await next(invocation);
        });
}
