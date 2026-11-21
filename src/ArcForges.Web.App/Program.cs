// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Probe;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ArcForges.Web.App.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The cookie session uses only the page's origin.
var origin = new ProbeOrigin(new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddSingleton(origin);
// Redirects are not followed by the page: a redirect answer is a refusal, not a second request to another place.
builder.Services.AddSingleton<HttpMessageHandler>(_ => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<SessionProbe>();
using (var settingsTransport = new HttpClientHandler { AllowAutoRedirect = false })
    builder.Services.AddSingleton(await DashboardConfigurationLoader.ReadAsync(origin.Value, settingsTransport));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DashboardGrpcTransport>();
builder.Services.AddSingleton<DashboardPasskeys>();
builder.Services.AddSingleton<DashboardCenter>();
builder.Services.AddSingleton<IDashboardCenter>(services => services.GetRequiredService<DashboardCenter>());
builder.Services.AddSingleton<IDashboardWorkspaces>(services => services.GetRequiredService<DashboardCenter>());
builder.Services.AddSingleton<IDashboardAccount>(services => services.GetRequiredService<DashboardCenter>());
builder.Services.AddSingleton<IDashboardSecurity>(services => services.GetRequiredService<DashboardCenter>());
builder.Services.AddSingleton<IDashboardSubscription>(services => services.GetRequiredService<DashboardCenter>());
builder.Services.AddTransient<IDashboardActivity, DashboardActivity>();

await builder.Build().RunAsync();
