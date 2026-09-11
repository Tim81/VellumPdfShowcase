using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VellumPdfShowcase.Web;
using VellumPdfShowcase.Web.Assets;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Scoped rather than transient, so the cache inside it survives navigation
// between capability pages and each asset is fetched at most once per session.
builder.Services.AddScoped<AssetLoader>();

await builder.Build().RunAsync();
