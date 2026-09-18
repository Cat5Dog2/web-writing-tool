using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Web.Security;

public sealed class GuestCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    ExternalApiExecutionContext executionContext) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (GuestIdentity.IsGuest(state.User))
        {
            executionContext.EnableGuestMode();
        }
    }
}
