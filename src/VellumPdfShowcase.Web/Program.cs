using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VellumPdfShowcase.Web;
using VellumPdfShowcase.Web.Assets;
using VellumPdfShowcase.Web.Model;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// MaxResponseContentBufferSize defaults to 2 GB, which is no bound at all from a
// tab's point of view. NOTE it does not bind on AssetLoader's own path, which
// reads headers first and counts the body itself; it bounds any OTHER caller that
// buffers a response through this client. This client will never legitimately
// fetch anything larger than one asset, so it says so.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    MaxResponseContentBufferSize = SpecLimits.MaxAssetBytes,
});

// Scoped rather than transient, so the cache inside it survives navigation
// between capability pages and each asset is fetched at most once per session.
builder.Services.AddScoped<AssetLoader>();

await builder.Build().RunAsync();
